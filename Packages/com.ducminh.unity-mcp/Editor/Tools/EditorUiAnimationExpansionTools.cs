using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable] public sealed class UiCanvasCreateInput { public string name = "Canvas"; public string renderMode = "overlay"; public int? cameraInstanceId; public int? parentInstanceId; public Vector2? sizeDelta; public bool apply; }
    [Serializable] public sealed class UiElementCreateInput { public string name = "UI Element"; public string elementType = "container"; public int? parentInstanceId; public Vector2? anchoredPosition; public Vector2? sizeDelta; public Vector2? anchorMin; public Vector2? anchorMax; public Vector2? pivot; public bool apply; }
    [Serializable] public sealed class UiElementSetInput { public int instanceId; public string name; public bool? active; public Vector2? anchoredPosition; public Vector2? sizeDelta; public Vector2? anchorMin; public Vector2? anchorMax; public Vector2? pivot; public Vector3? localEulerAngles; public int? siblingIndex; public string text; public bool apply; }
    [Serializable] public sealed class UiCreateOutput { public bool dryRun; public bool changed; public string summary; public int? instanceId; public string hierarchyPath; public string elementType; }
    [Serializable] public sealed class UiRaycastInput { public Vector2 screenPosition; public int? cameraInstanceId; public int? rootInstanceId; public bool includeInactive; public int limit = 50; }
    [Serializable] public sealed class UiRaycastHit { public int instanceId; public string name; public string hierarchyPath; public int hierarchyDepth; public bool active; }
    [Serializable] public sealed class UiRaycastOutput { public List<UiRaycastHit> hits = new List<UiRaycastHit>(); public bool truncated; }
    [Serializable] public sealed class AnimationClipCreateInput { public string path; public string name; public float frameRate = 60f; public bool loop; public bool apply; }
    [Serializable] public sealed class AnimationClipInfoOutput { public string path; public string name; public float length; public float frameRate; public bool loop; public bool legacy; public bool humanMotion; public bool empty; public int floatCurveCount; public int objectReferenceCurveCount; public int eventCount; }
    [Serializable] public sealed class ProfilerStartInput { public bool enableBinaryLog; public string logFile; public int? maxUsedMemory; public bool apply; }
    [Serializable] public sealed class ProfilerActionInput { public bool apply; }
    [Serializable] public sealed class ProfilerStatusOutput { public bool enabled; public bool binaryLogEnabled; public string logFile; public long maxUsedMemory; public long totalAllocatedMemory; public long totalReservedMemory; }
    [Serializable] public sealed class MemorySummaryOutput { public long totalAllocatedMemory; public long totalReservedMemory; public long totalUnusedReservedMemory; public long graphicsDriverMemory; public long monoUsedMemory; public long monoHeapMemory; public long managedMemory; public int systemMemoryMegabytes; }
    [Serializable] public sealed class SceneComplexityInput { public string scene; public bool includeInactive = true; }
    [Serializable] public sealed class SceneComplexityItem
    {
        public string scene;
        public string path;
        public int rootCount;
        public int gameObjectCount;
        public int activeGameObjectCount;
        public int componentCount;
        public int rendererCount;
        public int meshRendererCount;
        public int skinnedMeshRendererCount;
        public int lightCount;
        public int cameraCount;
        public int canvasCount;
        public int animatorCount;
        public int animationCount;
        public int colliderCount;
        public int collider2DCount;
        public int uniqueMeshCount;
        public long uniqueMeshVertices;
        public long uniqueMeshTriangles;
        public long rendererTriangleInstances;
    }
    [Serializable] public sealed class SceneComplexityOutput { public List<SceneComplexityItem> scenes = new List<SceneComplexityItem>(); }

    /// <summary>
    /// Editor-only tools based solely on UnityEngine/Core Editor APIs.  The UI tools deliberately
    /// operate on Canvas and RectTransform so the package does not force a UGUI or TextMeshPro dependency.
    /// </summary>
    public static class EditorUiAnimationExpansionTools
    {
        [UnityMcpTool("ui-canvas-create", Description = "Create a Canvas GameObject; dry-run unless apply is true.", Category = "ui", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static UiCreateOutput UiCanvasCreate(UiCanvasCreateInput input, UnityMcpContext context)
        {
            if (string.IsNullOrWhiteSpace(input.name)) throw new ArgumentException("Canvas name is required.");
            var renderMode = ParseRenderMode(input.renderMode);
            var parent = input.parentInstanceId.HasValue ? RequireSceneGameObject(input.parentInstanceId.Value) : null;
            var camera = input.cameraInstanceId.HasValue ? RequireCamera(input.cameraInstanceId.Value) : null;
            if (renderMode == RenderMode.ScreenSpaceCamera && camera == null) throw new ArgumentException("cameraInstanceId is required for screen-space-camera render mode.");

            if (context.DryRun)
                return UiChange(context, "Create " + renderMode + " Canvas '" + input.name + "'.", null, "canvas");

            var created = new GameObject(input.name, typeof(RectTransform), typeof(Canvas));
            Undo.RegisterCreatedObjectUndo(created, "UnityMCP Create Canvas");
            var rect = created.GetComponent<RectTransform>();
            var canvas = created.GetComponent<Canvas>();
            canvas.renderMode = renderMode;
            if (camera != null) canvas.worldCamera = camera;
            if (parent != null) Undo.SetTransformParent(created.transform, parent.transform, "UnityMCP Set Canvas Parent");
            if (input.sizeDelta.HasValue) rect.sizeDelta = input.sizeDelta.Value;
            return UiChange(context, "Create " + renderMode + " Canvas '" + created.name + "'.", created, "canvas");
        }

        [UnityMcpTool("ui-element-create", Description = "Create a RectTransform UI container under an optional parent; dry-run unless apply is true.", Category = "ui", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static UiCreateOutput UiElementCreate(UiElementCreateInput input, UnityMcpContext context)
        {
            if (string.IsNullOrWhiteSpace(input.name)) throw new ArgumentException("UI element name is required.");
            var elementType = NormalizeElementType(input.elementType);
            var parent = input.parentInstanceId.HasValue ? RequireSceneGameObject(input.parentInstanceId.Value) : null;
            if (parent != null && parent.GetComponent<RectTransform>() == null) throw new ArgumentException("The UI parent must have a RectTransform.");

            if (context.DryRun)
                return UiChange(context, "Create " + elementType + " UI element '" + input.name + "'.", null, elementType);

            var created = new GameObject(input.name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(created, "UnityMCP Create UI Element");
            var rect = created.GetComponent<RectTransform>();
            if (parent != null) Undo.SetTransformParent(rect, parent.transform, "UnityMCP Set UI Parent");
            ApplyRect(rect, input.anchoredPosition, input.sizeDelta, input.anchorMin, input.anchorMax, input.pivot);
            return UiChange(context, "Create " + elementType + " UI element '" + created.name + "'.", created, elementType);
        }

        [UnityMcpTool("ui-element-set", Description = "Set a RectTransform UI element's layout and optional text; dry-run unless apply is true.", Category = "ui", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static UiCreateOutput UiElementSet(UiElementSetInput input, UnityMcpContext context)
        {
            var target = RequireSceneGameObject(input.instanceId);
            var rect = target.GetComponent<RectTransform>();
            if (rect == null) throw new ArgumentException("Target GameObject does not have a RectTransform.");
            if (input.siblingIndex.HasValue && input.siblingIndex.Value < 0) throw new ArgumentException("siblingIndex must be zero or greater.");
            var textComponent = input.text == null ? null : FindTextComponent(target);
            if (input.text != null && textComponent == null)
                throw new ArgumentException("Target has no UGUI Text or TextMeshPro text component. This tool does not add optional UI package components.");

            if (context.DryRun)
                return UiChange(context, "Set UI element '" + HierarchyPath(target) + "'.", target, "container");

            Undo.RecordObject(target, "UnityMCP Set UI Element");
            Undo.RecordObject(rect, "UnityMCP Set UI Element Layout");
            if (textComponent != null) Undo.RecordObject(textComponent, "UnityMCP Set UI Text");
            if (input.name != null) target.name = input.name;
            if (input.active.HasValue) target.SetActive(input.active.Value);
            ApplyRect(rect, input.anchoredPosition, input.sizeDelta, input.anchorMin, input.anchorMax, input.pivot);
            if (input.localEulerAngles.HasValue) rect.localEulerAngles = input.localEulerAngles.Value;
            if (input.siblingIndex.HasValue) rect.SetSiblingIndex(input.siblingIndex.Value);
            if (textComponent != null) textComponent.GetType().GetProperty("text").SetValue(textComponent, input.text, null);
            EditorUtility.SetDirty(target);
            if (textComponent != null) EditorUtility.SetDirty(textComponent);
            return UiChange(context, "Set UI element '" + HierarchyPath(target) + "'.", target, "container");
        }

        [UnityMcpTool("ui-raycast", Description = "Return Canvas RectTransforms containing a screen point. This is geometry-based and has no UGUI dependency.", Category = "ui", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static UiRaycastOutput UiRaycast(UiRaycastInput input)
        {
            var camera = input.cameraInstanceId.HasValue ? RequireCamera(input.cameraInstanceId.Value) : null;
            var root = input.rootInstanceId.HasValue ? RequireSceneGameObject(input.rootInstanceId.Value) : null;
            var limit = Math.Max(1, Math.Min(200, input.limit));
            var candidates = new List<RectTransform>();

            foreach (var scene in LoadedScenes())
                foreach (var rootObject in scene.GetRootGameObjects())
                    candidates.AddRange(rootObject.GetComponentsInChildren<RectTransform>(input.includeInactive));

            var output = new UiRaycastOutput();
            foreach (var rect in candidates.Where(value => value != null).Distinct())
            {
                if (!input.includeInactive && !rect.gameObject.activeInHierarchy) continue;
                if (root != null && rect.gameObject != root && !rect.IsChildOf(root.transform)) continue;
                if (rect.GetComponentInParent<Canvas>() == null) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(rect, input.screenPosition, camera)) continue;
                output.hits.Add(new UiRaycastHit
                {
                    instanceId = rect.gameObject.GetInstanceID(),
                    name = rect.gameObject.name,
                    hierarchyPath = HierarchyPath(rect.gameObject),
                    hierarchyDepth = HierarchyDepth(rect),
                    active = rect.gameObject.activeInHierarchy
                });
            }

            var orderedHits = output.hits
                .OrderByDescending(hit => hit.hierarchyDepth)
                .ThenByDescending(hit => SiblingIndex(hit.instanceId))
                .ToList();
            output.truncated = orderedHits.Count > limit;
            output.hits = orderedHits.Take(limit).ToList();
            return output;
        }

        [UnityMcpTool("animation-clip-info", Description = "Read AnimationClip asset metadata and curve counts.", Category = "animation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static AnimationClipInfoOutput AnimationClipInfo(AssetPathInput input)
        {
            ValidateExistingAsset(input.path, ".anim");
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(input.path);
            if (clip == null) throw new ArgumentException("Path is not an AnimationClip asset.");
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            return new AnimationClipInfoOutput
            {
                path = input.path,
                name = clip.name,
                length = clip.length,
                frameRate = clip.frameRate,
                loop = settings.loopTime,
                legacy = clip.legacy,
                humanMotion = clip.humanMotion,
                empty = clip.empty,
                floatCurveCount = AnimationUtility.GetCurveBindings(clip).Length,
                objectReferenceCurveCount = AnimationUtility.GetObjectReferenceCurveBindings(clip).Length,
                eventCount = clip.events?.Length ?? 0
            };
        }

        [UnityMcpTool("animation-clip-create", Description = "Create an empty AnimationClip asset; dry-run unless apply is true.", Category = "animation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput AnimationClipCreate(AnimationClipCreateInput input, UnityMcpContext context)
        {
            ValidateCreatableAssetPath(input.path, ".anim");
            if (input.frameRate <= 0f || input.frameRate > 1000f) throw new ArgumentException("frameRate must be greater than zero and at most 1000.");
            if (context.DryRun) return AssetChange(context, "Create AnimationClip '" + input.path + "'.", "create", null, input.path);

            var clip = new AnimationClip { name = string.IsNullOrWhiteSpace(input.name) ? Path.GetFileNameWithoutExtension(input.path) : input.name, frameRate = input.frameRate };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = input.loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, input.path);
            Undo.RegisterCreatedObjectUndo(clip, "UnityMCP Create AnimationClip");
            AssetDatabase.SaveAssets();
            return AssetChange(context, "Create AnimationClip '" + input.path + "'.", "create", null, input.path);
        }

        [UnityMcpTool("profiler-start", Description = "Enable Unity Profiler recording and optional binary logging; dry-run unless apply is true.", Category = "diagnostic", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput ProfilerStart(ProfilerStartInput input, UnityMcpContext context)
        {
            var absoluteLogFile = ValidateProfilerLog(input.enableBinaryLog, input.logFile);
            if (input.maxUsedMemory.HasValue && input.maxUsedMemory.Value <= 0) throw new ArgumentException("maxUsedMemory must be positive when supplied.");
            if (!context.DryRun)
            {
                if (!string.IsNullOrEmpty(absoluteLogFile)) Profiler.logFile = absoluteLogFile;
                if (input.maxUsedMemory.HasValue) Profiler.maxUsedMemory = input.maxUsedMemory.Value;
                Profiler.enableBinaryLog = input.enableBinaryLog;
                Profiler.enabled = true;
            }
            return new ChangeOutput
            {
                dryRun = context.DryRun,
                changed = !context.DryRun,
                summary = "Enable Unity Profiler" + (input.enableBinaryLog ? " with binary logging." : "."),
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "set-profiler", before = null, after = input.enableBinaryLog ? input.logFile : "enabled" } }
            };
        }

        [UnityMcpTool("profiler-stop", Description = "Disable Unity Profiler recording and binary logging; dry-run unless apply is true.", Category = "diagnostic", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput ProfilerStop(ProfilerActionInput input, UnityMcpContext context)
        {
            if (!context.DryRun)
            {
                Profiler.enableBinaryLog = false;
                Profiler.enabled = false;
            }
            return new ChangeOutput
            {
                dryRun = context.DryRun,
                changed = !context.DryRun,
                summary = "Disable Unity Profiler.",
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "set-profiler", before = "enabled", after = "disabled" } }
            };
        }

        [UnityMcpTool("profiler-status", Description = "Read the current Unity Profiler configuration and memory totals.", Category = "diagnostic", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static ProfilerStatusOutput ProfilerStatus(EmptyInput input)
        {
            return new ProfilerStatusOutput
            {
                enabled = Profiler.enabled,
                binaryLogEnabled = Profiler.enableBinaryLog,
                logFile = Profiler.logFile,
                maxUsedMemory = Profiler.maxUsedMemory,
                totalAllocatedMemory = Profiler.GetTotalAllocatedMemoryLong(),
                totalReservedMemory = Profiler.GetTotalReservedMemoryLong()
            };
        }

        [UnityMcpTool("memory-summary", Description = "Read Unity and managed memory allocation totals.", Category = "diagnostic", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static MemorySummaryOutput MemorySummary(EmptyInput input)
        {
            return new MemorySummaryOutput
            {
                totalAllocatedMemory = Profiler.GetTotalAllocatedMemoryLong(),
                totalReservedMemory = Profiler.GetTotalReservedMemoryLong(),
                totalUnusedReservedMemory = Profiler.GetTotalUnusedReservedMemoryLong(),
                graphicsDriverMemory = Profiler.GetAllocatedMemoryForGraphicsDriver(),
                monoUsedMemory = Profiler.GetMonoUsedSizeLong(),
                monoHeapMemory = Profiler.GetMonoHeapSizeLong(),
                managedMemory = GC.GetTotalMemory(false),
                systemMemoryMegabytes = SystemInfo.systemMemorySize
            };
        }

        [UnityMcpTool("scene-complexity-analyze", Description = "Analyze loaded scene hierarchy, components, renderers, and mesh complexity.", Category = "diagnostic", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static SceneComplexityOutput SceneComplexityAnalyze(SceneComplexityInput input)
        {
            var scenes = LoadedScenes().Where(scene => string.IsNullOrWhiteSpace(input.scene) || scene.name == input.scene || scene.path == input.scene).ToList();
            if (!string.IsNullOrWhiteSpace(input.scene) && scenes.Count == 0) throw new ArgumentException("Loaded scene was not found.");
            var output = new SceneComplexityOutput();
            foreach (var scene in scenes) output.scenes.Add(AnalyzeScene(scene, input.includeInactive));
            return output;
        }

        private static UiCreateOutput UiChange(UnityMcpContext context, string summary, GameObject gameObject, string elementType)
        {
            return new UiCreateOutput
            {
                dryRun = context.DryRun,
                changed = !context.DryRun,
                summary = summary,
                instanceId = gameObject == null ? (int?)null : gameObject.GetInstanceID(),
                hierarchyPath = gameObject == null ? null : HierarchyPath(gameObject),
                elementType = elementType
            };
        }

        private static ChangeOutput AssetChange(UnityMcpContext context, string summary, string operation, string before, string after)
        {
            return new ChangeOutput
            {
                dryRun = context.DryRun,
                changed = !context.DryRun,
                summary = summary,
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = operation, before = before, after = after } }
            };
        }

        private static RenderMode ParseRenderMode(string value)
        {
            switch ((value ?? "overlay").Trim().ToLowerInvariant())
            {
                case "overlay":
                case "screen-space-overlay": return RenderMode.ScreenSpaceOverlay;
                case "camera":
                case "screen-space-camera": return RenderMode.ScreenSpaceCamera;
                case "world":
                case "world-space": return RenderMode.WorldSpace;
                default: throw new ArgumentException("renderMode must be overlay, screen-space-camera, or world-space.");
            }
        }

        private static string NormalizeElementType(string value)
        {
            var normalized = (value ?? "container").Trim().ToLowerInvariant();
            if (normalized == "container" || normalized == "rect-transform") return "container";
            throw new ArgumentException("elementType must be 'container' or 'rect-transform'. Graphic UI components require their optional package and are not created by this core tool.");
        }

        private static void ApplyRect(RectTransform rect, Vector2? anchoredPosition, Vector2? sizeDelta, Vector2? anchorMin, Vector2? anchorMax, Vector2? pivot)
        {
            if (anchoredPosition.HasValue) rect.anchoredPosition = anchoredPosition.Value;
            if (sizeDelta.HasValue) rect.sizeDelta = sizeDelta.Value;
            if (anchorMin.HasValue) rect.anchorMin = anchorMin.Value;
            if (anchorMax.HasValue) rect.anchorMax = anchorMax.Value;
            if (pivot.HasValue) rect.pivot = pivot.Value;
        }

        private static GameObject RequireSceneGameObject(int instanceId)
        {
            var value = EditorUtility.InstanceIDToObject(instanceId);
            var gameObject = value as GameObject ?? (value as Component)?.gameObject;
            if (gameObject == null || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded) throw new ArgumentException("A loaded scene GameObject was not found for instanceId.");
            return gameObject;
        }

        private static Camera RequireCamera(int instanceId)
        {
            var gameObject = RequireSceneGameObject(instanceId);
            var camera = gameObject.GetComponent<Camera>();
            if (camera == null) throw new ArgumentException("instanceId does not identify a GameObject with a Camera component.");
            return camera;
        }

        private static Component FindTextComponent(GameObject gameObject)
        {
            return gameObject.GetComponents<Component>()
                .FirstOrDefault(component => component != null &&
                    (component.GetType().FullName == "UnityEngine.UI.Text" || component.GetType().FullName == "TMPro.TMP_Text") &&
                    component.GetType().GetProperty("text")?.CanWrite == true &&
                    component.GetType().GetProperty("text").PropertyType == typeof(string));
        }

        private static IEnumerable<Scene> LoadedScenes()
        {
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene.IsValid() && scene.isLoaded) yield return scene;
            }
        }

        private static string HierarchyPath(GameObject gameObject)
        {
            var names = new Stack<string>();
            for (var current = gameObject.transform; current != null; current = current.parent) names.Push(current.name);
            return gameObject.scene.name + ":/" + string.Join("/", names.ToArray());
        }

        private static int HierarchyDepth(Transform transform)
        {
            var depth = 0;
            for (var current = transform.parent; current != null; current = current.parent) depth++;
            return depth;
        }

        private static int SiblingIndex(int instanceId)
        {
            var gameObject = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            return gameObject == null ? 0 : gameObject.transform.GetSiblingIndex();
        }

        private static void ValidateAssetPath(string path, string extension = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains("..") || Path.IsPathRooted(path))
                throw new ArgumentException("Path must be project-relative under Assets and may not contain '..'.");
            if (extension != null && !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Path must end with " + extension + ".");
        }

        private static void ValidateExistingAsset(string path, string extension = null)
        {
            ValidateAssetPath(path, extension);
            if (AssetDatabase.LoadMainAssetAtPath(path) == null) throw new ArgumentException("Asset was not found: " + path);
        }

        private static void ValidateCreatableAssetPath(string path, string extension)
        {
            ValidateAssetPath(path, extension);
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(parent) || !AssetDatabase.IsValidFolder(parent)) throw new ArgumentException("The asset's parent folder does not exist.");
            if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(Path.GetFullPath(path))) throw new ArgumentException("An asset already exists at the requested path.");
        }

        private static string ValidateProfilerLog(bool enableBinaryLog, string logFile)
        {
            if (!enableBinaryLog) return null;
            if (string.IsNullOrWhiteSpace(logFile)) throw new ArgumentException("logFile under Assets is required when enableBinaryLog is true.");
            ValidateAssetPath(logFile, ".raw");
            var absolute = Path.GetFullPath(logFile);
            if (!Directory.Exists(Path.GetDirectoryName(absolute))) throw new ArgumentException("The profiler log parent folder does not exist.");
            return absolute;
        }

        private static SceneComplexityItem AnalyzeScene(Scene scene, bool includeInactive)
        {
            var item = new SceneComplexityItem { scene = scene.name, path = scene.path, rootCount = scene.rootCount };
            var uniqueMeshes = new HashSet<int>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(includeInactive))
                {
                    var gameObject = transform.gameObject;
                    item.gameObjectCount++;
                    if (gameObject.activeInHierarchy) item.activeGameObjectCount++;
                    item.componentCount += gameObject.GetComponents<Component>().Count(component => component != null);
                    item.rendererCount += gameObject.GetComponents<Renderer>().Length;
                    item.meshRendererCount += gameObject.GetComponents<MeshRenderer>().Length;
                    item.skinnedMeshRendererCount += gameObject.GetComponents<SkinnedMeshRenderer>().Length;
                    item.lightCount += gameObject.GetComponents<Light>().Length;
                    item.cameraCount += gameObject.GetComponents<Camera>().Length;
                    item.canvasCount += gameObject.GetComponents<Canvas>().Length;
                    item.animatorCount += gameObject.GetComponents<Animator>().Length;
                    item.animationCount += gameObject.GetComponents<Animation>().Length;
                    item.colliderCount += gameObject.GetComponents<Collider>().Length;
                    item.collider2DCount += gameObject.GetComponents<Collider2D>().Length;

                    foreach (var filter in gameObject.GetComponents<MeshFilter>()) AddMesh(filter.sharedMesh, item, uniqueMeshes);
                    foreach (var renderer in gameObject.GetComponents<SkinnedMeshRenderer>()) AddMesh(renderer.sharedMesh, item, uniqueMeshes);
                }
            }
            return item;
        }

        private static void AddMesh(Mesh mesh, SceneComplexityItem item, HashSet<int> uniqueMeshes)
        {
            if (mesh == null) return;
            var triangles = MeshTriangleCount(mesh);
            item.rendererTriangleInstances += triangles;
            if (!uniqueMeshes.Add(mesh.GetInstanceID())) return;
            item.uniqueMeshCount++;
            item.uniqueMeshVertices += mesh.vertexCount;
            item.uniqueMeshTriangles += triangles;
        }

        private static long MeshTriangleCount(Mesh mesh)
        {
            long triangles = 0;
            for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                if (mesh.GetTopology(subMesh) == MeshTopology.Triangles) triangles += (long)mesh.GetIndexCount(subMesh) / 3L;
            return triangles;
        }
    }
}
