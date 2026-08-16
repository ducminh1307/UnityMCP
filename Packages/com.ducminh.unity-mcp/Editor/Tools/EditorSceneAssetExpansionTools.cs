using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable] public sealed class EditorPauseInput { public bool paused; public bool apply; }
    [Serializable] public sealed class SceneCloseInput { public string scene; public bool saveDirtyScene; public bool discardDirtyChanges; public bool apply; }
    [Serializable] public sealed class SceneValidationInput { public string scene; public bool includeInactive = true; public int maxIssues = 100; public string profile = "references"; }
    [Serializable] public sealed class SceneValidationIssue { public string kind; public string message; public int instanceId; public string hierarchyPath; public int componentIndex = -1; public string propertyPath; }
    [Serializable] public sealed class SceneValidationOutput { public string scene; public string profile; public int objectsScanned; public int componentsScanned; public List<SceneValidationIssue> issues = new List<SceneValidationIssue>(); public bool truncated; }
    [Serializable] public sealed class AssetImportInput { public string path; public bool forceUpdate = true; public bool apply; }
    [Serializable] public sealed class AssetImportOutput { public bool dryRun; public bool imported; public string path; public string guid; public string importerType; public string assetType; public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>(); }
    [Serializable] public sealed class AssetCopyInput { public string source; public string destination; public bool apply; }
    [Serializable] public sealed class TextureImportSettingsOutput { public string textureType; public bool sRgbTexture; public bool mipmapEnabled; public string wrapMode; public string filterMode; public int maxTextureSize; }
    [Serializable] public sealed class AssetImportSettingsOutput { public string path; public string importerType; public string assetBundleName; public string assetBundleVariant; public bool hasUserData; public TextureImportSettingsOutput texture; }
    [Serializable] public sealed class AssetImportSettingsSetInput
    {
        public string path;
        public string assetBundleName;
        public string assetBundleVariant;
        public TextureImporterType? textureType;
        public bool? sRgbTexture;
        public bool? mipmapEnabled;
        public TextureWrapMode? wrapMode;
        public FilterMode? filterMode;
        public int? maxTextureSize;
        public bool apply;
    }
    [Serializable] public sealed class PrefabCreateInput { public int? instanceId; public string hierarchyPath; public string destination; public bool apply; }
    [Serializable] public sealed class PrefabInstanceInput { public int? instanceId; public string hierarchyPath; public bool apply; }
    [Serializable] public sealed class PrefabUnpackInput { public int? instanceId; public string hierarchyPath; public bool completely = true; public bool apply; }
    [Serializable] public sealed class ScriptableObjectCreateInput { public string type; public string path; public bool apply; }
    [Serializable] public sealed class ScriptableObjectCreateOutput { public bool dryRun; public bool created; public string path; public string type; public string guid; public bool rollbackSupported; public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>(); }
    [Serializable] public sealed class ScriptableObjectGetInput { public string path; }
    [Serializable] public sealed class ScriptableObjectFieldInfo { public string name; public string type; public string valueJson; }
    [Serializable] public sealed class ScriptableObjectGetOutput { public string path; public string type; public string name; public List<ScriptableObjectFieldInfo> fields = new List<ScriptableObjectFieldInfo>(); }
    [Serializable] public sealed class ScriptableObjectFieldSet { public string name; public string valueJson; }
    [Serializable] public sealed class ScriptableObjectSetInput { public string path; public List<ScriptableObjectFieldSet> fields = new List<ScriptableObjectFieldSet>(); public bool apply; }

    /// <summary>
    /// Editor-only tools whose operations are deliberately bounded to Assets/ and loaded
    /// scenes. Asset mutations are dry-run by default and report a compact journal.
    /// </summary>
    public static class EditorSceneAssetExpansionTools
    {
        private const string UndoName = "UnityMCP";

        [UnityMcpTool("editor-pause", Description = "Set or clear the Editor pause state; dry-run unless apply is true.", Category = "editor", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput EditorPause(EditorPauseInput input, UnityMcpContext context)
        {
            if (!context.DryRun) EditorApplication.isPaused = input.paused;
            return Change(context, input.paused ? "Pause the Unity Editor." : "Resume the Unity Editor.");
        }

        [UnityMcpTool("editor-step", Description = "Advance exactly one frame while the Editor is in paused Play Mode; dry-run unless apply is true.", Category = "editor", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput EditorStep(EditorActionInput input, UnityMcpContext context)
        {
            if (!EditorApplication.isPlaying || !EditorApplication.isPaused)
                throw new InvalidOperationException("The Editor must be in paused Play Mode before stepping a frame.");
            if (!context.DryRun) EditorApplication.Step();
            return Change(context, "Advance one paused Play Mode frame.");
        }

        [UnityMcpTool("editor-undo", Description = "Request one Unity Editor undo; dry-run unless apply is true.", Category = "editor", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput EditorUndo(EditorActionInput input, UnityMcpContext context)
        {
            if (!context.DryRun) Undo.PerformUndo();
            return Change(context, "Request one Unity Editor undo.");
        }

        [UnityMcpTool("editor-redo", Description = "Request one Unity Editor redo; dry-run unless apply is true.", Category = "editor", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput EditorRedo(EditorActionInput input, UnityMcpContext context)
        {
            if (!context.DryRun) Undo.PerformRedo();
            return Change(context, "Request one Unity Editor redo.");
        }

        [UnityMcpTool("scene-close", Description = "Close an additive loaded scene with explicit dirty-scene handling; dry-run unless apply is true.", Category = "scene", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput SceneClose(SceneCloseInput input, UnityMcpContext context)
        {
            var scene = RequireLoadedScene(input.scene, true);
            if (scene == SceneManager.GetActiveScene())
                throw new InvalidOperationException("The active scene cannot be closed by this tool. Set another loaded scene active first.");
            if (SceneManager.sceneCount <= 1)
                throw new InvalidOperationException("The last loaded scene cannot be closed by this tool.");
            if (scene.isDirty && !input.saveDirtyScene && !input.discardDirtyChanges)
                throw new InvalidOperationException("The target scene has unsaved changes. Set saveDirtyScene or discardDirtyChanges explicitly.");
            if (scene.isDirty && input.saveDirtyScene && string.IsNullOrEmpty(scene.path))
                throw new InvalidOperationException("The target scene has no asset path. Save it explicitly before closing it.");

            if (!context.DryRun)
            {
                if (scene.isDirty && input.saveDirtyScene && !EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException("Unity could not save the dirty scene.");
                if (!EditorSceneManager.CloseScene(scene, true))
                    throw new InvalidOperationException("Unity could not close the scene.");
            }
            return Change(context, "Close loaded scene '" + scene.name + "'.");
        }

        [UnityMcpTool("scene-validate", Description = "Validate references and optional lifecycle hazards in one loaded scene.", Category = "scene", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static SceneValidationOutput SceneValidate(SceneValidationInput input)
        {
            var scene = RequireLoadedScene(input.scene, false);
            var limit = Math.Max(1, Math.Min(input.maxIssues, 1000));
            var profile = (input.profile ?? "references").Trim().ToLowerInvariant();
            if (profile != "references" && profile != "lifecycle" && profile != "all") throw new ArgumentException("profile must be references, lifecycle, or all.");
            var includeReferences = profile == "references" || profile == "all";
            var includeLifecycle = profile == "lifecycle" || profile == "all";
            var output = new SceneValidationOutput { scene = string.IsNullOrEmpty(scene.path) ? scene.name : scene.path, profile = profile };
            var rootNames = includeLifecycle ? new HashSet<string>(StringComparer.Ordinal) : null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (includeLifecycle && !rootNames.Add(root.name))
                    AddIssue(output, limit, new SceneValidationIssue { kind = "duplicate-root-name", message = "Multiple root GameObjects share this name; verify duplicate bootstrap objects are intentional.", instanceId = root.GetInstanceID(), hierarchyPath = HierarchyPath(root) });
                foreach (var gameObject in Traverse(root))
                {
                    if (!input.includeInactive && !gameObject.activeInHierarchy) continue;
                    output.objectsScanned++;
                    if (includeLifecycle && (gameObject.hideFlags & HideFlags.DontSave) != 0)
                    {
                        AddIssue(output, limit, new SceneValidationIssue { kind = "dont-save-object", message = "GameObject has DontSave hide flags and can survive lifecycle transitions unexpectedly.", instanceId = gameObject.GetInstanceID(), hierarchyPath = HierarchyPath(gameObject) });
                        if (output.truncated) return output;
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
                                instanceId = gameObject.GetInstanceID(),
                                hierarchyPath = HierarchyPath(gameObject),
                                componentIndex = componentIndex
                            });
                            if (output.truncated) return output;
                            continue;
                        }

                        output.componentsScanned++;
                        if (!includeReferences) continue;
                        try
                        {
                            var serialized = new SerializedObject(component);
                            var property = serialized.GetIterator();
                            var enterChildren = true;
                            while (property.NextVisible(enterChildren))
                            {
                                enterChildren = false;
                                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                                if (property.objectReferenceValue != null || property.objectReferenceInstanceIDValue == 0) continue;
                                AddIssue(output, limit, new SceneValidationIssue
                                {
                                    kind = "broken-object-reference",
                                    message = "A serialized object reference is missing.",
                                    instanceId = gameObject.GetInstanceID(),
                                    hierarchyPath = HierarchyPath(gameObject),
                                    componentIndex = componentIndex,
                                    propertyPath = property.propertyPath
                                });
                                if (output.truncated) return output;
                            }
                        }
                        catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
                        {
                            // Some built-in components intentionally cannot expose a SerializedObject.
                            // A failed projection is not itself a scene validation failure.
                        }
                    }
                }
            }
            return output;
        }

        [UnityMcpTool("asset-import", Description = "Import a file already present below Assets/; dry-run unless apply is true.", Category = "asset", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static AssetImportOutput AssetImport(AssetImportInput input, UnityMcpContext context)
        {
            var path = RequireExistingProjectFile(input.path);
            if (!context.DryRun) AssetDatabase.ImportAsset(path, ImportOptions(input.forceUpdate));
            return ImportOutput(path, context, "import");
        }

        [UnityMcpTool("asset-reimport", Description = "Force reimport a known project asset; dry-run unless apply is true.", Category = "asset", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static AssetImportOutput AssetReimport(AssetImportInput input, UnityMcpContext context)
        {
            var path = RequireExistingImportedAsset(input.path);
            if (!context.DryRun) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            return ImportOutput(path, context, "reimport");
        }

        [UnityMcpTool("asset-copy", Description = "Copy an existing asset with Unity-managed metadata; dry-run unless apply is true.", Category = "asset", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput AssetCopy(AssetCopyInput input, UnityMcpContext context)
        {
            var source = RequireExistingImportedAsset(input.source);
            var destination = NormalizeAssetPath(input.destination, null);
            if (AssetDatabase.LoadMainAssetAtPath(destination) != null || File.Exists(ToFullPath(destination)) || Directory.Exists(ToFullPath(destination)))
                throw new InvalidOperationException("The destination already exists.");
            var parent = Path.GetDirectoryName(destination)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) throw new ArgumentException("The destination parent folder does not exist.");
            if (!context.DryRun && !AssetDatabase.CopyAsset(source, destination))
                throw new InvalidOperationException("Unity could not copy the asset.");
            return AssetChange(context, "Copy asset '" + source + "' to '" + destination + "'.", "copy", source, destination);
        }

        [UnityMcpTool("asset-import-settings-get", Description = "Read the supported, non-secret settings of an asset importer.", Category = "asset", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static AssetImportSettingsOutput AssetImportSettingsGet(AssetPathInput input)
        {
            var path = RequireExistingImportedAsset(input.path);
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null) throw new ArgumentException("No asset importer was found at the path.");
            var output = new AssetImportSettingsOutput
            {
                path = path,
                importerType = importer.GetType().FullName,
                assetBundleName = importer.assetBundleName,
                assetBundleVariant = importer.assetBundleVariant,
                hasUserData = !string.IsNullOrEmpty(importer.userData)
            };
            if (importer is TextureImporter texture)
            {
                output.texture = new TextureImportSettingsOutput
                {
                    textureType = texture.textureType.ToString(),
                    sRgbTexture = texture.sRGBTexture,
                    mipmapEnabled = texture.mipmapEnabled,
                    wrapMode = texture.wrapMode.ToString(),
                    filterMode = texture.filterMode.ToString(),
                    maxTextureSize = texture.maxTextureSize
                };
            }
            return output;
        }

        [UnityMcpTool("asset-import-settings-set", Description = "Set a validated subset of importer settings and reimport; dry-run unless apply is true.", Category = "asset", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput AssetImportSettingsSet(AssetImportSettingsSetInput input, UnityMcpContext context)
        {
            var path = RequireExistingImportedAsset(input.path);
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null) throw new ArgumentException("No asset importer was found at the path.");
            var hasGenericUpdate = input.assetBundleName != null || input.assetBundleVariant != null;
            var hasTextureUpdate = input.textureType.HasValue || input.sRgbTexture.HasValue || input.mipmapEnabled.HasValue || input.wrapMode.HasValue || input.filterMode.HasValue || input.maxTextureSize.HasValue;
            if (!hasGenericUpdate && !hasTextureUpdate) throw new ArgumentException("Provide at least one supported importer setting.");
            if (hasTextureUpdate && !(importer is TextureImporter)) throw new ArgumentException("Texture settings can only be set on a TextureImporter.");
            if (input.maxTextureSize.HasValue && !IsSupportedTextureSize(input.maxTextureSize.Value))
                throw new ArgumentOutOfRangeException(nameof(input.maxTextureSize), "maxTextureSize must be a power of two from 32 through 16384.");

            if (!context.DryRun)
            {
                Undo.RecordObject(importer, UndoName + " Set Import Settings");
                if (input.assetBundleName != null) importer.assetBundleName = input.assetBundleName;
                if (input.assetBundleVariant != null) importer.assetBundleVariant = input.assetBundleVariant;
                var texture = importer as TextureImporter;
                if (texture != null)
                {
                    if (input.textureType.HasValue) texture.textureType = input.textureType.Value;
                    if (input.sRgbTexture.HasValue) texture.sRGBTexture = input.sRgbTexture.Value;
                    if (input.mipmapEnabled.HasValue) texture.mipmapEnabled = input.mipmapEnabled.Value;
                    if (input.wrapMode.HasValue) texture.wrapMode = input.wrapMode.Value;
                    if (input.filterMode.HasValue) texture.filterMode = input.filterMode.Value;
                    if (input.maxTextureSize.HasValue) texture.maxTextureSize = input.maxTextureSize.Value;
                }
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
            return AssetChange(context, "Update supported importer settings for '" + path + "'.", "set-import-settings", path, path);
        }

        [UnityMcpTool("prefab-create", Description = "Create a new prefab asset from a scene GameObject; dry-run unless apply is true.", Category = "prefab", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput PrefabCreate(PrefabCreateInput input, UnityMcpContext context)
        {
            var source = RequireSceneGameObject(input.instanceId, input.hierarchyPath);
            var destination = NormalizeAssetPath(input.destination, ".prefab");
            if (AssetDatabase.LoadMainAssetAtPath(destination) != null || File.Exists(ToFullPath(destination)))
                throw new InvalidOperationException("The destination prefab already exists.");
            var parent = Path.GetDirectoryName(destination)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) throw new ArgumentException("The destination parent folder does not exist.");
            if (!context.DryRun)
            {
                PrefabUtility.SaveAsPrefabAsset(source, destination);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(destination) == null)
                    throw new InvalidOperationException("Unity did not create the requested prefab asset.");
            }
            return AssetChange(context, "Create prefab '" + destination + "' from '" + HierarchyPath(source) + "'.", "create-prefab", null, destination);
        }

        [UnityMcpTool("prefab-apply", Description = "Apply all overrides on a prefab instance to its source asset; dry-run unless apply is true.", Category = "prefab", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput PrefabApply(PrefabInstanceInput input, UnityMcpContext context)
        {
            var root = RequirePrefabInstanceRoot(input.instanceId, input.hierarchyPath);
            var sourcePath = PrefabSourcePath(root);
            if (!context.DryRun) PrefabUtility.ApplyPrefabInstance(root, InteractionMode.AutomatedAction);
            return AssetChange(context, "Apply all overrides from '" + HierarchyPath(root) + "' to '" + sourcePath + "'.", "apply-prefab", HierarchyPath(root), sourcePath);
        }

        [UnityMcpTool("prefab-revert", Description = "Revert all overrides on a prefab instance; dry-run unless apply is true.", Category = "prefab", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Destructive, SupportsDryRun = true)]
        public static ChangeOutput PrefabRevert(PrefabInstanceInput input, UnityMcpContext context)
        {
            var root = RequirePrefabInstanceRoot(input.instanceId, input.hierarchyPath);
            if (!context.DryRun) PrefabUtility.RevertPrefabInstance(root, InteractionMode.AutomatedAction);
            return Change(context, "Revert all prefab overrides on '" + HierarchyPath(root) + "'.", root.GetInstanceID());
        }

        [UnityMcpTool("prefab-unpack", Description = "Unpack a prefab instance one level or completely; dry-run unless apply is true.", Category = "prefab", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Destructive, SupportsDryRun = true)]
        public static ChangeOutput PrefabUnpack(PrefabUnpackInput input, UnityMcpContext context)
        {
            var root = RequirePrefabInstanceRoot(input.instanceId, input.hierarchyPath);
            var mode = input.completely ? PrefabUnpackMode.Completely : PrefabUnpackMode.OutermostRoot;
            if (!context.DryRun) PrefabUtility.UnpackPrefabInstance(root, mode, InteractionMode.AutomatedAction);
            return Change(context, "Unpack prefab instance '" + HierarchyPath(root) + "' " + (input.completely ? "completely." : "one level."), root.GetInstanceID());
        }

        [UnityMcpTool("scriptableobject-create", Description = "Create an asset from a project type explicitly listed in UnityMCP's ScriptableObject allowlist; dry-run unless apply is true.", Category = "prefab", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ScriptableObjectCreateOutput ScriptableObjectCreate(ScriptableObjectCreateInput input, UnityMcpContext context)
        {
            var type = RequireAllowedScriptableObjectType(input.type);
            var path = NormalizeAssetPath(input.path, ".asset");
            if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(ToFullPath(path)))
                throw new InvalidOperationException("The destination asset already exists.");
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) throw new ArgumentException("The destination parent folder does not exist.");
            if (!context.DryRun)
            {
                var asset = ScriptableObject.CreateInstance(type);
                try
                {
                    asset.name = Path.GetFileNameWithoutExtension(path);
                    AssetDatabase.CreateAsset(asset, path);
                    AssetDatabase.SaveAssetIfDirty(asset);
                }
                catch
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                    throw;
                }
            }
            return new ScriptableObjectCreateOutput
            {
                dryRun = context.DryRun,
                created = !context.DryRun,
                path = path,
                type = type.FullName,
                guid = context.DryRun ? null : AssetDatabase.AssetPathToGUID(path),
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "create-scriptableobject", after = path } }
            };
        }

        [UnityMcpTool("scriptableobject-get", Description = "Read supported public serialized fields from an allowlisted ScriptableObject asset.", Category = "prefab", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static ScriptableObjectGetOutput ScriptableObjectGet(ScriptableObjectGetInput input)
        {
            var path = RequireExistingImportedAsset(input.path);
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset == null) throw new ArgumentException("Path is not a ScriptableObject asset.");
            var type = RequireAllowedScriptableObjectType(asset.GetType().FullName);
            var output = new ScriptableObjectGetOutput { path = path, type = type.FullName, name = asset.name };
            foreach (var field in SupportedScriptableFields(type))
                output.fields.Add(new ScriptableObjectFieldInfo { name = field.Name, type = FriendlyTypeName(field.FieldType), valueJson = SerializeScriptableValue(field.GetValue(asset), field.FieldType) });
            return output;
        }

        [UnityMcpTool("scriptableobject-set", Description = "Set supported public serialized fields on an allowlisted ScriptableObject; dry-run unless apply is true.", Category = "prefab", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput ScriptableObjectSet(ScriptableObjectSetInput input, UnityMcpContext context)
        {
            var path = RequireExistingImportedAsset(input.path);
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset == null) throw new ArgumentException("Path is not a ScriptableObject asset.");
            var type = RequireAllowedScriptableObjectType(asset.GetType().FullName);
            if (input.fields == null || input.fields.Count == 0) throw new ArgumentException("Provide at least one field update.");
            var allowedFields = SupportedScriptableFields(type).ToDictionary(field => field.Name, StringComparer.Ordinal);
            var updates = new List<ScriptableObjectFieldUpdate>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in input.fields)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.name) || !names.Add(entry.name))
                    throw new ArgumentException("Each field update must have a unique supported name.");
                if (!allowedFields.TryGetValue(entry.name, out var field))
                    throw new ArgumentException("Field is not supported for this allowlisted ScriptableObject: " + entry.name);
                updates.Add(new ScriptableObjectFieldUpdate { field = field, value = ParseScriptableValue(entry.valueJson, field.FieldType) });
            }

            if (!context.DryRun)
            {
                Undo.RecordObject(asset, UndoName + " Set ScriptableObject Fields");
                foreach (var update in updates) update.field.SetValue(asset, update.value);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
            }
            var result = Change(context, "Update " + updates.Count + " supported ScriptableObject field(s) on '" + path + "'.", asset.GetInstanceID());
            result.rollbackSupported = !context.DryRun;
            foreach (var update in updates)
                result.journal.Add(new ChangeJournalEntry { operation = "set-scriptableobject-field", before = update.field.Name, after = update.field.Name });
            return result;
        }

        private sealed class ScriptableObjectFieldUpdate { public FieldInfo field; public object value; }

        private static ChangeOutput Change(UnityMcpContext context, string summary, int? instanceId = null)
        {
            return new ChangeOutput { dryRun = context.DryRun, changed = !context.DryRun, summary = summary, instanceId = instanceId };
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

        private static AssetImportOutput ImportOutput(string path, UnityMcpContext context, string operation)
        {
            var asset = context.DryRun ? null : AssetDatabase.LoadMainAssetAtPath(path);
            var importer = context.DryRun ? null : AssetImporter.GetAtPath(path);
            return new AssetImportOutput
            {
                dryRun = context.DryRun,
                imported = !context.DryRun,
                path = path,
                guid = context.DryRun ? null : AssetDatabase.AssetPathToGUID(path),
                assetType = asset == null ? null : asset.GetType().FullName,
                importerType = importer == null ? null : importer.GetType().FullName,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = operation, after = path } }
            };
        }

        private static ImportAssetOptions ImportOptions(bool forceUpdate)
        {
            return forceUpdate ? ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport : ImportAssetOptions.ForceSynchronousImport;
        }

        private static Scene RequireLoadedScene(string selector, bool requireExplicit)
        {
            if (requireExplicit && string.IsNullOrWhiteSpace(selector))
                throw new ArgumentException("scene is required for this operation.");
            var result = string.IsNullOrWhiteSpace(selector) ? SceneManager.GetActiveScene() : default(Scene);
            if (!string.IsNullOrWhiteSpace(selector))
            {
                for (var index = 0; index < SceneManager.sceneCount; index++)
                {
                    var candidate = SceneManager.GetSceneAt(index);
                    if (string.Equals(candidate.name, selector, StringComparison.OrdinalIgnoreCase) || string.Equals(candidate.path, selector, StringComparison.OrdinalIgnoreCase))
                    {
                        result = candidate;
                        break;
                    }
                }
            }
            if (!result.IsValid() || !result.isLoaded) throw new ArgumentException("The requested scene is not loaded.");
            return result;
        }

        private static IEnumerable<GameObject> Traverse(GameObject root)
        {
            yield return root;
            for (var index = 0; index < root.transform.childCount; index++)
                foreach (var child in Traverse(root.transform.GetChild(index).gameObject)) yield return child;
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

        private static string HierarchyPath(GameObject gameObject)
        {
            var names = new Stack<string>();
            for (var current = gameObject.transform; current != null; current = current.parent) names.Push(current.name);
            return gameObject.scene.name + ":/" + string.Join("/", names.ToArray());
        }

        private static GameObject RequireSceneGameObject(int? instanceId, string hierarchyPath)
        {
            foreach (var sceneIndex in Enumerable.Range(0, SceneManager.sceneCount))
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var candidate in Traverse(root))
                    {
                        if (instanceId.HasValue && candidate.GetInstanceID() == instanceId.Value) return candidate;
                        if (!instanceId.HasValue && !string.IsNullOrWhiteSpace(hierarchyPath) && string.Equals(HierarchyPath(candidate), hierarchyPath, StringComparison.Ordinal)) return candidate;
                    }
                }
            }
            throw new ArgumentException("GameObject was not found; provide a valid scene instanceId or full hierarchyPath.");
        }

        private static GameObject RequirePrefabInstanceRoot(int? instanceId, string hierarchyPath)
        {
            var value = RequireSceneGameObject(instanceId, hierarchyPath);
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(value);
            if (root == null) throw new ArgumentException("The selected GameObject is not part of a prefab instance.");
            if (string.IsNullOrEmpty(PrefabSourcePath(root))) throw new ArgumentException("The prefab instance does not have a valid source asset.");
            return root;
        }

        private static string PrefabSourcePath(GameObject root)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(root);
            return source == null ? null : AssetDatabase.GetAssetPath(source);
        }

        private static string RequireExistingProjectFile(string path)
        {
            var normalized = NormalizeAssetPath(path, null);
            if (!File.Exists(ToFullPath(normalized))) throw new ArgumentException("A file was not found under Assets at the requested path.");
            return normalized;
        }

        private static string RequireExistingImportedAsset(string path)
        {
            var normalized = NormalizeAssetPath(path, null);
            if (AssetDatabase.IsValidFolder(normalized)) throw new ArgumentException("A file asset, not a folder, is required.");
            if (AssetDatabase.LoadMainAssetAtPath(normalized) == null) throw new ArgumentException("No imported asset was found at the requested path.");
            return normalized;
        }

        private static bool IsSupportedTextureSize(int value)
        {
            return value >= 32 && value <= 16384 && (value & (value - 1)) == 0;
        }

        private static string NormalizeAssetPath(string path, string requiredExtension)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A project-relative path under Assets is required.");
            var normalized = path.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(normalized) || normalized == "Assets" || !normalized.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Path must be project-relative under Assets/.");
            if (normalized.Split('/').Any(part => string.IsNullOrEmpty(part) || part == "." || part == ".."))
                throw new ArgumentException("Path may not contain empty, '.' or '..' segments.");
            if (normalized.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Unity .meta files are not valid tool targets.");
            if (requiredExtension != null && !normalized.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Path must end with " + requiredExtension + ".");
            var assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = ToFullPath(normalized);
            if (!fullPath.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Path escapes the project's Assets directory.");
            return normalized;
        }

        private static string ToFullPath(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) throw new InvalidOperationException("Could not determine the Unity project root.");
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static IEnumerable<FieldInfo> SupportedScriptableFields(Type type)
        {
            return type.GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => !field.IsInitOnly && !field.IsNotSerialized && IsSupportedScriptableFieldType(field.FieldType))
                .OrderBy(field => field.Name, StringComparer.Ordinal);
        }

        private static bool IsSupportedScriptableFieldType(Type type)
        {
            if (type.IsEnum || type == typeof(string) || type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte) ||
                type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) || type == typeof(long) ||
                type == typeof(ulong) || type == typeof(float) || type == typeof(double) || type == typeof(Vector2) || type == typeof(Vector3) ||
                type == typeof(Vector4) || type == typeof(Quaternion) || type == typeof(Color) || type == typeof(Rect) || type == typeof(Bounds)) return true;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return true;
            if (type.IsArray) return IsSupportedNonObjectCollectionElement(type.GetElementType());
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && IsSupportedNonObjectCollectionElement(type.GetGenericArguments()[0]);
        }

        private static bool IsSupportedNonObjectCollectionElement(Type type)
        {
            return !typeof(UnityEngine.Object).IsAssignableFrom(type) && IsSupportedScriptableFieldType(type) && !type.IsArray && !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>));
        }

        private static string SerializeScriptableValue(object value, Type type)
        {
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                var unityObject = value as UnityEngine.Object;
                var path = unityObject == null ? null : AssetDatabase.GetAssetPath(unityObject);
                return JsonConvert.SerializeObject(string.IsNullOrEmpty(path) ? null : path);
            }
            return JsonConvert.SerializeObject(value);
        }

        private static object ParseScriptableValue(string valueJson, Type type)
        {
            JToken token;
            try { token = JToken.Parse(valueJson ?? "null"); }
            catch (JsonReaderException exception) { throw new ArgumentException("valueJson must contain valid JSON.", exception); }
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                if (token.Type == JTokenType.Null) return null;
                if (token.Type != JTokenType.String) throw new ArgumentException("Unity object fields must use a project asset path string or null.");
                var path = RequireExistingImportedAsset(token.Value<string>());
                var value = AssetDatabase.LoadAssetAtPath(path, type);
                if (value == null) throw new ArgumentException("The referenced asset does not match the field type.");
                return value;
            }
            if (token.Type == JTokenType.Null && type.IsValueType) throw new ArgumentException("A value-type field cannot be set to null.");
            try { return token.ToObject(type, JsonSerializer.CreateDefault()); }
            catch (Exception exception) when (exception is JsonException || exception is InvalidCastException || exception is ArgumentException)
            {
                throw new ArgumentException("valueJson does not match the supported field type " + FriendlyTypeName(type) + ".", exception);
            }
        }

        private static Type RequireAllowedScriptableObjectType(string requestedType)
        {
            if (string.IsNullOrWhiteSpace(requestedType)) throw new ArgumentException("type is required and must be explicitly allowlisted.");
            foreach (var type in AllowedScriptableObjectTypes())
                if (string.Equals(type.FullName, requestedType, StringComparison.Ordinal) || string.Equals(type.AssemblyQualifiedName, requestedType, StringComparison.Ordinal)) return type;
            throw new ArgumentException("The ScriptableObject type is not in the local UnityMCP allowlist.");
        }

        private static IEnumerable<Type> AllowedScriptableObjectTypes()
        {
            var result = new HashSet<Type>();
            foreach (var guid in AssetDatabase.FindAssets("t:UnityMcpScriptableObjectAllowlist"))
            {
                var allowlist = AssetDatabase.LoadAssetAtPath<UnityMcpScriptableObjectAllowlist>(AssetDatabase.GUIDToAssetPath(guid));
                if (allowlist == null) continue;
                foreach (var name in allowlist.allowedTypeNames ?? new List<string>())
                {
                    var type = ResolveType(name);
                    if (type != null && type != typeof(UnityMcpScriptableObjectAllowlist) && !type.IsAbstract && typeof(ScriptableObject).IsAssignableFrom(type)) result.Add(type);
                }
            }
            return result;
        }

        private static Type ResolveType(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var direct = Type.GetType(name, false);
            if (direct != null) return direct;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type candidate;
                try { candidate = assembly.GetType(name, false); }
                catch { continue; }
                if (candidate != null) return candidate;
            }
            return null;
        }

        private static string FriendlyTypeName(Type type)
        {
            if (!type.IsGenericType) return type.FullName;
            return type.GetGenericTypeDefinition().FullName + "<" + string.Join(", ", type.GetGenericArguments().Select(FriendlyTypeName).ToArray()) + ">";
        }
    }
}
