using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable]
    public sealed class SceneValidationInput
    {
        public string scene;
        public bool includeInactive = true;
        public int maxIssues = 100;
        public string profile = "references";
    }

    [Serializable]
    public sealed class SceneValidationIssue
    {
        public string kind;
        public string message;
        public int instanceId;
        public string hierarchyPath;
        public int componentIndex = -1;
        public string propertyPath;
    }

    [Serializable]
    public sealed class SceneValidationOutput
    {
        public string scene;
        public string profile;
        public int objectsScanned;
        public int componentsScanned;
        public List<SceneValidationIssue> issues = new List<SceneValidationIssue>();
        public bool truncated;
    }

    [Serializable]
    public sealed class SceneComplexityInput
    {
        public string scene;
        public bool includeInactive = true;
    }

    [Serializable]
    public sealed class SceneComplexityItem
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

    [Serializable]
    public sealed class SceneComplexityOutput
    {
        public List<SceneComplexityItem> scenes = new List<SceneComplexityItem>();
    }

    /// <summary>
    /// Editor scene validation and hierarchy/mesh complexity analysis commands.
    /// </summary>
    public static class EditorSceneAnalysisCommands
    {
        private const string Group = "scene_ext";

        [CliGroupCommand(
            Group,
            "Validate references and optional lifecycle hazards in one loaded scene.",
            Name = "validate",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "scene", "diagnostics" })]
        internal static ExitCode ValidateCommand(
            CommandContext cmd,
            [CliArg("scene", "Optional loaded scene name or path (defaults to active scene).")] string scene = null,
            [CliArg("include-inactive", "Include inactive GameObjects (default true).")] bool includeInactive = true,
            [CliArg("max-issues", "Maximum issues to return (1-1000, default 100).")] int maxIssues = 100,
            [CliArg("profile", "Validation profile: references, lifecycle, or all (default references).")] string profile = "references")
        {
            return Execute(cmd, () => Validate(new SceneValidationInput
            {
                scene = scene,
                includeInactive = includeInactive,
                maxIssues = maxIssues,
                profile = profile
            }));
        }

        [CliGroupCommand(
            Group,
            "Analyze loaded scene hierarchy, components, renderers, and mesh complexity.",
            Name = "analyze_complexity",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "scene", "diagnostics" })]
        internal static ExitCode AnalyzeComplexityCommand(
            CommandContext cmd,
            [CliArg("scene", "Optional loaded scene name or path (analyzes all loaded scenes when omitted).")] string scene = null,
            [CliArg("include-inactive", "Include inactive GameObjects (default true).")] bool includeInactive = true)
        {
            return Execute(cmd, () => AnalyzeComplexity(new SceneComplexityInput
            {
                scene = scene,
                includeInactive = includeInactive
            }));
        }

        internal static SceneValidationOutput Validate(SceneValidationInput input)
        {
            if (input == null)
                input = new SceneValidationInput();

            var scene = EditorCommandHelpers.RequireLoadedScene(input.scene, false);
            var limit = Math.Max(1, Math.Min(input.maxIssues, 1000));
            var profile = (input.profile ?? "references").Trim().ToLowerInvariant();
            if (profile != "references" && profile != "lifecycle" && profile != "all")
                throw new ArgumentException("profile must be references, lifecycle, or all.");

            var includeReferences = profile == "references" || profile == "all";
            var includeLifecycle = profile == "lifecycle" || profile == "all";
            var output = new SceneValidationOutput
            {
                scene = string.IsNullOrEmpty(scene.path) ? scene.name : scene.path,
                profile = profile
            };

            var rootNames = includeLifecycle ? new HashSet<string>(StringComparer.Ordinal) : null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (includeLifecycle && !rootNames.Add(root.name))
                {
                    AddIssue(output, limit, new SceneValidationIssue
                    {
                        kind = "duplicate-root-name",
                        message = "Multiple root GameObjects share this name; verify duplicate bootstrap objects are intentional.",
                        instanceId = UnityMcpObjectId.Get(root),
                        hierarchyPath = RuntimeCommandHelpers.HierarchyPath(root)
                    });
                    if (output.truncated)
                        return output;
                }

                foreach (var gameObject in RuntimeCommandHelpers.Traverse(root))
                {
                    if (!input.includeInactive && !gameObject.activeInHierarchy)
                        continue;

                    output.objectsScanned++;
                    if (includeLifecycle && (gameObject.hideFlags & HideFlags.DontSave) != 0)
                    {
                        AddIssue(output, limit, new SceneValidationIssue
                        {
                            kind = "dont-save-object",
                            message = "GameObject has DontSave hide flags and can survive lifecycle transitions unexpectedly.",
                            instanceId = UnityMcpObjectId.Get(gameObject),
                            hierarchyPath = RuntimeCommandHelpers.HierarchyPath(gameObject)
                        });
                        if (output.truncated)
                            return output;
                    }

                    var components = gameObject.GetComponents<Component>();
                    for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
                    {
                        var component = components[componentIndex];
                        if (component == null)
                        {
                            AddIssue(output, limit, new SceneValidationIssue
                            {
                                kind = "missing-script",
                                message = "A GameObject has a missing script component.",
                                instanceId = UnityMcpObjectId.Get(gameObject),
                                hierarchyPath = RuntimeCommandHelpers.HierarchyPath(gameObject),
                                componentIndex = componentIndex
                            });
                            if (output.truncated)
                                return output;
                            continue;
                        }

                        output.componentsScanned++;
                        if (!includeReferences)
                            continue;

                        try
                        {
                            var serialized = new SerializedObject(component);
                            var property = serialized.GetIterator();
                            var enterChildren = true;
                            while (property.NextVisible(enterChildren))
                            {
                                enterChildren = false;
                                if (property.propertyType != SerializedPropertyType.ObjectReference)
                                    continue;
#if UNITY_6000_6_OR_NEWER
                                if (property.objectReferenceValue != null || !property.objectReferenceEntityIdValue.IsValid())
                                    continue;
#else
                                if (property.objectReferenceValue != null || property.objectReferenceInstanceIDValue == 0)
                                    continue;
#endif
                                AddIssue(output, limit, new SceneValidationIssue
                                {
                                    kind = "broken-object-reference",
                                    message = "A serialized object reference is missing.",
                                    instanceId = UnityMcpObjectId.Get(gameObject),
                                    hierarchyPath = RuntimeCommandHelpers.HierarchyPath(gameObject),
                                    componentIndex = componentIndex,
                                    propertyPath = property.propertyPath
                                });
                                if (output.truncated)
                                    return output;
                            }
                        }
                        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                        {
                            // Built-in components without SerializedObject projection are not scene errors.
                        }
                    }
                }
            }

            return output;
        }

        internal static SceneComplexityOutput AnalyzeComplexity(SceneComplexityInput input)
        {
            if (input == null)
                input = new SceneComplexityInput();

            var scenes = LoadedScenes()
                .Where(scene => string.IsNullOrWhiteSpace(input.scene) ||
                                string.Equals(scene.name, input.scene, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(scene.path, input.scene, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!string.IsNullOrWhiteSpace(input.scene) && scenes.Count == 0)
                throw new ArgumentException("Loaded scene was not found.");

            var output = new SceneComplexityOutput();
            foreach (var scene in scenes)
                output.scenes.Add(AnalyzeScene(scene, input.includeInactive));
            return output;
        }

        private static void AddIssue(SceneValidationOutput output, int limit, SceneValidationIssue issue)
        {
            if (output.issues.Count >= limit)
            {
                output.truncated = true;
                return;
            }

            output.issues.Add(issue);
        }

        private static IEnumerable<Scene> LoadedScenes()
        {
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene.IsValid() && scene.isLoaded)
                    yield return scene;
            }
        }

        private static SceneComplexityItem AnalyzeScene(Scene scene, bool includeInactive)
        {
            var item = new SceneComplexityItem
            {
                scene = scene.name,
                path = scene.path,
                rootCount = scene.rootCount
            };
            var uniqueMeshes = new HashSet<int>();

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(includeInactive))
                {
                    var gameObject = transform.gameObject;
                    item.gameObjectCount++;
                    if (gameObject.activeInHierarchy)
                        item.activeGameObjectCount++;

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

                    foreach (var filter in gameObject.GetComponents<MeshFilter>())
                        AddMesh(filter.sharedMesh, item, uniqueMeshes);
                    foreach (var renderer in gameObject.GetComponents<SkinnedMeshRenderer>())
                        AddMesh(renderer.sharedMesh, item, uniqueMeshes);
                }
            }

            return item;
        }

        private static void AddMesh(Mesh mesh, SceneComplexityItem item, HashSet<int> uniqueMeshes)
        {
            if (mesh == null)
                return;

            var triangles = MeshTriangleCount(mesh);
            item.rendererTriangleInstances += triangles;
            if (!uniqueMeshes.Add(UnityMcpObjectId.Get(mesh)))
                return;

            item.uniqueMeshCount++;
            item.uniqueMeshVertices += mesh.vertexCount;
            item.uniqueMeshTriangles += triangles;
        }

        private static long MeshTriangleCount(Mesh mesh)
        {
            long triangles = 0;
            for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (mesh.GetTopology(subMesh) == MeshTopology.Triangles)
                    triangles += (long)mesh.GetIndexCount(subMesh) / 3L;
            }
            return triangles;
        }

        private static ExitCode Execute<T>(CommandContext cmd, Func<T> action)
        {
            try
            {
                var result = action();
                cmd.Result.Write(result);
                return ExitCode.Success;
            }
            catch (OperationCanceledException)
            {
                cmd.Log.Error("Command cancelled.");
                return ExitCode.Cancelled;
            }
            catch (ArgumentException ex)
            {
                cmd.Log.Error(ex.Message);
                return ex.Message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
                    ? ExitCode.NotFound
                    : ExitCode.InvalidUsage;
            }
            catch (Exception ex)
            {
                cmd.Log.Error(ex.Message);
                return ExitCode.Failed;
            }
        }
    }
}
