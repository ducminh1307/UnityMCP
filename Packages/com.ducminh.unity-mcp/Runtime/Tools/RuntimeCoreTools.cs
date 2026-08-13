using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DucMinh.UnityMcp
{
    [Serializable] public sealed class EmptyInput { }
    [Serializable] public sealed class UnityStatusOutput { public string status; public string scope; public string unityVersion; public bool isPlaying; public int frameCount; }
    [Serializable] public sealed class ProjectInfoOutput { public string productName; public string companyName; public string unityVersion; public string platform; public string dataPath; public string persistentDataPath; public string version; public string buildGuid; }
    [Serializable] public sealed class RuntimeStateOutput { public bool isFocused; public bool isPlaying; public bool isPaused; public float time; public float realtimeSinceStartup; public int frameCount; public int targetFrameRate; }
    [Serializable] public sealed class TimeScaleOutput { public float timeScale; public float fixedDeltaTime; }
    [Serializable] public sealed class SetTimeScaleInput { public float timeScale = 1f; public float? fixedDeltaTime; public bool apply; }
    [Serializable] public sealed class SceneInfo { public int buildIndex; public string name; public string path; public bool isLoaded; public bool isDirty; public int rootCount; public bool isActive; }
    [Serializable] public sealed class SceneListOutput { public List<SceneInfo> scenes = new List<SceneInfo>(); }
    [Serializable] public sealed class SceneHierarchyInput { public string scene; public int maxDepth = 12; public bool includeInactive = true; }
    [Serializable] public sealed class GameObjectNode { public int instanceId; public string name; public string path; public bool activeSelf; public bool activeInHierarchy; public string tag; public int layer; public List<string> componentTypes = new List<string>(); public List<GameObjectNode> children = new List<GameObjectNode>(); public bool truncated; }
    [Serializable] public sealed class SceneHierarchyOutput { public List<GameObjectNode> roots = new List<GameObjectNode>(); }
    [Serializable] public sealed class GameObjectSelector { public int? instanceId; public string path; }
    [Serializable] public sealed class GameObjectFindInput { public string name; public string tag; public string scene; public bool includeInactive = true; public int limit = 100; }
    [Serializable] public sealed class GameObjectSummary { public int instanceId; public string name; public string path; public string scene; public bool activeSelf; public bool activeInHierarchy; }
    [Serializable] public sealed class GameObjectFindOutput { public List<GameObjectSummary> matches = new List<GameObjectSummary>(); public bool truncated; }
    [Serializable] public sealed class GameObjectGetInput { public int? instanceId; public string path; }
    [Serializable] public sealed class TransformInfo { public Vector3 localPosition; public Quaternion localRotation; public Vector3 localScale; public Vector3 worldPosition; public Quaternion worldRotation; }
    [Serializable] public sealed class GameObjectInfo { public int instanceId; public string name; public string path; public string scene; public bool activeSelf; public bool activeInHierarchy; public string tag; public int layer; public int? parentInstanceId; public TransformInfo transform; public List<string> componentTypes = new List<string>(); }
    [Serializable] public sealed class ComponentTypesInput { public string search; public int limit = 200; }
    [Serializable] public sealed class ComponentTypeInfo { public string fullName; public string assembly; public bool isBehaviour; }
    [Serializable] public sealed class ComponentTypesOutput { public List<ComponentTypeInfo> types = new List<ComponentTypeInfo>(); public bool truncated; }
    [Serializable] public sealed class ComponentSchemaInput { public string type; }
    [Serializable] public sealed class MemberSchemaInfo { public string name; public string type; public bool writable; public string kind; }
    [Serializable] public sealed class ComponentSchemaOutput { public string type; public List<MemberSchemaInfo> members = new List<MemberSchemaInfo>(); }
    [Serializable] public sealed class ComponentGetInput { public int? instanceId; public string path; public string type; }
    [Serializable] public sealed class ComponentInfo { public int instanceId; public string type; public string json; public bool enabled; }
    [Serializable] public sealed class ChangeJournalEntry { public string operation; public string before; public string after; }
    [Serializable] public sealed class ChangeOutput { public bool dryRun; public bool changed; public string summary; public int? instanceId; public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>(); public bool rollbackSupported; }
    [Serializable] public sealed class GameObjectCreateInput { public string name = "GameObject"; public int? parentInstanceId; public string parentPath; public Vector3? localPosition; public bool apply; }
    [Serializable] public sealed class GameObjectDeleteInput { public int? instanceId; public string path; public bool apply; }
    [Serializable] public sealed class GameObjectParentInput { public int? instanceId; public string path; public int? parentInstanceId; public string parentPath; public bool worldPositionStays = true; public bool apply; }
    [Serializable] public sealed class GameObjectTransformInput { public int? instanceId; public string path; public Vector3? localPosition; public Vector3? localEulerAngles; public Vector3? localScale; public Vector3? worldPosition; public Vector3? worldEulerAngles; public bool apply; }
    [Serializable] public sealed class GameObjectPropertiesInput { public int? instanceId; public string path; public string name; public bool? active; public int? layer; public string tag; public bool apply; }
    [Serializable] public sealed class ComponentAddInput { public int? instanceId; public string path; public string type; public bool apply; }
    [Serializable] public sealed class ComponentRemoveInput { public int? instanceId; public string path; public string type; public int componentIndex; public bool apply; }
    [Serializable] public sealed class ComponentSetPropertyInput { public int? instanceId; public string path; public string type; public int componentIndex; public string property; public string valueJson; public bool apply; }
    [Serializable] public sealed class JobInput { public string jobId; }
    [Serializable] public sealed class JobOutput { public string jobId; public string status; public string resultJson; public string error; }

    public static class RuntimeCoreTools
    {
        [UnityMcpTool("unity-status", Title = "Unity status", Description = "Return the connected Unity target status.", Category = "system", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static UnityStatusOutput UnityStatus(EmptyInput input) => new UnityStatusOutput
        {
            status = "ok", scope = Application.isEditor ? "editor" : "runtime", unityVersion = Application.unityVersion,
            isPlaying = Application.isPlaying, frameCount = Time.frameCount
        };

        [UnityMcpTool("project-info", Title = "Project info", Description = "Return non-secret Unity project and build metadata.", Category = "system", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static ProjectInfoOutput ProjectInfo(EmptyInput input) => new ProjectInfoOutput
        {
            productName = Application.productName, companyName = Application.companyName, unityVersion = Application.unityVersion,
            platform = Application.platform.ToString(), dataPath = Application.dataPath, persistentDataPath = Application.persistentDataPath,
            version = Application.version, buildGuid = Application.buildGUID
        };

        [UnityMcpTool("runtime-state-get", Description = "Return Development Player runtime state.", Category = "runtime", Scope = UnityMcpScope.Runtime, Safety = UnityMcpSafety.SafeRead)]
        public static RuntimeStateOutput RuntimeStateGet(EmptyInput input) => new RuntimeStateOutput
        {
            isFocused = Application.isFocused, isPlaying = Application.isPlaying, isPaused = Debug.isDebugBuild && Time.timeScale == 0,
            time = Time.time, realtimeSinceStartup = Time.realtimeSinceStartup, frameCount = Time.frameCount, targetFrameRate = Application.targetFrameRate
        };

        [UnityMcpTool("runtime-time-scale-get", Description = "Read Player time scale.", Category = "runtime", Scope = UnityMcpScope.Runtime, Safety = UnityMcpSafety.SafeRead)]
        public static TimeScaleOutput RuntimeTimeScaleGet(EmptyInput input) => new TimeScaleOutput { timeScale = Time.timeScale, fixedDeltaTime = Time.fixedDeltaTime };

        [UnityMcpTool("runtime-time-scale-set", Description = "Set Player time scale; dry-run unless apply is true.", Category = "runtime", Scope = UnityMcpScope.Runtime, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput RuntimeTimeScaleSet(SetTimeScaleInput input, UnityMcpContext context)
        {
            if (input.timeScale < 0 || input.timeScale > 100) throw new ArgumentOutOfRangeException(nameof(input.timeScale), "timeScale must be between 0 and 100.");
            if (!context.DryRun) { Time.timeScale = input.timeScale; if (input.fixedDeltaTime.HasValue) Time.fixedDeltaTime = input.fixedDeltaTime.Value; }
            return Change(context, $"Set timeScale to {input.timeScale}.");
        }

        [UnityMcpTool("scene-list", Description = "List currently loaded scenes.", Category = "scene", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static SceneListOutput SceneList(EmptyInput input)
        {
            var output = new SceneListOutput();
            var active = SceneManager.GetActiveScene();
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                output.scenes.Add(new SceneInfo { buildIndex = scene.buildIndex, name = scene.name, path = scene.path, isLoaded = scene.isLoaded, isDirty = scene.isDirty, rootCount = scene.rootCount, isActive = scene == active });
            }
            return output;
        }

        [UnityMcpTool("scene-hierarchy", Description = "Read a loaded scene hierarchy.", Category = "scene", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static SceneHierarchyOutput SceneHierarchy(SceneHierarchyInput input)
        {
            var scene = FindScene(input.scene);
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("Scene was not found or is not loaded.");
            var output = new SceneHierarchyOutput();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!input.includeInactive && !root.activeInHierarchy) continue;
                output.roots.Add(ToNode(root, 0, Math.Max(0, Math.Min(input.maxDepth, 64)), input.includeInactive));
            }
            return output;
        }

        [UnityMcpTool("gameobject-find", Description = "Find loaded GameObjects by name, tag, and scene.", Category = "gameobject", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static GameObjectFindOutput GameObjectFind(GameObjectFindInput input)
        {
            var limit = Math.Max(1, Math.Min(input.limit, 1000));
            var output = new GameObjectFindOutput();
            foreach (var gameObject in AllSceneObjects())
            {
                if (!input.includeInactive && !gameObject.activeInHierarchy) continue;
                if (!string.IsNullOrEmpty(input.name) && gameObject.name.IndexOf(input.name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!string.IsNullOrEmpty(input.tag) && !gameObject.CompareTag(input.tag)) continue;
                if (!string.IsNullOrEmpty(input.scene) && !SceneMatches(gameObject.scene, input.scene)) continue;
                if (output.matches.Count >= limit) { output.truncated = true; break; }
                output.matches.Add(Summary(gameObject));
            }
            return output;
        }

        [UnityMcpTool("gameobject-get", Description = "Read one loaded GameObject by instance ID or hierarchy path.", Category = "gameobject", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static GameObjectInfo GameObjectGet(GameObjectGetInput input) => Info(RequireGameObject(input.instanceId, input.path));

        [UnityMcpTool("component-types", Description = "List available concrete Component types.", Category = "component", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static ComponentTypesOutput ComponentTypes(ComponentTypesInput input)
        {
            var output = new ComponentTypesOutput();
            var limit = Math.Max(1, Math.Min(input.limit, 1000));
            foreach (var type in AllTypes().Where(t => typeof(Component).IsAssignableFrom(t) && !t.IsAbstract && !t.ContainsGenericParameters).OrderBy(t => t.FullName))
            {
                if (!string.IsNullOrEmpty(input.search) && type.FullName.IndexOf(input.search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (output.types.Count >= limit) { output.truncated = true; break; }
                output.types.Add(new ComponentTypeInfo { fullName = type.FullName, assembly = type.Assembly.GetName().Name, isBehaviour = typeof(Behaviour).IsAssignableFrom(type) });
            }
            return output;
        }

        [UnityMcpTool("component-schema", Description = "Describe serializable fields and public properties of a Component type.", Category = "component", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static ComponentSchemaOutput ComponentSchema(ComponentSchemaInput input)
        {
            var type = RequireComponentType(input.type);
            var output = new ComponentSchemaOutput { type = type.FullName };
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public).OrderBy(f => f.Name))
                output.members.Add(new MemberSchemaInfo { name = field.Name, type = field.FieldType.FullName, writable = !field.IsInitOnly, kind = "field" });
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.GetIndexParameters().Length == 0).OrderBy(p => p.Name))
                output.members.Add(new MemberSchemaInfo { name = property.Name, type = property.PropertyType.FullName, writable = property.CanWrite, kind = "property" });
            return output;
        }

        [UnityMcpTool("component-get", Description = "Read a Component as Unity JSON.", Category = "component", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static ComponentInfo ComponentGet(ComponentGetInput input)
        {
            var gameObject = RequireGameObject(input.instanceId, input.path);
            var component = RequireComponents(gameObject, input.type).First();
            return new ComponentInfo { instanceId = component.GetInstanceID(), type = component.GetType().FullName, json = JsonUtility.ToJson(component), enabled = !(component is Behaviour behaviour) || behaviour.enabled };
        }

        [UnityMcpTool("gameobject-create", Description = "Create a GameObject; dry-run unless apply is true.", Category = "gameobject", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput GameObjectCreate(GameObjectCreateInput input, UnityMcpContext context)
        {
            if (string.IsNullOrWhiteSpace(input.name)) throw new ArgumentException("name is required.");
            var parent = input.parentInstanceId.HasValue || !string.IsNullOrEmpty(input.parentPath) ? RequireGameObject(input.parentInstanceId, input.parentPath) : null;
            if (context.DryRun) return Change(context, $"Create GameObject '{input.name}'.");
            var created = new GameObject(input.name);
            UnityMcpUndo.RegisterCreated(created, "UnityMCP Create GameObject");
            if (parent != null) created.transform.SetParent(parent.transform, false);
            if (input.localPosition.HasValue) created.transform.localPosition = input.localPosition.Value;
            return Change(context, $"Created GameObject '{input.name}'.", created.GetInstanceID());
        }

        [UnityMcpTool("gameobject-delete", Description = "Destroy a GameObject; dry-run unless apply is true.", Category = "gameobject", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Destructive, SupportsDryRun = true)]
        public static ChangeOutput GameObjectDelete(GameObjectDeleteInput input, UnityMcpContext context)
        {
            var target = RequireGameObject(input.instanceId, input.path);
            var summary = $"Destroy GameObject '{HierarchyPath(target)}'.";
            var id = target.GetInstanceID();
            if (!context.DryRun) UnityMcpUndo.Destroy(target);
            return Change(context, summary, id);
        }

        [UnityMcpTool("gameobject-set-parent", Description = "Change a GameObject parent; dry-run unless apply is true.", Category = "gameobject", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput GameObjectSetParent(GameObjectParentInput input, UnityMcpContext context)
        {
            var target = RequireGameObject(input.instanceId, input.path);
            var parent = input.parentInstanceId.HasValue || !string.IsNullOrEmpty(input.parentPath) ? RequireGameObject(input.parentInstanceId, input.parentPath) : null;
            if (parent != null && (parent == target || parent.transform.IsChildOf(target.transform))) throw new ArgumentException("Parent would create a hierarchy cycle.");
            if (!context.DryRun) UnityMcpUndo.SetParent(target.transform, parent == null ? null : parent.transform, input.worldPositionStays, "UnityMCP Set Parent");
            return Change(context, $"Set parent of '{HierarchyPath(target)}' to '{(parent == null ? "<scene>" : HierarchyPath(parent))}'.", target.GetInstanceID());
        }

        [UnityMcpTool("gameobject-set-transform", Description = "Set transform values; dry-run unless apply is true.", Category = "gameobject", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput GameObjectSetTransform(GameObjectTransformInput input, UnityMcpContext context)
        {
            var target = RequireGameObject(input.instanceId, input.path);
            if (!context.DryRun)
            {
                UnityMcpUndo.Record(target.transform, "UnityMCP Set Transform");
                if (input.localPosition.HasValue) target.transform.localPosition = input.localPosition.Value;
                if (input.localEulerAngles.HasValue) target.transform.localEulerAngles = input.localEulerAngles.Value;
                if (input.localScale.HasValue) target.transform.localScale = input.localScale.Value;
                if (input.worldPosition.HasValue) target.transform.position = input.worldPosition.Value;
                if (input.worldEulerAngles.HasValue) target.transform.eulerAngles = input.worldEulerAngles.Value;
            }
            return Change(context, $"Set transform on '{HierarchyPath(target)}'.", target.GetInstanceID());
        }

        [UnityMcpTool("gameobject-set-properties", Description = "Set common GameObject properties; dry-run unless apply is true.", Category = "gameobject", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput GameObjectSetProperties(GameObjectPropertiesInput input, UnityMcpContext context)
        {
            var target = RequireGameObject(input.instanceId, input.path);
            if (input.layer.HasValue && (input.layer.Value < 0 || input.layer.Value > 31)) throw new ArgumentOutOfRangeException(nameof(input.layer));
            if (!context.DryRun)
            {
                UnityMcpUndo.Record(target, "UnityMCP Set GameObject Properties");
                if (input.name != null) target.name = input.name;
                if (input.active.HasValue) target.SetActive(input.active.Value);
                if (input.layer.HasValue) target.layer = input.layer.Value;
                if (input.tag != null) target.tag = input.tag;
            }
            return Change(context, $"Set properties on '{HierarchyPath(target)}'.", target.GetInstanceID());
        }

        [UnityMcpTool("component-add", Description = "Add a Component; dry-run unless apply is true.", Category = "component", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput ComponentAdd(ComponentAddInput input, UnityMcpContext context)
        {
            var target = RequireGameObject(input.instanceId, input.path);
            var type = RequireComponentType(input.type);
            if (context.DryRun) return Change(context, $"Add {type.FullName} to '{HierarchyPath(target)}'.", target.GetInstanceID());
            var component = UnityMcpUndo.AddComponent(target, type);
            return Change(context, $"Added {type.FullName}.", component.GetInstanceID());
        }

        [UnityMcpTool("component-remove", Description = "Remove a Component; dry-run unless apply is true.", Category = "component", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Destructive, SupportsDryRun = true)]
        public static ChangeOutput ComponentRemove(ComponentRemoveInput input, UnityMcpContext context)
        {
            var target = RequireGameObject(input.instanceId, input.path);
            var components = RequireComponents(target, input.type);
            if (input.componentIndex < 0 || input.componentIndex >= components.Length) throw new ArgumentOutOfRangeException(nameof(input.componentIndex));
            var component = components[input.componentIndex];
            if (component is Transform) throw new InvalidOperationException("Transform cannot be removed.");
            var componentId = component.GetInstanceID();
            if (!context.DryRun) UnityMcpUndo.Destroy(component);
            return Change(context, $"Remove {component.GetType().FullName} from '{HierarchyPath(target)}'.", componentId);
        }

        [UnityMcpTool("component-set-property", Description = "Set one public Component field/property from JSON; dry-run unless apply is true.", Category = "component", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput ComponentSetProperty(ComponentSetPropertyInput input, UnityMcpContext context)
        {
            var target = RequireGameObject(input.instanceId, input.path);
            var components = RequireComponents(target, input.type);
            if (input.componentIndex < 0 || input.componentIndex >= components.Length) throw new ArgumentOutOfRangeException(nameof(input.componentIndex));
            var component = components[input.componentIndex];
            var field = component.GetType().GetField(input.property, BindingFlags.Instance | BindingFlags.Public);
            var property = component.GetType().GetProperty(input.property, BindingFlags.Instance | BindingFlags.Public);
            var valueType = field?.FieldType ?? (property != null && property.CanWrite ? property.PropertyType : null);
            if (valueType == null) throw new ArgumentException("A writable public field/property was not found.");
            var value = JsonConvert.DeserializeObject(input.valueJson ?? "null", valueType);
            if (!context.DryRun) { UnityMcpUndo.Record(component, "UnityMCP Set Component Property"); if (field != null) field.SetValue(component, value); else property.SetValue(component, value); }
            return Change(context, $"Set {component.GetType().FullName}.{input.property}.", component.GetInstanceID());
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [UnityMcpTool("job-get", Description = "Read one asynchronous UnityMCP job.", Category = "automation", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.SafeRead)]
        public static JobOutput JobGet(JobInput input)
        {
            if (!UnityMcpJobStore.Shared.TryGet(input.jobId, out var job)) throw new ArgumentException("Unknown job.");
            return new JobOutput { jobId = job.jobId, status = job.status, resultJson = job.result == null ? null : JsonConvert.SerializeObject(job.result), error = job.error };
        }

        [UnityMcpTool("job-cancel", Description = "Cancel one cancellable UnityMCP job.", Category = "automation", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Write)]
        public static JobOutput JobCancel(JobInput input)
        {
            if (!UnityMcpJobStore.Shared.Cancel(input.jobId, out var job)) throw new ArgumentException("Unknown job.");
            return new JobOutput { jobId = job.jobId, status = job.status, resultJson = job.result == null ? null : JsonConvert.SerializeObject(job.result), error = job.error };
        }
#endif

        [UnityMcpTool("screenshot-game-view", Description = "Capture the Development Player framebuffer as PNG.", Category = "visual", Scope = UnityMcpScope.Runtime, Safety = UnityMcpSafety.SafeRead)]
        public static UnityMcpResult ScreenshotGameView(EmptyInput input)
        {
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                var png = texture.EncodeToPNG();
                return new UnityMcpResult
                {
                    content = new List<UnityMcpContent> { new UnityMcpContent { type = "image", data = Convert.ToBase64String(png), mimeType = "image/png" } },
                    structuredContent = new { width = texture.width, height = texture.height, mimeType = "image/png" }
                };
            }
            finally { UnityEngine.Object.Destroy(texture); }
        }

        internal static ChangeOutput Change(UnityMcpContext context, string summary, int? instanceId = null) => new ChangeOutput { dryRun = context.DryRun, changed = !context.DryRun, summary = summary, instanceId = instanceId };

        internal static Scene FindScene(string selector)
        {
            if (string.IsNullOrEmpty(selector)) return SceneManager.GetActiveScene();
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (SceneMatches(scene, selector)) return scene;
            }
            return default;
        }

        internal static bool SceneMatches(Scene scene, string selector) => string.Equals(scene.name, selector, StringComparison.OrdinalIgnoreCase) || string.Equals(scene.path, selector, StringComparison.OrdinalIgnoreCase);

        internal static IEnumerable<GameObject> AllSceneObjects()
        {
            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
                foreach (var root in SceneManager.GetSceneAt(sceneIndex).GetRootGameObjects())
                    foreach (var item in Traverse(root)) yield return item;
        }

        internal static IEnumerable<GameObject> Traverse(GameObject root)
        {
            yield return root;
            for (var index = 0; index < root.transform.childCount; index++)
                foreach (var child in Traverse(root.transform.GetChild(index).gameObject)) yield return child;
        }

        internal static GameObject RequireGameObject(int? instanceId, string path)
        {
            GameObject result = null;
            if (instanceId.HasValue)
                result = AllSceneObjects().FirstOrDefault(go => go.GetInstanceID() == instanceId.Value);
            else if (!string.IsNullOrWhiteSpace(path))
                result = AllSceneObjects().FirstOrDefault(go => string.Equals(HierarchyPath(go), path, StringComparison.Ordinal));
            if (result == null) throw new ArgumentException("GameObject was not found; supply a valid instanceId or full hierarchy path.");
            return result;
        }

        internal static Type RequireComponentType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) throw new ArgumentException("type is required.");
            var matches = AllTypes().Where(t => typeof(Component).IsAssignableFrom(t) && (t.FullName == typeName || t.Name == typeName)).Distinct().ToArray();
            if (matches.Length == 0) throw new ArgumentException($"Component type '{typeName}' was not found.");
            if (matches.Length > 1) throw new ArgumentException($"Component type '{typeName}' is ambiguous; use its full name.");
            return matches[0];
        }

        internal static Component[] RequireComponents(GameObject gameObject, string typeName)
        {
            var type = RequireComponentType(typeName);
            var values = gameObject.GetComponents(type);
            if (values.Length == 0) throw new ArgumentException($"GameObject has no {type.FullName} component.");
            return values;
        }

        internal static IEnumerable<Type> AllTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException exception) { types = exception.Types.Where(t => t != null).ToArray(); }
                catch { continue; }
                foreach (var type in types) yield return type;
            }
        }

        internal static string HierarchyPath(GameObject gameObject)
        {
            var names = new Stack<string>();
            for (var current = gameObject.transform; current != null; current = current.parent) names.Push(current.name);
            return gameObject.scene.name + ":/" + string.Join("/", names.ToArray());
        }

        internal static GameObjectSummary Summary(GameObject gameObject) => new GameObjectSummary
        {
            instanceId = gameObject.GetInstanceID(), name = gameObject.name, path = HierarchyPath(gameObject), scene = gameObject.scene.name,
            activeSelf = gameObject.activeSelf, activeInHierarchy = gameObject.activeInHierarchy
        };

        internal static GameObjectInfo Info(GameObject gameObject) => new GameObjectInfo
        {
            instanceId = gameObject.GetInstanceID(), name = gameObject.name, path = HierarchyPath(gameObject), scene = gameObject.scene.name,
            activeSelf = gameObject.activeSelf, activeInHierarchy = gameObject.activeInHierarchy, tag = gameObject.tag, layer = gameObject.layer,
            parentInstanceId = gameObject.transform.parent == null ? (int?)null : gameObject.transform.parent.gameObject.GetInstanceID(),
            transform = new TransformInfo { localPosition = gameObject.transform.localPosition, localRotation = gameObject.transform.localRotation, localScale = gameObject.transform.localScale, worldPosition = gameObject.transform.position, worldRotation = gameObject.transform.rotation },
            componentTypes = gameObject.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().FullName).ToList()
        };

        internal static GameObjectNode ToNode(GameObject gameObject, int depth, int maxDepth, bool includeInactive)
        {
            var node = new GameObjectNode
            {
                instanceId = gameObject.GetInstanceID(), name = gameObject.name, path = HierarchyPath(gameObject), activeSelf = gameObject.activeSelf,
                activeInHierarchy = gameObject.activeInHierarchy, tag = gameObject.tag, layer = gameObject.layer,
                componentTypes = gameObject.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().FullName).ToList()
            };
            if (depth >= maxDepth) { node.truncated = gameObject.transform.childCount > 0; return node; }
            for (var index = 0; index < gameObject.transform.childCount; index++)
            {
                var child = gameObject.transform.GetChild(index).gameObject;
                if (includeInactive || child.activeInHierarchy) node.children.Add(ToNode(child, depth + 1, maxDepth, includeInactive));
            }
            return node;
        }
    }
}
