using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DucMinh.UnityMcp
{
    [Serializable] public sealed class SceneSetActiveInput { public string scene; public bool apply; }
    [Serializable] public sealed class GameObjectDuplicateInput { public int? instanceId; public string path; public string name; public int? parentInstanceId; public string parentPath; public bool apply; }
    [Serializable] public sealed class ComponentPropertyWrite { public string property; public string valueJson; }
    [Serializable] public sealed class ComponentSetPropertiesInput { public int? instanceId; public string path; public string type; public int componentIndex; public List<ComponentPropertyWrite> values = new List<ComponentPropertyWrite>(); public bool apply; }
    [Serializable] public sealed class TypeSchemaInput { public string type; }
    [Serializable] public sealed class TypeSchemaOutput { public string type; public string schemaJson; public List<MemberSchemaInfo> members = new List<MemberSchemaInfo>(); }

    [Serializable] public sealed class PhysicsSettingsOutput
    {
        public Vector3 gravity;
        public float defaultContactOffset;
        public float bounceThreshold;
        public int defaultSolverIterations;
        public int defaultSolverVelocityIterations;
        public bool queriesHitTriggers;
        public bool queriesHitBackfaces;
    }

    [Serializable] public sealed class PhysicsSettingsInput
    {
        public Vector3? gravity;
        public float? defaultContactOffset;
        public float? bounceThreshold;
        public int? defaultSolverIterations;
        public int? defaultSolverVelocityIterations;
        public bool? queriesHitTriggers;
        public bool? queriesHitBackfaces;
        public bool apply;
    }

    [Serializable] public sealed class PhysicsCollisionLayerInfo { public int layer; public string name; public List<int> ignoredWith = new List<int>(); }
    [Serializable] public sealed class PhysicsCollisionMatrixOutput { public List<PhysicsCollisionLayerInfo> layers = new List<PhysicsCollisionLayerInfo>(); }
    [Serializable] public sealed class PhysicsCollisionMatrixSetInput { public int layerA; public int layerB; public bool ignore; public bool apply; }
    [Serializable] public sealed class PhysicsRaycastInput { public Vector3 origin; public Vector3 direction = Vector3.forward; public float maxDistance = 100f; public int layerMask = -1; public bool includeTriggers = true; }
    [Serializable] public sealed class PhysicsRaycastOutput { public bool hit; public int colliderInstanceId; public string gameObjectPath; public Vector3 point; public Vector3 normal; public float distance; }
    [Serializable] public sealed class PhysicsOverlapInput { public string shape = "sphere"; public Vector3 center; public float radius = 1f; public Vector3 halfExtents = Vector3.one; public Quaternion? orientation; public int layerMask = -1; public bool includeTriggers = true; public int limit = 100; }
    [Serializable] public sealed class PhysicsOverlapOutput { public List<GameObjectSummary> matches = new List<GameObjectSummary>(); public bool truncated; }

    [Serializable] public sealed class AudioSourceSelector { public int? instanceId; public string path; public int componentIndex; }
    [Serializable] public sealed class AudioClipInfoInput { public int? instanceId; public string path; public int componentIndex; }
    [Serializable] public sealed class AudioClipInfoOutput { public bool hasClip; public string name; public float length; public int samples; public int frequency; public int channels; public string loadType; }
    [Serializable] public sealed class AudioSourceCreateInput { public int? instanceId; public string path; public string resourcePath; public bool? loop; public float? volume; public bool? playOnAwake; public bool apply; }
    [Serializable] public sealed class AudioSourceSetInput { public int? instanceId; public string path; public int componentIndex; public string resourcePath; public float? volume; public float? pitch; public bool? loop; public bool? mute; public bool? playOnAwake; public float? spatialBlend; public bool? play; public bool apply; }

    [Serializable] public sealed class CameraSelector { public int? instanceId; public string path; }
    [Serializable] public sealed class CameraInfo { public int instanceId; public string name; public string path; public bool enabled; public bool orthographic; public float fieldOfView; public float orthographicSize; public float nearClipPlane; public float farClipPlane; public int cullingMask; public Color backgroundColor; public Rect rect; }
    [Serializable] public sealed class CameraListOutput { public List<CameraInfo> cameras = new List<CameraInfo>(); }
    [Serializable] public sealed class CameraSetInput { public int? instanceId; public string path; public bool? enabled; public bool? orthographic; public float? fieldOfView; public float? orthographicSize; public float? nearClipPlane; public float? farClipPlane; public int? cullingMask; public Color? backgroundColor; public Rect? rect; public bool apply; }

    [Serializable] public sealed class ParticleSetInput { public int? instanceId; public string path; public int componentIndex; public float? startLifetime; public float? startSpeed; public float? startSize; public Color? startColor; public bool? looping; public bool? play; public bool apply; }
    [Serializable] public sealed class RuntimeQuitInput { public bool apply; }

    /// <summary>Additional engine-only tools. They contain no editor-only or optional package APIs.</summary>
    public static class RuntimeExpansionTools
    {
        [UnityMcpTool("scene-set-active", Description = "Set the active loaded scene; dry-run unless apply is true.", Category = "scene", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput SceneSetActive(SceneSetActiveInput input, UnityMcpContext context)
        {
            var scene = RuntimeCoreTools.FindScene(input.scene);
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("Scene was not found or is not loaded.");
            if (!context.DryRun && !SceneManager.SetActiveScene(scene)) throw new InvalidOperationException("Unity could not make the scene active.");
            return RuntimeCoreTools.Change(context, "Set active scene to '" + scene.name + "'.");
        }

        [UnityMcpTool("gameobject-duplicate", Description = "Duplicate a GameObject; dry-run unless apply is true.", Category = "gameobject", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput GameObjectDuplicate(GameObjectDuplicateInput input, UnityMcpContext context)
        {
            var source = RuntimeCoreTools.RequireGameObject(input.instanceId, input.path);
            var parent = input.parentInstanceId.HasValue || !string.IsNullOrWhiteSpace(input.parentPath)
                ? RuntimeCoreTools.RequireGameObject(input.parentInstanceId, input.parentPath).transform
                : source.transform.parent;
            if (context.DryRun) return RuntimeCoreTools.Change(context, "Duplicate GameObject '" + RuntimeCoreTools.HierarchyPath(source) + "'.", source.GetInstanceID());
            var clone = UnityEngine.Object.Instantiate(source, parent, false);
            clone.name = string.IsNullOrWhiteSpace(input.name) ? source.name : input.name.Trim();
            UnityMcpUndo.RegisterCreated(clone, "UnityMCP Duplicate GameObject");
            return RuntimeCoreTools.Change(context, "Duplicated GameObject '" + RuntimeCoreTools.HierarchyPath(source) + "'.", clone.GetInstanceID());
        }

        [UnityMcpTool("component-set-properties", Description = "Set multiple public Component fields or properties; dry-run unless apply is true.", Category = "component", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput ComponentSetProperties(ComponentSetPropertiesInput input, UnityMcpContext context)
        {
            var target = RuntimeCoreTools.RequireGameObject(input.instanceId, input.path);
            var components = RuntimeCoreTools.RequireComponents(target, input.type);
            if (input.componentIndex < 0 || input.componentIndex >= components.Length) throw new ArgumentOutOfRangeException(nameof(input.componentIndex));
            if (input.values == null || input.values.Count == 0) throw new ArgumentException("values must contain at least one property write.");
            var component = components[input.componentIndex];
            var writes = input.values.Select(value => ResolveComponentWrite(component, value)).ToList();
            if (!context.DryRun)
            {
                var undoGroup = UnityMcpUndo.Begin("UnityMCP Set Component Properties");
                try
                {
                    UnityMcpUndo.Record(component, "UnityMCP Set Component Properties");
                    foreach (var write in writes) write.Apply(component);
                }
                finally { UnityMcpUndo.End(undoGroup); }
            }
            return RuntimeCoreTools.Change(context, "Set " + writes.Count + " properties on " + component.GetType().FullName + ".", component.GetInstanceID());
        }

        [UnityMcpTool("type-schema", Description = "Describe public fields and properties for a supported CLR or Unity type.", Category = "component", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead)]
        public static TypeSchemaOutput TypeSchema(TypeSchemaInput input)
        {
            var type = RequireType(input.type);
            var output = new TypeSchemaOutput { type = type.FullName };
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public).OrderBy(field => field.Name))
                if (!field.IsNotSerialized) output.members.Add(new MemberSchemaInfo { name = field.Name, type = TypeName(field.FieldType), writable = !field.IsInitOnly, kind = "field" });
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(property => property.GetIndexParameters().Length == 0).OrderBy(property => property.Name))
                output.members.Add(new MemberSchemaInfo { name = property.Name, type = TypeName(property.PropertyType), writable = property.CanWrite, kind = "property" });
            output.schemaJson = JsonConvert.SerializeObject(new { type = "object", title = type.FullName, members = output.members });
            return output;
        }

        [UnityMcpTool("physics-settings-get", Description = "Read global 3D physics settings.", Category = "physics", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead)]
        public static PhysicsSettingsOutput PhysicsSettingsGet(EmptyInput input) => new PhysicsSettingsOutput
        {
            gravity = Physics.gravity,
            defaultContactOffset = Physics.defaultContactOffset,
            bounceThreshold = Physics.bounceThreshold,
            defaultSolverIterations = Physics.defaultSolverIterations,
            defaultSolverVelocityIterations = Physics.defaultSolverVelocityIterations,
            queriesHitTriggers = Physics.queriesHitTriggers,
            queriesHitBackfaces = Physics.queriesHitBackfaces,
        };

        [UnityMcpTool("physics-settings-set", Description = "Set global 3D physics settings; dry-run unless apply is true.", Category = "physics", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput PhysicsSettingsSet(PhysicsSettingsInput input, UnityMcpContext context)
        {
            if (input.defaultContactOffset.HasValue && input.defaultContactOffset.Value <= 0f) throw new ArgumentOutOfRangeException(nameof(input.defaultContactOffset));
            if (input.defaultSolverIterations.HasValue && input.defaultSolverIterations.Value < 1) throw new ArgumentOutOfRangeException(nameof(input.defaultSolverIterations));
            if (input.defaultSolverVelocityIterations.HasValue && input.defaultSolverVelocityIterations.Value < 1) throw new ArgumentOutOfRangeException(nameof(input.defaultSolverVelocityIterations));
            if (!context.DryRun)
            {
                if (input.gravity.HasValue) Physics.gravity = input.gravity.Value;
                if (input.defaultContactOffset.HasValue) Physics.defaultContactOffset = input.defaultContactOffset.Value;
                if (input.bounceThreshold.HasValue) Physics.bounceThreshold = input.bounceThreshold.Value;
                if (input.defaultSolverIterations.HasValue) Physics.defaultSolverIterations = input.defaultSolverIterations.Value;
                if (input.defaultSolverVelocityIterations.HasValue) Physics.defaultSolverVelocityIterations = input.defaultSolverVelocityIterations.Value;
                if (input.queriesHitTriggers.HasValue) Physics.queriesHitTriggers = input.queriesHitTriggers.Value;
                if (input.queriesHitBackfaces.HasValue) Physics.queriesHitBackfaces = input.queriesHitBackfaces.Value;
            }
            return RuntimeCoreTools.Change(context, "Updated global physics settings.");
        }

        [UnityMcpTool("physics-collision-matrix-get", Description = "Read the 3D physics layer collision matrix.", Category = "physics", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead)]
        public static PhysicsCollisionMatrixOutput PhysicsCollisionMatrixGet(EmptyInput input)
        {
            var output = new PhysicsCollisionMatrixOutput();
            for (var layer = 0; layer < 32; layer++)
            {
                var info = new PhysicsCollisionLayerInfo { layer = layer, name = LayerMask.LayerToName(layer) };
                for (var other = 0; other < 32; other++) if (Physics.GetIgnoreLayerCollision(layer, other)) info.ignoredWith.Add(other);
                output.layers.Add(info);
            }
            return output;
        }

        [UnityMcpTool("physics-collision-matrix-set", Description = "Set one 3D physics layer collision relationship; dry-run unless apply is true.", Category = "physics", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput PhysicsCollisionMatrixSet(PhysicsCollisionMatrixSetInput input, UnityMcpContext context)
        {
            ValidateLayer(input.layerA, nameof(input.layerA));
            ValidateLayer(input.layerB, nameof(input.layerB));
            if (!context.DryRun) Physics.IgnoreLayerCollision(input.layerA, input.layerB, input.ignore);
            return RuntimeCoreTools.Change(context, (input.ignore ? "Ignore" : "Enable") + " collisions between layers " + input.layerA + " and " + input.layerB + ".");
        }

        [UnityMcpTool("physics-raycast", Description = "Perform a bounded 3D physics raycast.", Category = "physics", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead)]
        public static PhysicsRaycastOutput PhysicsRaycast(PhysicsRaycastInput input)
        {
            if (input.maxDistance < 0 || float.IsNaN(input.maxDistance) || float.IsInfinity(input.maxDistance)) throw new ArgumentOutOfRangeException(nameof(input.maxDistance));
            if (input.direction.sqrMagnitude < 0.000001f) throw new ArgumentException("direction must be non-zero.");
            var trigger = input.includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;
            if (!Physics.Raycast(input.origin, input.direction.normalized, out var hit, input.maxDistance, input.layerMask, trigger)) return new PhysicsRaycastOutput();
            return new PhysicsRaycastOutput { hit = true, colliderInstanceId = hit.collider.GetInstanceID(), gameObjectPath = RuntimeCoreTools.HierarchyPath(hit.collider.gameObject), point = hit.point, normal = hit.normal, distance = hit.distance };
        }

        [UnityMcpTool("physics-overlap", Description = "Perform a bounded sphere or box overlap query.", Category = "physics", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead)]
        public static PhysicsOverlapOutput PhysicsOverlap(PhysicsOverlapInput input)
        {
            var limit = Mathf.Clamp(input.limit, 1, 1000);
            var trigger = input.includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;
            Collider[] colliders;
            if (string.Equals(input.shape, "box", StringComparison.OrdinalIgnoreCase))
            {
                if (input.halfExtents.x < 0 || input.halfExtents.y < 0 || input.halfExtents.z < 0) throw new ArgumentOutOfRangeException(nameof(input.halfExtents));
                colliders = Physics.OverlapBox(input.center, input.halfExtents, input.orientation ?? Quaternion.identity, input.layerMask, trigger);
            }
            else if (string.Equals(input.shape, "sphere", StringComparison.OrdinalIgnoreCase))
            {
                if (input.radius < 0) throw new ArgumentOutOfRangeException(nameof(input.radius));
                colliders = Physics.OverlapSphere(input.center, input.radius, input.layerMask, trigger);
            }
            else throw new ArgumentException("shape must be 'sphere' or 'box'.");
            var output = new PhysicsOverlapOutput();
            foreach (var collider in colliders.Where(value => value != null).OrderBy(value => value.GetInstanceID()))
            {
                if (output.matches.Any(match => match.instanceId == collider.gameObject.GetInstanceID())) continue;
                if (output.matches.Count >= limit) { output.truncated = true; break; }
                output.matches.Add(RuntimeCoreTools.Summary(collider.gameObject));
            }
            return output;
        }

        [UnityMcpTool("audio-clip-info", Description = "Read the AudioClip assigned to an AudioSource.", Category = "audio", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead)]
        public static AudioClipInfoOutput AudioClipInfo(AudioClipInfoInput input)
        {
            var source = RequireAudioSource(input.instanceId, input.path, input.componentIndex);
            var clip = source.clip;
            return clip == null ? new AudioClipInfoOutput() : new AudioClipInfoOutput { hasClip = true, name = clip.name, length = clip.length, samples = clip.samples, frequency = clip.frequency, channels = clip.channels, loadType = clip.loadType.ToString() };
        }

        [UnityMcpTool("audio-source-create", Description = "Add an AudioSource to a GameObject; dry-run unless apply is true.", Category = "audio", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput AudioSourceCreate(AudioSourceCreateInput input, UnityMcpContext context)
        {
            var target = RuntimeCoreTools.RequireGameObject(input.instanceId, input.path);
            if (context.DryRun) return RuntimeCoreTools.Change(context, "Add AudioSource to '" + RuntimeCoreTools.HierarchyPath(target) + "'.", target.GetInstanceID());
            var source = UnityMcpUndo.AddComponent(target, typeof(AudioSource)) as AudioSource;
            if (source == null) throw new InvalidOperationException("Unity did not create an AudioSource.");
            if (!string.IsNullOrWhiteSpace(input.resourcePath)) source.clip = Resources.Load<AudioClip>(input.resourcePath);
            if (input.loop.HasValue) source.loop = input.loop.Value;
            if (input.volume.HasValue) source.volume = Mathf.Clamp01(input.volume.Value);
            if (input.playOnAwake.HasValue) source.playOnAwake = input.playOnAwake.Value;
            return RuntimeCoreTools.Change(context, "Added AudioSource to '" + RuntimeCoreTools.HierarchyPath(target) + "'.", source.GetInstanceID());
        }

        [UnityMcpTool("audio-source-set", Description = "Set AudioSource properties; dry-run unless apply is true.", Category = "audio", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput AudioSourceSet(AudioSourceSetInput input, UnityMcpContext context)
        {
            var source = RequireAudioSource(input.instanceId, input.path, input.componentIndex);
            if (input.volume.HasValue && (input.volume.Value < 0 || input.volume.Value > 1)) throw new ArgumentOutOfRangeException(nameof(input.volume));
            if (input.spatialBlend.HasValue && (input.spatialBlend.Value < 0 || input.spatialBlend.Value > 1)) throw new ArgumentOutOfRangeException(nameof(input.spatialBlend));
            if (!context.DryRun)
            {
                UnityMcpUndo.Record(source, "UnityMCP Set AudioSource");
                if (!string.IsNullOrWhiteSpace(input.resourcePath)) source.clip = Resources.Load<AudioClip>(input.resourcePath);
                if (input.volume.HasValue) source.volume = input.volume.Value;
                if (input.pitch.HasValue) source.pitch = input.pitch.Value;
                if (input.loop.HasValue) source.loop = input.loop.Value;
                if (input.mute.HasValue) source.mute = input.mute.Value;
                if (input.playOnAwake.HasValue) source.playOnAwake = input.playOnAwake.Value;
                if (input.spatialBlend.HasValue) source.spatialBlend = input.spatialBlend.Value;
                if (input.play.HasValue) { if (input.play.Value) source.Play(); else source.Stop(); }
            }
            return RuntimeCoreTools.Change(context, "Updated AudioSource on '" + RuntimeCoreTools.HierarchyPath(source.gameObject) + "'.", source.GetInstanceID());
        }

        [UnityMcpTool("camera-list", Description = "List loaded Camera components.", Category = "camera", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead)]
        public static CameraListOutput CameraList(EmptyInput input)
        {
            var output = new CameraListOutput();
            foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)) output.cameras.Add(ToCameraInfo(camera));
            return output;
        }

        [UnityMcpTool("camera-info", Description = "Read one Camera component.", Category = "camera", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead)]
        public static CameraInfo CameraInfo(CameraSelector input) => ToCameraInfo(RequireCamera(input.instanceId, input.path));

        [UnityMcpTool("camera-set", Description = "Set Camera properties; dry-run unless apply is true.", Category = "camera", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput CameraSet(CameraSetInput input, UnityMcpContext context)
        {
            var camera = RequireCamera(input.instanceId, input.path);
            if (input.fieldOfView.HasValue && (input.fieldOfView.Value <= 0 || input.fieldOfView.Value >= 180)) throw new ArgumentOutOfRangeException(nameof(input.fieldOfView));
            if (input.orthographicSize.HasValue && input.orthographicSize.Value <= 0) throw new ArgumentOutOfRangeException(nameof(input.orthographicSize));
            if (input.nearClipPlane.HasValue && input.nearClipPlane.Value <= 0) throw new ArgumentOutOfRangeException(nameof(input.nearClipPlane));
            if (input.farClipPlane.HasValue && input.farClipPlane.Value <= 0) throw new ArgumentOutOfRangeException(nameof(input.farClipPlane));
            if (!context.DryRun)
            {
                UnityMcpUndo.Record(camera, "UnityMCP Set Camera");
                if (input.enabled.HasValue) camera.enabled = input.enabled.Value;
                if (input.orthographic.HasValue) camera.orthographic = input.orthographic.Value;
                if (input.fieldOfView.HasValue) camera.fieldOfView = input.fieldOfView.Value;
                if (input.orthographicSize.HasValue) camera.orthographicSize = input.orthographicSize.Value;
                if (input.nearClipPlane.HasValue) camera.nearClipPlane = input.nearClipPlane.Value;
                if (input.farClipPlane.HasValue) camera.farClipPlane = input.farClipPlane.Value;
                if (input.cullingMask.HasValue) camera.cullingMask = input.cullingMask.Value;
                if (input.backgroundColor.HasValue) camera.backgroundColor = input.backgroundColor.Value;
                if (input.rect.HasValue) camera.rect = input.rect.Value;
            }
            return RuntimeCoreTools.Change(context, "Updated Camera '" + RuntimeCoreTools.HierarchyPath(camera.gameObject) + "'.", camera.GetInstanceID());
        }

        [UnityMcpTool("particle-set", Description = "Set basic ParticleSystem main-module properties; dry-run unless apply is true.", Category = "vfx", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput ParticleSet(ParticleSetInput input, UnityMcpContext context)
        {
            var target = RuntimeCoreTools.RequireGameObject(input.instanceId, input.path);
            var systems = target.GetComponents<ParticleSystem>();
            if (input.componentIndex < 0 || input.componentIndex >= systems.Length) throw new ArgumentOutOfRangeException(nameof(input.componentIndex));
            var system = systems[input.componentIndex];
            if (!context.DryRun)
            {
                UnityMcpUndo.Record(system, "UnityMCP Set ParticleSystem");
                var main = system.main;
                if (input.startLifetime.HasValue) main.startLifetime = input.startLifetime.Value;
                if (input.startSpeed.HasValue) main.startSpeed = input.startSpeed.Value;
                if (input.startSize.HasValue) main.startSize = input.startSize.Value;
                if (input.startColor.HasValue) main.startColor = input.startColor.Value;
                if (input.looping.HasValue) main.loop = input.looping.Value;
                if (input.play.HasValue) { if (input.play.Value) system.Play(true); else system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
            }
            return RuntimeCoreTools.Change(context, "Updated ParticleSystem on '" + RuntimeCoreTools.HierarchyPath(target) + "'.", system.GetInstanceID());
        }

        [UnityMcpTool("runtime-quit", Description = "Quit a desktop Development Player; dry-run unless apply is true.", Category = "runtime", Scope = UnityMcpScope.Runtime, Safety = UnityMcpSafety.Destructive, SupportsDryRun = true)]
        public static ChangeOutput RuntimeQuit(RuntimeQuitInput input, UnityMcpContext context)
        {
            if (!context.DryRun) Application.Quit();
            return RuntimeCoreTools.Change(context, "Quit the Development Player.");
        }

        private sealed class ResolvedComponentWrite
        {
            private readonly FieldInfo field;
            private readonly PropertyInfo property;
            private readonly object value;
            public ResolvedComponentWrite(FieldInfo field, PropertyInfo property, object value) { this.field = field; this.property = property; this.value = value; }
            public void Apply(Component component) { if (field != null) field.SetValue(component, value); else property.SetValue(component, value, null); }
        }

        private static ResolvedComponentWrite ResolveComponentWrite(Component component, ComponentPropertyWrite write)
        {
            if (write == null || string.IsNullOrWhiteSpace(write.property)) throw new ArgumentException("Each value needs a property name.");
            var field = component.GetType().GetField(write.property, BindingFlags.Instance | BindingFlags.Public);
            var property = component.GetType().GetProperty(write.property, BindingFlags.Instance | BindingFlags.Public);
            if (field != null && field.IsInitOnly) field = null;
            if (property != null && (!property.CanWrite || property.GetIndexParameters().Length != 0)) property = null;
            var valueType = field != null ? field.FieldType : property != null ? property.PropertyType : null;
            if (valueType == null) throw new ArgumentException("No writable public field/property named '" + write.property + "' was found.");
            var serializer = JsonSerializer.CreateDefault(new JsonSerializerSettings { Converters = new List<JsonConverter> { UnityMcpValueJsonConverter.Instance, new StringEnumConverter() } });
            var value = JToken.Parse(write.valueJson ?? "null").ToObject(valueType, serializer);
            return new ResolvedComponentWrite(field, property, value);
        }

        private static Type RequireType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) throw new ArgumentException("type is required.");
            var matches = RuntimeCoreTools.AllTypes().Where(type => type.FullName == typeName || type.Name == typeName).Distinct().ToArray();
            if (matches.Length == 0) throw new ArgumentException("Type '" + typeName + "' was not found.");
            if (matches.Length > 1) throw new ArgumentException("Type '" + typeName + "' is ambiguous; use its full name.");
            return matches[0];
        }

        private static string TypeName(Type type)
        {
            var nullable = Nullable.GetUnderlyingType(type);
            return nullable == null ? type.FullName : nullable.FullName + "?";
        }

        private static void ValidateLayer(int layer, string parameterName)
        {
            if (layer < 0 || layer > 31) throw new ArgumentOutOfRangeException(parameterName, "Layer must be between 0 and 31.");
        }

        private static AudioSource RequireAudioSource(int? instanceId, string path, int componentIndex)
        {
            var target = RuntimeCoreTools.RequireGameObject(instanceId, path);
            var sources = target.GetComponents<AudioSource>();
            if (componentIndex < 0 || componentIndex >= sources.Length) throw new ArgumentOutOfRangeException(nameof(componentIndex), "AudioSource index was not found on the target GameObject.");
            return sources[componentIndex];
        }

        private static Camera RequireCamera(int? instanceId, string path)
        {
            var target = RuntimeCoreTools.RequireGameObject(instanceId, path);
            var camera = target.GetComponent<Camera>();
            if (camera == null) throw new ArgumentException("Target GameObject has no Camera component.");
            return camera;
        }

        private static CameraInfo ToCameraInfo(Camera camera) => new CameraInfo
        {
            instanceId = camera.GetInstanceID(), name = camera.name, path = RuntimeCoreTools.HierarchyPath(camera.gameObject), enabled = camera.enabled,
            orthographic = camera.orthographic, fieldOfView = camera.fieldOfView, orthographicSize = camera.orthographicSize,
            nearClipPlane = camera.nearClipPlane, farClipPlane = camera.farClipPlane, cullingMask = camera.cullingMask,
            backgroundColor = camera.backgroundColor, rect = camera.rect
        };
    }
}
