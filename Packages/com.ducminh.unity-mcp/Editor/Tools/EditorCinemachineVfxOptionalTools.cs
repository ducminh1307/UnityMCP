using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable] public sealed class CinemachineCreateInput
    {
        public string name = "Cinemachine Camera";
        public int? parentGameObjectInstanceId;
        public int? followGameObjectInstanceId;
        public int? lookAtGameObjectInstanceId;
        public int? priority;
        public bool apply;
    }

    [Serializable] public sealed class CinemachineCreateOutput
    {
        public bool dryRun;
        public bool created;
        public int? instanceId;
        public string name;
        public int? parentGameObjectInstanceId;
        public int? followGameObjectInstanceId;
        public int? lookAtGameObjectInstanceId;
        public int? priority;
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    [Serializable] public sealed class VfxGraphPropertyWrite
    {
        public string name;
        /// <summary>bool, int, float, vector2, vector3, or vector4.</summary>
        public string kind;
        public bool? boolValue;
        public int? intValue;
        public float? floatValue;
        public Vector2? vector2Value;
        public Vector3? vector3Value;
        public Vector4? vector4Value;
    }

    [Serializable] public sealed class VfxGraphSetInput
    {
        public int visualEffectInstanceId;
        public List<VfxGraphPropertyWrite> values = new List<VfxGraphPropertyWrite>();
        public bool apply;
    }

    [Serializable] public sealed class VfxGraphSetOutput
    {
        public bool dryRun;
        public bool changed;
        public int visualEffectInstanceId;
        public int propertyCount;
        public List<string> propertyNames = new List<string>();
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    /// <summary>
    /// Cinemachine 3 and VFX Graph integrations that deliberately have no compile-time reference
    /// to either optional package. RequiredType makes each tool invisible in targets where that
    /// package is absent; reflection is used only after the registry's availability gate passes.
    /// </summary>
    public static class EditorCinemachineVfxOptionalTools
    {
        private const int MaximumVfxWrites = 64;

        [UnityMcpTool("cinemachine-create", Description = "Create a Cinemachine 3 CinemachineCamera with optional parent, Follow, LookAt, and priority; dry-run unless apply is true.", Category = "camera-rendering-vfx", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, RequiredType = "Unity.Cinemachine.CinemachineCamera")]
        public static CinemachineCreateOutput CinemachineCreate(CinemachineCreateInput input, UnityMcpContext context)
        {
            var name = RequireName(input.name, "name");
            if (input.priority.HasValue && (input.priority.Value < -100000 || input.priority.Value > 100000))
                throw new ArgumentException("priority must be between -100000 and 100000.");
            var cameraType = RequireType("Unity.Cinemachine.CinemachineCamera");
            if (!typeof(Component).IsAssignableFrom(cameraType)) throw new InvalidOperationException("The installed CinemachineCamera type is not a Unity Component.");
            var parent = input.parentGameObjectInstanceId.HasValue ? RequireSceneGameObject(input.parentGameObjectInstanceId.Value, "parentGameObjectInstanceId") : null;
            var follow = input.followGameObjectInstanceId.HasValue ? RequireSceneGameObject(input.followGameObjectInstanceId.Value, "followGameObjectInstanceId") : null;
            var lookAt = input.lookAtGameObjectInstanceId.HasValue ? RequireSceneGameObject(input.lookAtGameObjectInstanceId.Value, "lookAtGameObjectInstanceId") : null;
            if (follow != null) ValidateWritableMember(cameraType, "Follow", typeof(Transform));
            if (lookAt != null) ValidateWritableMember(cameraType, "LookAt", typeof(Transform));
            if (input.priority.HasValue) ValidatePriorityMember(cameraType);

            if (context.DryRun)
            {
                return new CinemachineCreateOutput
                {
                    dryRun = true,
                    created = false,
                    name = name,
                    parentGameObjectInstanceId = parent == null ? (int?)null : parent.GetInstanceID(),
                    followGameObjectInstanceId = follow == null ? (int?)null : follow.GetInstanceID(),
                    lookAtGameObjectInstanceId = lookAt == null ? (int?)null : lookAt.GetInstanceID(),
                    priority = input.priority,
                    rollbackSupported = true,
                    journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "create-cinemachine-camera", after = name } }
                };
            }

            var created = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(created, "UnityMCP Create Cinemachine Camera");
            if (parent != null) Undo.SetTransformParent(created.transform, parent.transform, "UnityMCP Set Cinemachine Camera Parent");
            var component = Undo.AddComponent(created, cameraType);
            if (component == null) throw new InvalidOperationException("Unity could not add CinemachineCamera to the new GameObject.");
            if (follow != null) SetMember(component, "Follow", follow.transform);
            if (lookAt != null) SetMember(component, "LookAt", lookAt.transform);
            if (input.priority.HasValue) SetPriority(component, input.priority.Value);
            EditorUtility.SetDirty(component);
            EditorSceneManager.MarkSceneDirty(created.scene);
            return new CinemachineCreateOutput
            {
                dryRun = false,
                created = true,
                instanceId = component.GetInstanceID(),
                name = created.name,
                parentGameObjectInstanceId = parent == null ? (int?)null : parent.GetInstanceID(),
                followGameObjectInstanceId = follow == null ? (int?)null : follow.GetInstanceID(),
                lookAtGameObjectInstanceId = lookAt == null ? (int?)null : lookAt.GetInstanceID(),
                priority = input.priority,
                rollbackSupported = true,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "create-cinemachine-camera", after = component.GetInstanceID().ToString() } }
            };
        }

        [UnityMcpTool("vfxgraph-set", Description = "Set validated exposed bool, int, float, Vector2, Vector3, or Vector4 Visual Effect Graph overrides; dry-run unless apply is true.", Category = "camera-rendering-vfx", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, RequiredType = "UnityEngine.VFX.VisualEffect")]
        public static VfxGraphSetOutput VfxGraphSet(VfxGraphSetInput input, UnityMcpContext context)
        {
            var effectType = RequireType("UnityEngine.VFX.VisualEffect");
            var target = EditorUtility.InstanceIDToObject(input.visualEffectInstanceId);
            if (target == null || !effectType.IsInstanceOfType(target))
                throw new ArgumentException("visualEffectInstanceId must identify a loaded VisualEffect component.");
            var component = target as Component;
            if (component == null || !component.gameObject.scene.IsValid() || !component.gameObject.scene.isLoaded)
                throw new ArgumentException("visualEffectInstanceId must identify a VisualEffect component in a loaded scene.");
            var writes = ResolveVfxWrites(target, input.values);
            if (!context.DryRun)
            {
                Undo.RecordObject(target, "UnityMCP Set VFX Graph Properties");
                foreach (var write in writes) write.set.Invoke(target, new[] { (object)write.name, write.value });
                EditorUtility.SetDirty(target);
                EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
            }
            return new VfxGraphSetOutput
            {
                dryRun = context.DryRun,
                changed = !context.DryRun,
                visualEffectInstanceId = target.GetInstanceID(),
                propertyCount = writes.Count,
                propertyNames = writes.Select(write => write.name).ToList(),
                rollbackSupported = true,
                journal = writes.Select(write => new ChangeJournalEntry { operation = "set-vfx-" + write.kind, after = write.name }).ToList()
            };
        }

        private sealed class ResolvedVfxWrite
        {
            public string name;
            public string kind;
            public object value;
            public MethodInfo set;
        }

        private static List<ResolvedVfxWrite> ResolveVfxWrites(UnityEngine.Object target, List<VfxGraphPropertyWrite> values)
        {
            var source = values ?? new List<VfxGraphPropertyWrite>();
            if (source.Count == 0 || source.Count > MaximumVfxWrites)
                throw new ArgumentException("values must contain between 1 and " + MaximumVfxWrites + " property writes.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            var output = new List<ResolvedVfxWrite>();
            foreach (var sourceWrite in source)
            {
                if (sourceWrite == null) throw new ArgumentException("values may not contain null entries.");
                var name = RequireName(sourceWrite.name, "values.name");
                if (!names.Add(name)) throw new ArgumentException("Each Visual Effect Graph property may be written only once per call.");
                var kind = NormalizeVfxKind(sourceWrite.kind);
                var value = VfxValue(sourceWrite, kind);
                var suffix = VfxMethodSuffix(kind);
                var has = target.GetType().GetMethod("Has" + suffix, BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
                var set = target.GetType().GetMethod("Set" + suffix, BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string), value.GetType() }, null);
                if (has == null || set == null) throw new InvalidOperationException("This Visual Effect Graph package does not expose the expected " + suffix + " property API.");
                if (!(has.Invoke(target, new object[] { name }) is bool exists) || !exists)
                    throw new ArgumentException("The Visual Effect Graph does not expose a " + kind + " property named '" + name + "'.");
                output.Add(new ResolvedVfxWrite { name = name, kind = kind, value = value, set = set });
            }
            return output;
        }

        private static string NormalizeVfxKind(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "bool":
                case "int":
                case "float":
                case "vector2":
                case "vector3":
                case "vector4": return (value ?? string.Empty).Trim().ToLowerInvariant();
                default: throw new ArgumentException("values.kind must be bool, int, float, vector2, vector3, or vector4.");
            }
        }

        private static string VfxMethodSuffix(string kind)
        {
            switch (kind)
            {
                case "bool": return "Bool";
                case "int": return "Int";
                case "float": return "Float";
                case "vector2": return "Vector2";
                case "vector3": return "Vector3";
                case "vector4": return "Vector4";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static object VfxValue(VfxGraphPropertyWrite input, string kind)
        {
            switch (kind)
            {
                case "bool":
                    if (!input.boolValue.HasValue) throw new ArgumentException("boolValue is required for kind=bool.");
                    return input.boolValue.Value;
                case "int":
                    if (!input.intValue.HasValue) throw new ArgumentException("intValue is required for kind=int.");
                    return input.intValue.Value;
                case "float":
                    if (!input.floatValue.HasValue || !IsFinite(input.floatValue.Value)) throw new ArgumentException("floatValue must be finite for kind=float.");
                    return input.floatValue.Value;
                case "vector2":
                    if (!input.vector2Value.HasValue || !IsFinite(input.vector2Value.Value)) throw new ArgumentException("vector2Value must contain finite values for kind=vector2.");
                    return input.vector2Value.Value;
                case "vector3":
                    if (!input.vector3Value.HasValue || !IsFinite(input.vector3Value.Value)) throw new ArgumentException("vector3Value must contain finite values for kind=vector3.");
                    return input.vector3Value.Value;
                case "vector4":
                    if (!input.vector4Value.HasValue || !IsFinite(input.vector4Value.Value)) throw new ArgumentException("vector4Value must contain finite values for kind=vector4.");
                    return input.vector4Value.Value;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static void ValidateWritableMember(Type type, string name, Type expectedType)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite && property.PropertyType == expectedType) return;
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null && field.FieldType == expectedType) return;
            throw new InvalidOperationException("The installed CinemachineCamera type does not expose writable " + expectedType.Name + " member '" + name + "'.");
        }

        private static void ValidatePriorityMember(Type type)
        {
            var field = type.GetField("Priority", BindingFlags.Public | BindingFlags.Instance);
            var property = type.GetProperty("Priority", BindingFlags.Public | BindingFlags.Instance);
            var priorityType = field?.FieldType ?? property?.PropertyType;
            if (priorityType == null || (property != null && !property.CanWrite))
                throw new InvalidOperationException("The installed CinemachineCamera type does not expose a writable PrioritySettings member.");
            var value = priorityType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            var enabled = priorityType.GetField("Enabled", BindingFlags.Public | BindingFlags.Instance) ?? (MemberInfo)priorityType.GetProperty("Enabled", BindingFlags.Public | BindingFlags.Instance);
            if (value == null || !value.CanWrite || value.PropertyType != typeof(int) || enabled == null)
                throw new InvalidOperationException("The installed Cinemachine PrioritySettings type is not compatible with this tool.");
        }

        private static void SetPriority(object target, int priority)
        {
            var type = target.GetType();
            var field = type.GetField("Priority", BindingFlags.Public | BindingFlags.Instance);
            var property = type.GetProperty("Priority", BindingFlags.Public | BindingFlags.Instance);
            var priorityValue = field != null ? field.GetValue(target) : property?.GetValue(target, null);
            if (priorityValue == null) throw new InvalidOperationException("The installed CinemachineCamera Priority member could not be read.");
            var priorityType = priorityValue.GetType();
            var valueProperty = priorityType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            if (valueProperty == null || !valueProperty.CanWrite || valueProperty.PropertyType != typeof(int))
                throw new InvalidOperationException("The installed Cinemachine PrioritySettings member has no writable int Value property.");
            valueProperty.SetValue(priorityValue, priority, null);
            var enabledField = priorityType.GetField("Enabled", BindingFlags.Public | BindingFlags.Instance);
            var enabledProperty = priorityType.GetProperty("Enabled", BindingFlags.Public | BindingFlags.Instance);
            if (enabledField != null && enabledField.FieldType == typeof(bool)) enabledField.SetValue(priorityValue, true);
            else if (enabledProperty != null && enabledProperty.CanWrite && enabledProperty.PropertyType == typeof(bool)) enabledProperty.SetValue(priorityValue, true, null);
            else throw new InvalidOperationException("The installed Cinemachine PrioritySettings member has no writable bool Enabled member.");
            if (field != null) field.SetValue(target, priorityValue);
            else if (property != null && property.CanWrite) property.SetValue(target, priorityValue, null);
            else throw new InvalidOperationException("The installed CinemachineCamera Priority member is not writable.");
        }

        private static void SetMember(object target, string name, object value)
        {
            var type = target.GetType();
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite) { property.SetValue(target, value, null); return; }
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null) { field.SetValue(target, value); return; }
            throw new InvalidOperationException(type.FullName + " has no writable member '" + name + "'.");
        }

        private static GameObject RequireSceneGameObject(int instanceId, string inputName)
        {
            var value = EditorUtility.InstanceIDToObject(instanceId);
            var gameObject = value as GameObject ?? (value as Component)?.gameObject;
            if (gameObject == null || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded)
                throw new ArgumentException(inputName + " must identify a GameObject or Component in a loaded scene.");
            return gameObject;
        }

        private static Type RequireType(string fullName)
        {
            var result = Type.GetType(fullName, false);
            if (result != null) return result;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    result = assembly.GetType(fullName, false);
                    if (result != null) return result;
                }
                catch { }
            }
            throw new InvalidOperationException("The required optional Unity type is unavailable: " + fullName);
        }

        private static string RequireName(string value, string inputName)
        {
            var result = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(result) || result.Length > 128 || result.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException(inputName + " must be a non-empty name of at most 128 characters.");
            return result;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);
        private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        private static bool IsFinite(Vector4 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
    }
}
