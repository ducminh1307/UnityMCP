#if DM_CINEMACHINE_PRESENT || DM_VFXGRAPH_PRESENT
#pragma warning disable UAC0005 // Editor tool reflection scans currently loaded optional package types.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
#if DM_CINEMACHINE_PRESENT
    [Serializable]
    public sealed class CinemachineCreateOutput
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
#endif

#if DM_VFXGRAPH_PRESENT
    [Serializable]
    public sealed class VfxGraphPropertyWrite
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

    [Serializable]
    public sealed class VfxGraphSetOutput
    {
        public bool dryRun;
        public bool changed;
        public int visualEffectInstanceId;
        public int propertyCount;
        public List<string> propertyNames = new List<string>();
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }
#endif

    /// <summary>
    /// Cinemachine 3 and VFX Graph integrations that use reflection inside DM_CINEMACHINE_PRESENT
    /// and DM_VFXGRAPH_PRESENT so neither optional package requires an assembly reference.
    /// </summary>
    public static class EditorCinemachineVfxOptionalTools
    {
#if DM_CINEMACHINE_PRESENT
        private const string CinemachineGroup = "cinemachine";

        [CliGroupCommand(
            CinemachineGroup,
            "Create a Cinemachine 3 CinemachineCamera with optional parent, Follow, LookAt, and priority.",
            Name = "create",
            Effect = CommandEffect.Additive,
            Tags = new[] { "cinemachine", "camera" })]
        internal static ExitCode CinemachineCreate(
            CommandContext cmd,
            [CliArg("name", "Name for the new CinemachineCamera GameObject.")] string name = "Cinemachine Camera",
            [CliArg("parent-id", "Optional parent GameObject handle (globalId, hierarchyPath, or instanceId).")] ObjectRef parentId = null,
            [CliArg("follow-id", "Optional Follow target GameObject handle (globalId, hierarchyPath, or instanceId).")] ObjectRef followId = null,
            [CliArg("look-at-id", "Optional LookAt target GameObject handle (globalId, hierarchyPath, or instanceId).")] ObjectRef lookAtId = null,
            [CliArg("priority", "Optional CinemachineCamera priority (-100000 to 100000).")] int? priority = null,
            [CliArg("dry-run", "If true, validate inputs and preview without creating the camera.")] bool dryRun = false)
        {
            return Execute(cmd, () => CinemachineCreateCore(name, parentId, followId, lookAtId, priority, dryRun));
        }

        internal static CinemachineCreateOutput CinemachineCreateCore(
            string name = "Cinemachine Camera",
            ObjectRef parentId = null,
            ObjectRef followId = null,
            ObjectRef lookAtId = null,
            int? priority = null,
            bool dryRun = false)
        {
            var resolvedName = RequireName(name, "name");
            if (priority.HasValue && (priority.Value < -100000 || priority.Value > 100000))
                throw new ArgumentException("priority must be between -100000 and 100000.");

            var cameraType = RequireType("Unity.Cinemachine.CinemachineCamera");
            if (!typeof(Component).IsAssignableFrom(cameraType))
                throw new InvalidOperationException("The installed CinemachineCamera type is not a Unity Component.");

            var parent = parentId != null && !parentId.IsEmpty ? RequireSceneGameObject(parentId, "parent-id") : null;
            var follow = followId != null && !followId.IsEmpty ? RequireSceneGameObject(followId, "follow-id") : null;
            var lookAt = lookAtId != null && !lookAtId.IsEmpty ? RequireSceneGameObject(lookAtId, "look-at-id") : null;

            if (follow != null) ValidateWritableMember(cameraType, "Follow", typeof(Transform));
            if (lookAt != null) ValidateWritableMember(cameraType, "LookAt", typeof(Transform));
            if (priority.HasValue) ValidatePriorityMember(cameraType);

            if (dryRun)
            {
                return new CinemachineCreateOutput
                {
                    dryRun = true,
                    created = false,
                    name = resolvedName,
                    parentGameObjectInstanceId = parent == null ? (int?)null : UnityMcpObjectId.Get(parent),
                    followGameObjectInstanceId = follow == null ? (int?)null : UnityMcpObjectId.Get(follow),
                    lookAtGameObjectInstanceId = lookAt == null ? (int?)null : UnityMcpObjectId.Get(lookAt),
                    priority = priority,
                    rollbackSupported = true,
                    journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "create-cinemachine-camera", after = resolvedName } }
                };
            }

            GameObject created;
            Component component;
            using (new AuthoringUndoScope("UnityMCP Create Cinemachine Camera"))
            {
                created = new GameObject(resolvedName);
                Undo.RegisterCreatedObjectUndo(created, "UnityMCP Create Cinemachine Camera");
                if (parent != null) Undo.SetTransformParent(created.transform, parent.transform, "UnityMCP Set Cinemachine Camera Parent");
                component = Undo.AddComponent(created, cameraType);
                if (component == null) throw new InvalidOperationException("Unity could not add CinemachineCamera to the new GameObject.");
                if (follow != null) SetMember(component, "Follow", follow.transform);
                if (lookAt != null) SetMember(component, "LookAt", lookAt.transform);
                if (priority.HasValue) SetPriority(component, priority.Value);
                EditorUtility.SetDirty(component);
                EditorSceneManager.MarkSceneDirty(created.scene);
            }

            return new CinemachineCreateOutput
            {
                dryRun = false,
                created = true,
                instanceId = UnityMcpObjectId.Get(component),
                name = created.name,
                parentGameObjectInstanceId = parent == null ? (int?)null : UnityMcpObjectId.Get(parent),
                followGameObjectInstanceId = follow == null ? (int?)null : UnityMcpObjectId.Get(follow),
                lookAtGameObjectInstanceId = lookAt == null ? (int?)null : UnityMcpObjectId.Get(lookAt),
                priority = priority,
                rollbackSupported = true,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "create-cinemachine-camera", after = UnityMcpObjectId.Get(component).ToString(CultureInfo.InvariantCulture) } }
            };
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

        private static GameObject RequireSceneGameObject(ObjectRef handle, string inputName)
        {
            var value = ResolveObject(handle, inputName);
            var gameObject = value as GameObject ?? (value as Component)?.gameObject;
            if (gameObject == null || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded)
                throw new ArgumentException(inputName + " must identify a GameObject or Component in a loaded scene.");
            return gameObject;
        }
#endif

#if DM_VFXGRAPH_PRESENT
        private const string VfxGraphGroup = "vfxgraph";
        private const int MaximumVfxWrites = 64;

        [CliGroupCommand(
            VfxGraphGroup,
            "Set validated exposed bool, int, float, Vector2, Vector3, or Vector4 Visual Effect Graph overrides.",
            Name = "set",
            Effect = CommandEffect.Idempotent,
            Tags = new[] { "vfx", "vfxgraph" })]
        internal static ExitCode VfxGraphSet(
            CommandContext cmd,
            [CliArg("visual-effect-id", "Handle or instanceId of a loaded VisualEffect component.", Required = true)] ObjectRef visualEffectId,
            [CliArg("values-json", "JSON array of {name, kind, boolValue|intValue|floatValue|vector2Value|vector3Value|vector4Value} overrides.", Required = true)] string valuesJson,
            [CliArg("dry-run", "If true, validate target and overrides without modifying the VisualEffect.")] bool dryRun = false)
        {
            return Execute(cmd, () => VfxGraphSetCore(visualEffectId, ParseVfxWritesJson(valuesJson), dryRun));
        }

        internal static VfxGraphSetOutput VfxGraphSetCore(
            ObjectRef visualEffectId,
            List<VfxGraphPropertyWrite> values,
            bool dryRun = false)
        {
            var effectType = RequireType("UnityEngine.VFX.VisualEffect");
            var resolved = ResolveObject(visualEffectId, "visual-effect-id");
            var target = effectType.IsInstanceOfType(resolved)
                ? resolved
                : (resolved as GameObject)?.GetComponent(effectType);

            if (target == null || !effectType.IsInstanceOfType(target))
                throw new ArgumentException("visual-effect-id must identify a loaded VisualEffect component.");

            var component = target as Component;
            if (component == null || !component.gameObject.scene.IsValid() || !component.gameObject.scene.isLoaded)
                throw new ArgumentException("visual-effect-id must identify a VisualEffect component in a loaded scene.");

            var writes = ResolveVfxWrites(target, values);
            if (!dryRun)
            {
                using (new AuthoringUndoScope("UnityMCP Set VFX Graph Properties"))
                {
                    Undo.RecordObject(target, "UnityMCP Set VFX Graph Properties");
                    foreach (var write in writes) write.set.Invoke(target, new[] { (object)write.name, write.value });
                    EditorUtility.SetDirty(target);
                    EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
                }
            }

            return new VfxGraphSetOutput
            {
                dryRun = dryRun,
                changed = !dryRun,
                visualEffectInstanceId = UnityMcpObjectId.Get(target),
                propertyCount = writes.Count,
                propertyNames = writes.Select(write => write.name).ToList(),
                rollbackSupported = true,
                journal = writes.Select(write => new ChangeJournalEntry { operation = "set-vfx-" + write.kind, after = write.name }).ToList()
            };
        }

        internal static List<VfxGraphPropertyWrite> ParseVfxWritesJson(string valuesJson)
        {
            if (string.IsNullOrWhiteSpace(valuesJson))
                throw new ArgumentException("values-json is required.");

            JArray array;
            try
            {
                if (!(JToken.Parse(valuesJson) is JArray parsed))
                    throw new ArgumentException("values-json must be a JSON array of property overrides.");
                array = parsed;
            }
            catch (ArgumentException) { throw; }
            catch (Exception ex)
            {
                throw new ArgumentException("values-json is not valid JSON: " + ex.Message, ex);
            }

            var result = new List<VfxGraphPropertyWrite>(array.Count);
            foreach (var item in array)
            {
                if (!(item is JObject obj))
                    throw new ArgumentException("values-json entries must be JSON objects.");

                var write = new VfxGraphPropertyWrite
                {
                    name = ReadJsonString(obj, "name"),
                    kind = ReadJsonString(obj, "kind"),
                    boolValue = ReadJsonBool(obj, "boolValue"),
                    intValue = ReadJsonInt(obj, "intValue"),
                    floatValue = ReadJsonFloat(obj, "floatValue"),
                    vector2Value = ReadJsonVector2(obj, "vector2Value"),
                    vector3Value = ReadJsonVector3(obj, "vector3Value"),
                    vector4Value = ReadJsonVector4(obj, "vector4Value")
                };
                result.Add(write);
            }

            return result;
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

        private static string ReadJsonString(JObject obj, string key)
        {
            var token = obj[key];
            return token == null || token.Type == JTokenType.Null ? null : token.Value<string>();
        }

        private static bool? ReadJsonBool(JObject obj, string key)
        {
            var token = obj[key];
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type != JTokenType.Boolean) throw new ArgumentException(key + " must be a boolean.");
            return token.Value<bool>();
        }

        private static int? ReadJsonInt(JObject obj, string key)
        {
            var token = obj[key];
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type != JTokenType.Integer) throw new ArgumentException(key + " must be an integer.");
            return token.Value<int>();
        }

        private static float? ReadJsonFloat(JObject obj, string key)
        {
            var token = obj[key];
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) throw new ArgumentException(key + " must be a number.");
            return token.Value<float>();
        }

        private static Vector2? ReadJsonVector2(JObject obj, string key)
        {
            var components = ReadJsonVectorComponents(obj, key, 2);
            return components == null ? (Vector2?)null : new Vector2(components[0], components[1]);
        }

        private static Vector3? ReadJsonVector3(JObject obj, string key)
        {
            var components = ReadJsonVectorComponents(obj, key, 3);
            return components == null ? (Vector3?)null : new Vector3(components[0], components[1], components[2]);
        }

        private static Vector4? ReadJsonVector4(JObject obj, string key)
        {
            var components = ReadJsonVectorComponents(obj, key, 4);
            return components == null ? (Vector4?)null : new Vector4(components[0], components[1], components[2], components[3]);
        }

        private static float[] ReadJsonVectorComponents(JObject obj, string key, int count)
        {
            var token = obj[key];
            if (token == null || token.Type == JTokenType.Null) return null;

            if (token is JArray array)
            {
                if (array.Count != count)
                    throw new ArgumentException(key + " array must have exactly " + count + " components.");
                var values = new float[count];
                for (var i = 0; i < count; i++)
                {
                    if (array[i].Type != JTokenType.Integer && array[i].Type != JTokenType.Float)
                        throw new ArgumentException(key + " components must be numbers.");
                    values[i] = array[i].Value<float>();
                }

                return values;
            }

            if (token is JObject vec)
            {
                var fields = new[] { "x", "y", "z", "w" };
                var values = new float[count];
                for (var i = 0; i < count; i++)
                {
                    if (vec.TryGetValue(fields[i], StringComparison.OrdinalIgnoreCase, out var component) && component.Type != JTokenType.Null)
                    {
                        if (component.Type != JTokenType.Integer && component.Type != JTokenType.Float)
                            throw new ArgumentException(key + "." + fields[i] + " must be a number.");
                        values[i] = component.Value<float>();
                    }
                }

                return values;
            }

            throw new ArgumentException(key + " must be an array or {x,y,...} object.");
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);
        private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        private static bool IsFinite(Vector4 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
#endif

        private static ExitCode Execute(CommandContext cmd, Func<object> action)
        {
            try
            {
                cmd.Result.Write(action());
                return ExitCode.Success;
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                cmd.Log.Error(inner.Message);
                return inner is ArgumentException ? ExitCode.InvalidUsage : ExitCode.Failed;
            }
            catch (ArgumentException ex)
            {
                cmd.Log.Error(ex.Message);
                return ExitCode.InvalidUsage;
            }
            catch (Exception ex)
            {
                cmd.Log.Error(ex.Message);
                return ExitCode.Failed;
            }
        }

        private static UnityEngine.Object ResolveObject(ObjectRef handle, string paramName)
        {
            if (handle == null || handle.IsEmpty)
                throw new ArgumentException(paramName + " is required.");

            if (ObjectResolver.TryResolve(handle, out var obj, out var error) && obj != null)
                return obj;

            var text = handle.ToString();
            if (!string.IsNullOrEmpty(text) && text.StartsWith("instanceId:", StringComparison.Ordinal)
                && int.TryParse(text.Substring("instanceId:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacyId))
            {
                var legacy = UnityMcpEditorObjectId.Resolve(legacyId);
                if (legacy != null) return legacy;
            }

            throw new ArgumentException(paramName + " could not be resolved: " + (error ?? "not found."));
        }

        private static Type RequireType(string fullName)
        {
            return EditorCommandHelpers.TryResolveOptionalType(fullName)
                ?? throw new InvalidOperationException("The required optional Unity type is unavailable: " + fullName);
        }

        private static string RequireName(string value, string inputName)
        {
            var result = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(result) || result.Length > 128 || result.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException(inputName + " must be a non-empty name of at most 128 characters.");
            return result;
        }
    }
}
#endif
