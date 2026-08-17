using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable] public sealed class EditorStateOutput { public bool isPlaying; public bool isPaused; public bool isCompiling; public bool isUpdating; public bool isFocused; public string activeScene; public string activeScenePath; }
    [Serializable] public sealed class SelectionItem { public int instanceId; public string name; public string type; public string assetPath; }
    [Serializable] public sealed class EditorSelectionOutput { public int? activeInstanceId; public List<SelectionItem> objects = new List<SelectionItem>(); }
    [Serializable] public sealed class EditorSelectionSetInput { public List<int> instanceIds = new List<int>(); public int? activeInstanceId; public bool apply; }
    [Serializable] public sealed class EditorActionInput { public bool apply; }
    [Serializable] public sealed class PlayModeSessionOutput { public bool isPlaying; public bool isPaused; public bool isCompiling; public bool isUpdating; public int stableEditorFrames; }
    [Serializable] internal sealed class PersistedPlayModeTransition { public string jobId; public bool targetPlayMode; public string deadlineUtc; public string createdUtc; public string startedUtc; public float progress; public string progressMessage; }
    [Serializable] public sealed class SceneCreateInput { public string path; public bool withDefaultGameObjects; public bool additive; public bool apply; }
    [Serializable] public sealed class SceneOpenInput { public string path; public bool additive; public bool apply; }
    [Serializable] public sealed class SceneSaveInput { public string scene; public string path; public bool apply; }
    [Serializable] public sealed class AssetSearchInput { public string query; public List<string> folders = new List<string>(); public int limit = 100; }
    [Serializable] public sealed class AssetSummary { public string guid; public string path; public string name; public string type; }
    [Serializable] public sealed class AssetSearchOutput { public List<AssetSummary> assets = new List<AssetSummary>(); public bool truncated; }
    [Serializable] public sealed class AssetPathInput { public string path; }
    [Serializable] public sealed class AssetInfoOutput { public string guid; public string path; public string name; public string type; public long fileSize; public List<string> labels = new List<string>(); public string importerType; }
    [Serializable] public sealed class AssetDependenciesOutput { public string path; public List<string> dependencies = new List<string>(); }
    [Serializable] public sealed class AssetCreateFolderInput { public string path; public bool apply; }
    [Serializable] public sealed class AssetMoveInput { public string source; public string destination; public bool apply; }
    [Serializable] public sealed class AssetDeleteInput { public string path; public bool apply; }
    [Serializable] public sealed class PrefabInfoOutput { public string path; public string assetType; public string instanceStatus; public string rootName; public int componentCount; }
    [Serializable] public sealed class PrefabInstantiateInput { public string path; public string scene; public int? parentInstanceId; public string parentPath; public Vector3? position; public bool apply; }
    [Serializable] public sealed class MaterialInfoOutput { public string path; public string name; public string shader; public int renderQueue; public List<string> keywords = new List<string>(); public List<MaterialPropertyInfo> properties = new List<MaterialPropertyInfo>(); }
    [Serializable] public sealed class MaterialPropertyInfo { public string name; public string description; public string type; }
    [Serializable] public sealed class MaterialSetPropertyInput { public string path; public string property; public string valueJson; public bool apply; }
    [Serializable] public sealed class CompileStatusOutput { public bool isCompiling; public bool isUpdating; public string[] assemblies; }
    [Serializable] public sealed class ConsoleReadInput { public int limit = 100; public string severity; public string contains; public long afterCursor = -1; }
    [Serializable] public sealed class ConsoleEntryInfo { public long cursor; public string observedUtc; public string message; public string stackTrace; public string file; public int line; public string severity; }
    [Serializable] public sealed class ConsoleReadOutput { public List<ConsoleEntryInfo> entries = new List<ConsoleEntryInfo>(); public long firstCursor = -1; public long lastCursor = -1; public long nextCursor = -1; public bool cursorReset; public bool truncated; }
    [Serializable] public sealed class CompileErrorsOutput { public List<ConsoleEntryInfo> errors = new List<ConsoleEntryInfo>(); }
    [Serializable] public sealed class PackageInfoOutput { public string name; public string displayName; public string version; public string source; public string resolvedPath; }
    [Serializable] public sealed class PackageListOutput { public List<PackageInfoOutput> packages = new List<PackageInfoOutput>(); }
    [Serializable] public sealed class CustomArgumentSpec { public string name; public string type = "string"; public bool optional; }
    [Serializable] public sealed class CustomToolScaffoldInput { public string toolName; public string className; public string description; public string scope = "editor"; public string safety = "write"; public List<CustomArgumentSpec> arguments = new List<CustomArgumentSpec>(); public bool apply; }
    [Serializable] public sealed class CustomToolScaffoldOutput { public bool dryRun; public string assetPath; public string className; public string toolName; public bool rollbackSupported; public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>(); }
    [Serializable] public sealed class CustomToolValidateInput { public string toolName; }
    [Serializable] public sealed class CustomToolValidationItem { public string toolName; public string method; public bool valid; public List<string> issues = new List<string>(); }
    [Serializable] public sealed class CustomToolValidateOutput { public List<CustomToolValidationItem> tools = new List<CustomToolValidationItem>(); }

    public static class EditorCoreTools
    {
        private static readonly Regex ToolName = new Regex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);
        private static readonly Regex Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        [UnityMcpTool("editor-state-get", Description = "Read Unity Editor state.", Category = "editor", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static EditorStateOutput EditorStateGet(EmptyInput input)
        {
            var scene = SceneManager.GetActiveScene();
            return new EditorStateOutput { isPlaying = EditorApplication.isPlaying, isPaused = EditorApplication.isPaused, isCompiling = EditorApplication.isCompiling, isUpdating = EditorApplication.isUpdating, isFocused = UnityEditorInternal.InternalEditorUtility.isApplicationActive, activeScene = scene.name, activeScenePath = scene.path };
        }

        [UnityMcpTool("editor-selection-get", Description = "Read the Editor selection.", Category = "editor", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static EditorSelectionOutput EditorSelectionGet(EmptyInput input)
        {
            var output = new EditorSelectionOutput { activeInstanceId = Selection.activeObject == null ? (int?)null : Selection.activeObject.GetInstanceID() };
            foreach (var value in Selection.objects.Where(v => v != null))
                output.objects.Add(new SelectionItem { instanceId = value.GetInstanceID(), name = value.name, type = value.GetType().FullName, assetPath = AssetDatabase.GetAssetPath(value) });
            return output;
        }

        [UnityMcpTool("editor-play", Description = "Enter Play Mode and return a job that completes after the Editor is stable; dry-run unless apply is true.", Category = "editor", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, ReturnsJob = true, TimeoutMs = 120000)]
        public static WorkflowJobStartOutput EditorPlay(EditorActionInput input, UnityMcpContext context)
        {
            if (context.DryRun) return new WorkflowJobStartOutput { dryRun = true, summary = "Enter Play Mode and wait for a stable Editor state." };
            var job = EditorWorkflowJobRunner.Start(new EditorPlayModeOperation(true), "play-mode");
            return new WorkflowJobStartOutput { accepted = true, jobId = job.jobId, status = job.status, summary = "Play Mode transition queued." };
        }

        [UnityMcpTool("editor-stop", Description = "Exit Play Mode and return a job that completes after the Editor is stable; dry-run unless apply is true.", Category = "editor", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, ReturnsJob = true, TimeoutMs = 120000)]
        public static WorkflowJobStartOutput EditorStop(EditorActionInput input, UnityMcpContext context)
        {
            if (context.DryRun) return new WorkflowJobStartOutput { dryRun = true, summary = "Exit Play Mode and wait for a stable Editor state." };
            var job = EditorWorkflowJobRunner.Start(new EditorPlayModeOperation(false), "play-mode");
            return new WorkflowJobStartOutput { accepted = true, jobId = job.jobId, status = job.status, summary = "Exit Play Mode transition queued." };
        }

        [UnityMcpTool("editor-selection-set", Description = "Set Editor selection; dry-run unless apply is true.", Category = "editor", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput EditorSelectionSet(EditorSelectionSetInput input, UnityMcpContext context)
        {
            var objects = input.instanceIds.Select(id => EditorUtility.EntityIdToObject((EntityId)id)).Where(v => v != null).ToArray();
            if (objects.Length != input.instanceIds.Count) throw new ArgumentException("One or more instance IDs were not found.");
            if (!context.DryRun)
            {
                Selection.objects = objects;
                if (input.activeInstanceId.HasValue) Selection.activeObject = EditorUtility.EntityIdToObject((EntityId)input.activeInstanceId.Value);
            }
            return Change(context, $"Select {objects.Length} object(s).");
        }

        [UnityMcpTool("editor-refresh", Description = "Refresh the AssetDatabase; dry-run unless apply is true.", Category = "editor", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput EditorRefresh(EditorActionInput input, UnityMcpContext context)
        {
            if (!context.DryRun) AssetDatabase.Refresh();
            return Change(context, "Refresh AssetDatabase.");
        }

        [UnityMcpTool("scene-create", Description = "Create and optionally save an Editor scene; dry-run unless apply is true.", Category = "scene", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput SceneCreate(SceneCreateInput input, UnityMcpContext context)
        {
            if (!string.IsNullOrEmpty(input.path)) ValidateAssetPath(input.path, ".unity");
            if (!context.DryRun)
            {
                var scene = EditorSceneManager.NewScene(input.withDefaultGameObjects ? NewSceneSetup.DefaultGameObjects : NewSceneSetup.EmptyScene, input.additive ? NewSceneMode.Additive : NewSceneMode.Single);
                if (!string.IsNullOrEmpty(input.path) && !EditorSceneManager.SaveScene(scene, input.path)) throw new InvalidOperationException("Unity could not save the new scene.");
            }
            return Change(context, $"Create {(input.additive ? "additive" : "single")} scene{(string.IsNullOrEmpty(input.path) ? "" : " at " + input.path)}.");
        }

        [UnityMcpTool("scene-open", Description = "Open a scene asset; dry-run unless apply is true.", Category = "scene", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput SceneOpen(SceneOpenInput input, UnityMcpContext context)
        {
            ValidateExistingAsset(input.path, ".unity");
            if (!context.DryRun) EditorSceneManager.OpenScene(input.path, input.additive ? OpenSceneMode.Additive : OpenSceneMode.Single);
            return Change(context, $"Open scene '{input.path}'.");
        }

        [UnityMcpTool("scene-save", Description = "Save a loaded scene; dry-run unless apply is true.", Category = "scene", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput SceneSave(SceneSaveInput input, UnityMcpContext context)
        {
            var scene = string.IsNullOrEmpty(input.scene) ? SceneManager.GetActiveScene() : Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).FirstOrDefault(v => v.name == input.scene || v.path == input.scene);
            if (!scene.IsValid()) throw new ArgumentException("Loaded scene was not found.");
            if (!string.IsNullOrEmpty(input.path)) ValidateAssetPath(input.path, ".unity");
            if (!context.DryRun && !EditorSceneManager.SaveScene(scene, input.path ?? string.Empty)) throw new InvalidOperationException("Unity could not save the scene.");
            return Change(context, $"Save scene '{scene.name}'.");
        }

        [UnityMcpTool("asset-search", Description = "Search project assets using AssetDatabase filters.", Category = "asset", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static AssetSearchOutput AssetSearch(AssetSearchInput input)
        {
            var folders = input.folders?.Where(AssetDatabase.IsValidFolder).Distinct().ToArray();
            var guids = folders != null && folders.Length > 0 ? AssetDatabase.FindAssets(input.query ?? string.Empty, folders) : AssetDatabase.FindAssets(input.query ?? string.Empty);
            var output = new AssetSearchOutput();
            var limit = Math.Max(1, Math.Min(input.limit, 1000));
            foreach (var guid in guids.Take(limit))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                output.assets.Add(new AssetSummary { guid = guid, path = path, name = Path.GetFileNameWithoutExtension(path), type = type?.FullName });
            }
            output.truncated = guids.Length > limit;
            return output;
        }

        [UnityMcpTool("asset-info", Description = "Read project asset metadata.", Category = "asset", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static AssetInfoOutput AssetInfo(AssetPathInput input)
        {
            ValidateExistingAsset(input.path);
            var asset = AssetDatabase.LoadMainAssetAtPath(input.path);
            var file = new FileInfo(Path.GetFullPath(input.path));
            return new AssetInfoOutput { guid = AssetDatabase.AssetPathToGUID(input.path), path = input.path, name = asset?.name, type = asset?.GetType().FullName, fileSize = file.Exists ? file.Length : 0, labels = asset == null ? new List<string>() : AssetDatabase.GetLabels(asset).ToList(), importerType = AssetImporter.GetAtPath(input.path)?.GetType().FullName };
        }

        [UnityMcpTool("asset-dependencies", Description = "List direct or recursive asset dependencies.", Category = "asset", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static AssetDependenciesOutput AssetDependencies(AssetPathInput input)
        {
            ValidateExistingAsset(input.path);
            return new AssetDependenciesOutput { path = input.path, dependencies = AssetDatabase.GetDependencies(input.path, true).Where(p => p != input.path).OrderBy(p => p).ToList() };
        }

        [UnityMcpTool("asset-create-folder", Description = "Create an Assets folder; dry-run unless apply is true.", Category = "asset", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput AssetCreateFolder(AssetCreateFolderInput input, UnityMcpContext context)
        {
            ValidateAssetPath(input.path);
            if (AssetDatabase.IsValidFolder(input.path)) throw new InvalidOperationException("Folder already exists.");
            var parent = Path.GetDirectoryName(input.path)?.Replace('\\', '/');
            var name = Path.GetFileName(input.path);
            if (!AssetDatabase.IsValidFolder(parent)) throw new ArgumentException("Parent folder does not exist.");
            if (!context.DryRun && string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, name))) throw new InvalidOperationException("Unity could not create the folder.");
            return AssetChange(context, $"Create folder '{input.path}'.", "create-folder", null, input.path);
        }

        [UnityMcpTool("asset-move", Description = "Move or rename an asset; dry-run unless apply is true.", Category = "asset", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput AssetMove(AssetMoveInput input, UnityMcpContext context)
        {
            ValidateExistingAsset(input.source); ValidateAssetPath(input.destination);
            if (!context.DryRun) { var error = AssetDatabase.MoveAsset(input.source, input.destination); if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error); }
            return AssetChange(context, $"Move '{input.source}' to '{input.destination}'.", "move", input.source, input.destination);
        }

        [UnityMcpTool("asset-delete", Description = "Delete an asset; dry-run unless apply is true.", Category = "asset", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Destructive, SupportsDryRun = true)]
        public static ChangeOutput AssetDelete(AssetDeleteInput input, UnityMcpContext context)
        {
            ValidateExistingAsset(input.path);
            if (!context.DryRun && !AssetDatabase.DeleteAsset(input.path)) throw new InvalidOperationException("Unity could not delete the asset.");
            return AssetChange(context, $"Delete asset '{input.path}'.", "delete", input.path, null);
        }

        [UnityMcpTool("prefab-info", Description = "Read prefab asset or instance metadata.", Category = "prefab", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static PrefabInfoOutput PrefabInfo(AssetPathInput input)
        {
            ValidateExistingAsset(input.path, ".prefab");
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(input.path);
            if (root == null) throw new ArgumentException("Path is not a prefab asset.");
            return new PrefabInfoOutput { path = input.path, assetType = PrefabUtility.GetPrefabAssetType(root).ToString(), instanceStatus = PrefabUtility.GetPrefabInstanceStatus(root).ToString(), rootName = root.name, componentCount = root.GetComponentsInChildren<Component>(true).Count(c => c != null) };
        }

        [UnityMcpTool("prefab-instantiate", Description = "Instantiate a prefab; dry-run unless apply is true.", Category = "prefab", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput PrefabInstantiate(PrefabInstantiateInput input, UnityMcpContext context)
        {
            ValidateExistingAsset(input.path, ".prefab");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(input.path);
            if (prefab == null) throw new ArgumentException("Path is not a prefab asset.");
            var scene = string.IsNullOrEmpty(input.scene) ? SceneManager.GetActiveScene() : Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).FirstOrDefault(v => v.name == input.scene || v.path == input.scene);
            if (!scene.IsValid()) throw new ArgumentException("Loaded target scene was not found.");
            var parent = input.parentInstanceId.HasValue || !string.IsNullOrEmpty(input.parentPath) ? FindGameObject(input.parentInstanceId, input.parentPath) : null;
            if (context.DryRun) return Change(context, $"Instantiate prefab '{input.path}'.");
            var created = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(created, "UnityMCP Instantiate Prefab");
            if (parent != null) Undo.SetTransformParent(created.transform, parent.transform, "UnityMCP Set Prefab Parent");
            if (input.position.HasValue) created.transform.position = input.position.Value;
            return Change(context, $"Instantiated prefab '{input.path}'.", created.GetInstanceID());
        }

        [UnityMcpTool("material-info", Description = "Read material and shader properties.", Category = "material", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static MaterialInfoOutput MaterialInfo(AssetPathInput input)
        {
            ValidateExistingAsset(input.path);
            var material = AssetDatabase.LoadAssetAtPath<Material>(input.path);
            if (material == null) throw new ArgumentException("Path is not a Material asset.");
            var output = new MaterialInfoOutput { path = input.path, name = material.name, shader = material.shader?.name, renderQueue = material.renderQueue, keywords = material.enabledKeywords.Select(v => v.name).OrderBy(v => v).ToList() };
            if (material.shader != null)
                for (var index = 0; index < material.shader.GetPropertyCount(); index++)
                    output.properties.Add(new MaterialPropertyInfo { name = material.shader.GetPropertyName(index), description = material.shader.GetPropertyDescription(index), type = material.shader.GetPropertyType(index).ToString() });
            return output;
        }

        [UnityMcpTool("material-set-property", Description = "Set a material property from JSON; dry-run unless apply is true.", Category = "material", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput MaterialSetProperty(MaterialSetPropertyInput input, UnityMcpContext context)
        {
            ValidateExistingAsset(input.path);
            var material = AssetDatabase.LoadAssetAtPath<Material>(input.path);
            if (material == null) throw new ArgumentException("Path is not a Material asset.");
            if (!material.HasProperty(input.property)) throw new ArgumentException("Material property was not found.");
            var token = JToken.Parse(input.valueJson ?? "null");
            var index = Enumerable.Range(0, material.shader.GetPropertyCount()).FirstOrDefault(i => material.shader.GetPropertyName(i) == input.property);
            var type = material.shader.GetPropertyType(index);
            if (!context.DryRun)
            {
                Undo.RecordObject(material, "UnityMCP Set Material Property");
                switch (type)
                {
                    case ShaderPropertyType.Color: material.SetColor(input.property, token.ToObject<Color>()); break;
                    case ShaderPropertyType.Vector: material.SetVector(input.property, token.ToObject<Vector4>()); break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range: material.SetFloat(input.property, token.Value<float>()); break;
                    case ShaderPropertyType.Int: material.SetInteger(input.property, token.Value<int>()); break;
                    case ShaderPropertyType.Texture:
                        var texturePath = token.Type == JTokenType.String ? token.Value<string>() : null;
                        material.SetTexture(input.property, string.IsNullOrEmpty(texturePath) ? null : AssetDatabase.LoadAssetAtPath<Texture>(texturePath)); break;
                    default: throw new NotSupportedException("Unsupported shader property type.");
                }
                EditorUtility.SetDirty(material);
            }
            return Change(context, $"Set material property '{input.property}'.");
        }

        [UnityMcpTool("compile-status", Description = "Read current Editor compilation status.", Category = "compilation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static CompileStatusOutput CompileStatus(EmptyInput input) => new CompileStatusOutput { isCompiling = EditorApplication.isCompiling, isUpdating = EditorApplication.isUpdating, assemblies = CompilationPipeline.GetAssemblies().Select(a => a.name).OrderBy(v => v).ToArray() };

        [UnityMcpTool("console-read", Description = "Read recent Unity Console entries.", Category = "console", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static ConsoleReadOutput ConsoleRead(ConsoleReadInput input) => ConsoleReflection.Read(input);

        [UnityMcpTool("compile-errors", Description = "Read current compilation errors from the Unity Console.", Category = "compilation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static CompileErrorsOutput CompileErrors(EmptyInput input)
        {
            var read = ConsoleReflection.Read(new ConsoleReadInput { limit = 1000, severity = "error" });
            return new CompileErrorsOutput { errors = read.entries.Where(e => e.message != null && (e.message.Contains("error CS") || e.message.Contains("Assembly has reference errors"))).ToList() };
        }

        [UnityMcpTool("package-list", Description = "List registered Unity packages.", Category = "package", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, DefaultEnabled = true)]
        public static PackageListOutput PackageList(EmptyInput input)
        {
            var output = new PackageListOutput();
            foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().OrderBy(p => p.name))
                output.packages.Add(new PackageInfoOutput { name = package.name, displayName = package.displayName, version = package.version, source = package.source.ToString(), resolvedPath = package.resolvedPath });
            return output;
        }

        [UnityMcpTool("custom-tool-scaffold", Description = "Generate a disabled-by-default typed C# custom tool skeleton; dry-run unless apply is true.", Category = "custom", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static CustomToolScaffoldOutput CustomToolScaffold(CustomToolScaffoldInput input, UnityMcpContext context)
        {
            ValidateCustomSpec(input);
            var runtime = string.Equals(input.scope, "runtime", StringComparison.OrdinalIgnoreCase) || string.Equals(input.scope, "all", StringComparison.OrdinalIgnoreCase);
            var folder = runtime ? "Assets/UnityMCP/CustomTools/Runtime" : "Assets/UnityMCP/CustomTools/Editor";
            var assetPath = folder + "/" + input.className + ".cs";
            if (File.Exists(Path.GetFullPath(assetPath))) throw new InvalidOperationException("A custom tool file already exists at " + assetPath);
            if (!context.DryRun)
            {
                Directory.CreateDirectory(Path.GetFullPath(folder));
                File.WriteAllText(Path.GetFullPath(assetPath), BuildCustomSource(input), new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }
            return new CustomToolScaffoldOutput
            {
                dryRun = context.DryRun, assetPath = assetPath, className = input.className, toolName = input.toolName, rollbackSupported = false,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "create-script", before = null, after = assetPath } }
            };
        }

        [UnityMcpTool("custom-tool-validate", Description = "Validate discovered project custom tools without enabling them.", Category = "custom", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static CustomToolValidateOutput CustomToolValidate(CustomToolValidateInput input)
        {
            var output = new CustomToolValidateOutput();
            foreach (var method in TypeCache.GetMethodsWithAttribute<UnityMcpToolAttribute>().Where(m => m.DeclaringType?.Assembly.GetName().Name != "DucMinh.UnityMcp.Runtime" && m.DeclaringType?.Assembly.GetName().Name != "DucMinh.UnityMcp.Editor"))
            {
                var attribute = method.GetCustomAttribute<UnityMcpToolAttribute>(false);
                if (!string.IsNullOrEmpty(input.toolName) && attribute.Name != input.toolName) continue;
                var item = new CustomToolValidationItem { toolName = attribute.Name, method = method.DeclaringType.FullName + "." + method.Name, valid = true };
                if (!ToolName.IsMatch(attribute.Name)) item.issues.Add("Tool name must be lower kebab-case.");
                if (!method.IsStatic) item.issues.Add("Tool method must be static.");
                if (!method.IsPublic) item.issues.Add("Tool method must be public.");
                if ((attribute.Scope & UnityMcpScope.Runtime) != 0 && method.DeclaringType.Assembly.GetName().Name.EndsWith(".Editor", StringComparison.OrdinalIgnoreCase)) item.issues.Add("Runtime tool is compiled into an Editor assembly.");
                try { UnityMcpSchemaGenerator.InputFor(method, attribute.SupportsDryRun); UnityMcpSchemaGenerator.OutputFor(method); } catch (Exception exception) { item.issues.Add(exception.Message); }
                item.valid = item.issues.Count == 0;
                output.tools.Add(item);
            }
            return output;
        }

        private static ChangeOutput Change(UnityMcpContext context, string summary, int? instanceId = null) => new ChangeOutput { dryRun = context.DryRun, changed = !context.DryRun, summary = summary, instanceId = instanceId };

        private static ChangeOutput AssetChange(UnityMcpContext context, string summary, string operation, string before, string after) => new ChangeOutput
        {
            dryRun = context.DryRun, changed = !context.DryRun, summary = summary, rollbackSupported = false,
            journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = operation, before = before, after = after } }
        };

        private static void ValidateAssetPath(string path, string extension = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !(path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal)) || path.Contains("..") || Path.IsPathRooted(path)) throw new ArgumentException("Path must be project-relative under Assets and may not contain '..'.");
            if (extension != null && !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Path must end with " + extension + ".");
        }

        private static void ValidateExistingAsset(string path, string extension = null)
        {
            ValidateAssetPath(path, extension);
            if (AssetDatabase.LoadMainAssetAtPath(path) == null && !AssetDatabase.IsValidFolder(path)) throw new ArgumentException("Asset was not found: " + path);
        }

        private static GameObject FindGameObject(int? instanceId, string path)
        {
            if (instanceId.HasValue)
            {
                var value = EditorUtility.EntityIdToObject((EntityId)instanceId.Value) as GameObject;
                if (value != null && value.scene.IsValid()) return value;
            }
            if (!string.IsNullOrEmpty(path))
                foreach (var root in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).SelectMany(s => s.GetRootGameObjects()))
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                        if (ScenePath(transform.gameObject) == path) return transform.gameObject;
            throw new ArgumentException("GameObject was not found.");
        }

        private static string ScenePath(GameObject value)
        {
            var names = new Stack<string>();
            for (var current = value.transform; current != null; current = current.parent) names.Push(current.name);
            return value.scene.name + ":/" + string.Join("/", names.ToArray());
        }

        private static void ValidateCustomSpec(CustomToolScaffoldInput input)
        {
            if (!ToolName.IsMatch(input.toolName ?? "")) throw new ArgumentException("toolName must be lower kebab-case.");
            if (!Identifier.IsMatch(input.className ?? "")) throw new ArgumentException("className must be a valid C# identifier.");
            if (!new[] { "editor", "runtime", "all" }.Contains((input.scope ?? "").ToLowerInvariant())) throw new ArgumentException("scope must be editor, runtime, or all.");
            if (!new[] { "safe-read", "write", "destructive", "unsafe" }.Contains((input.safety ?? "").ToLowerInvariant())) throw new ArgumentException("Unknown safety tier.");
            var allowedTypes = new HashSet<string> { "string", "bool", "int", "float", "double", "Vector2", "Vector3", "Vector4", "Color" };
            foreach (var argument in input.arguments ?? new List<CustomArgumentSpec>())
                if (!Identifier.IsMatch(argument.name ?? "") || !allowedTypes.Contains(argument.type)) throw new ArgumentException("Argument names/types must use the supported typed scaffold set.");
        }

        private static string BuildCustomSource(CustomToolScaffoldInput input)
        {
            var scope = string.Equals(input.scope, "all", StringComparison.OrdinalIgnoreCase) ? "UnityMcpScope.All" : string.Equals(input.scope, "runtime", StringComparison.OrdinalIgnoreCase) ? "UnityMcpScope.Runtime" : "UnityMcpScope.Editor";
            var safety = string.Equals(input.safety, "safe-read", StringComparison.OrdinalIgnoreCase) ? "UnityMcpSafety.SafeRead" : string.Equals(input.safety, "destructive", StringComparison.OrdinalIgnoreCase) ? "UnityMcpSafety.Destructive" : string.Equals(input.safety, "unsafe", StringComparison.OrdinalIgnoreCase) ? "UnityMcpSafety.Unsafe" : "UnityMcpSafety.Write";
            var fields = string.Join("\n", (input.arguments ?? new List<CustomArgumentSpec>()).Select(a => $"        public {a.type}{(a.optional && a.type != "string" ? "?" : "")} {a.name};"));
            var description = (input.description ?? "Project custom UnityMCP tool.").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
            return $@"using System;
using DucMinh.UnityMcp;
using UnityEngine;

public static class {input.className}
{{
    [Serializable]
    public sealed class Args
    {{
{fields}
    }}

    [UnityMcpTool(""{input.toolName}"", Description = ""{description}"", Scope = {scope}, Safety = {safety}, SupportsDryRun = true)]
    public static UnityMcpResult Run(Args input, UnityMcpContext context)
    {{
        if (context.DryRun) return UnityMcpResult.Success(text: ""Dry run: implement validation here."");
        // TODO: implement the project-specific operation.
        return UnityMcpResult.Success(text: ""Custom tool executed."");
    }}
}}
";
        }
    }

    internal sealed class EditorPlayModeOperation : IEditorWorkflowOperation
    {
        private readonly bool enterPlayMode;
        private readonly DateTime deadlineUtc;
        private bool requested;
        private int stableFrames;

        public EditorPlayModeOperation(bool enterPlayMode, DateTime? deadlineUtc = null, bool requestAlreadySubmitted = false)
        {
            this.enterPlayMode = enterPlayMode;
            this.deadlineUtc = deadlineUtc ?? DateTime.UtcNow.AddMinutes(2);
            requested = requestAlreadySubmitted;
        }
        public bool DrainWhenCancelled => false;

        public bool Tick(UnityMcpJob job)
        {
            if (!requested)
            {
                requested = true;
                PlayModeTransitionRecovery.Track(job, enterPlayMode, deadlineUtc);
                EditorApplication.isPlaying = enterPlayMode;
                UnityMcpJobStore.Shared.Report(job, 0.1f, enterPlayMode ? "Entering Play Mode." : "Exiting Play Mode.");
                return false;
            }
            if (DateTime.UtcNow > deadlineUtc)
            {
                EditorWorkflowJobRunner.Fail(job, "Timed out waiting for the Editor to reach a stable Play Mode state.");
                PlayModeTransitionRecovery.Clear(job.jobId);
                return true;
            }
            var targetReached = EditorApplication.isPlaying == enterPlayMode && !EditorApplication.isCompiling && !EditorApplication.isUpdating;
            if (!targetReached) { stableFrames = 0; return false; }
            stableFrames++;
            UnityMcpJobStore.Shared.Report(job, Math.Min(0.95f, 0.5f + stableFrames * 0.2f), "Waiting for the Editor to remain stable.");
            if (stableFrames < 3) return false;
            EditorWorkflowJobRunner.Succeed(job, new PlayModeSessionOutput
            {
                isPlaying = EditorApplication.isPlaying, isPaused = EditorApplication.isPaused,
                isCompiling = EditorApplication.isCompiling, isUpdating = EditorApplication.isUpdating, stableEditorFrames = stableFrames
            });
            PlayModeTransitionRecovery.Clear(job.jobId);
            return true;
        }
    }

    [InitializeOnLoad]
    internal static class PlayModeTransitionRecovery
    {
        private const string SessionKey = "DucMinh.UnityMcp.PlayModeTransition";

        static PlayModeTransitionRecovery()
        {
            EditorApplication.delayCall += Restore;
        }

        public static void Track(UnityMcpJob job, bool targetPlayMode, DateTime deadlineUtc)
        {
            if (job == null) return;
            SessionState.SetString(SessionKey, JsonConvert.SerializeObject(new PersistedPlayModeTransition
            {
                jobId = job.jobId,
                targetPlayMode = targetPlayMode,
                deadlineUtc = deadlineUtc.ToUniversalTime().ToString("O"),
                createdUtc = job.createdUtc,
                startedUtc = job.startedUtc,
                progress = job.progress,
                progressMessage = job.progressMessage
            }));
        }

        public static void Clear(string jobId)
        {
            var raw = SessionState.GetString(SessionKey, string.Empty);
            if (string.IsNullOrEmpty(raw)) return;
            try
            {
                var persisted = JsonConvert.DeserializeObject<PersistedPlayModeTransition>(raw);
                if (persisted == null || string.Equals(persisted.jobId, jobId, StringComparison.Ordinal)) SessionState.EraseString(SessionKey);
            }
            catch { SessionState.EraseString(SessionKey); }
        }

        private static void Restore()
        {
            var raw = SessionState.GetString(SessionKey, string.Empty);
            if (string.IsNullOrEmpty(raw)) return;
            PersistedPlayModeTransition persisted;
            try { persisted = JsonConvert.DeserializeObject<PersistedPlayModeTransition>(raw); }
            catch { SessionState.EraseString(SessionKey); return; }
            if (persisted == null || string.IsNullOrWhiteSpace(persisted.jobId) || !DateTime.TryParse(persisted.deadlineUtc, out var deadlineUtc)) { SessionState.EraseString(SessionKey); return; }
            var job = UnityMcpJobStore.Shared.Restore(persisted.jobId, "play-mode", "running", persisted.progress, persisted.progressMessage, persisted.createdUtc, persisted.startedUtc);
            if (DateTime.UtcNow > deadlineUtc)
            {
                UnityMcpJobStore.Shared.Fail(job, "Timed out while Unity reloaded the domain during the Play Mode transition.");
                SessionState.EraseString(SessionKey);
                return;
            }
            EditorWorkflowJobRunner.Resume(job, new EditorPlayModeOperation(persisted.targetPlayMode, deadlineUtc.ToUniversalTime(), true));
        }
    }

    internal static class ConsoleReflection
    {
        public static ConsoleReadOutput Read(ConsoleReadInput input)
        {
            var output = new ConsoleReadOutput();
            var logEntries = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntries");
            var logEntry = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntry");
            if (logEntries == null || logEntry == null) return output;
            var count = (int)(logEntries.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null) ?? 0);
            var start = logEntries.GetMethod("StartGettingEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var end = logEntries.GetMethod("EndGettingEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var get = logEntries.GetMethod("GetEntryInternal", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var limit = Math.Max(1, Math.Min(input.limit, 1000));
            var afterCursor = input.afterCursor;
            var cursorReset = afterCursor >= count;
            if (cursorReset) afterCursor = -1;
            var first = afterCursor >= 0 ? Math.Min(count, (int)afterCursor + 1) : count - 1;
            var step = afterCursor >= 0 ? 1 : -1;
            var observedUtc = DateTime.UtcNow.ToString("O");
            output.cursorReset = cursorReset;
            start?.Invoke(null, null);
            try
            {
                for (var index = first; index >= 0 && index < count; index += step)
                {
                    var entry = Activator.CreateInstance(logEntry);
                    get?.Invoke(null, new[] { (object)index, entry });
                    var mode = Convert.ToInt32(Field(logEntry, entry, "mode") ?? 0);
                    var severity = (mode & (1 << 0 | 1 << 1 | 1 << 4 | 1 << 6 | 1 << 7 | 1 << 8 | 1 << 9)) != 0 ? "error" : (mode & (1 << 2 | 1 << 3 | 1 << 5)) != 0 ? "warning" : "log";
                    var message = Convert.ToString(Field(logEntry, entry, "condition"));
                    if (!string.IsNullOrEmpty(input.severity) && !string.Equals(input.severity, severity, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(input.contains) && (message == null || message.IndexOf(input.contains, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    if (output.entries.Count >= limit) { output.truncated = true; break; }
                    output.entries.Add(new ConsoleEntryInfo { cursor = index, observedUtc = observedUtc, message = message, stackTrace = Convert.ToString(Field(logEntry, entry, "stackTrace")), file = Convert.ToString(Field(logEntry, entry, "file")), line = Convert.ToInt32(Field(logEntry, entry, "line") ?? 0), severity = severity });
                }
            }
            finally { end?.Invoke(null, null); }
            if (output.entries.Count > 0)
            {
                output.firstCursor = output.entries[0].cursor;
                output.lastCursor = output.entries[output.entries.Count - 1].cursor;
            }
            output.nextCursor = count - 1;
            return output;
        }

        private static object Field(Type type, object value, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value);
    }
}
