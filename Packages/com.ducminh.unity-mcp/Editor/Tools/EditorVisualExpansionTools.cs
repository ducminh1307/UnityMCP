using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable] public sealed class MaterialCreateInput { public string path; public string shader = "Standard"; public string name; public bool apply; }
    [Serializable] public sealed class MaterialAssignInput { public int? instanceId; public int rendererIndex; public int materialIndex; public string materialPath; public bool apply; }
    [Serializable] public sealed class ShaderListInput { public string search; public int limit = 200; }
    [Serializable] public sealed class ShaderSummary { public string path; public string name; public bool isSupported; }
    [Serializable] public sealed class ShaderListOutput { public List<ShaderSummary> shaders = new List<ShaderSummary>(); public bool truncated; }
    [Serializable] public sealed class ShaderInfoInput { public string path; public string name; }
    [Serializable] public sealed class ShaderInfoOutput { public string path; public string name; public bool isSupported; public List<MaterialPropertyInfo> properties = new List<MaterialPropertyInfo>(); }
    [Serializable] public sealed class TextureGenerateInput { public string path; public int width = 256; public int height = 256; public Color color = Color.white; public bool mipmaps; public bool apply; }
    [Serializable] public sealed class TextureImportSettingsInput { public string path; public bool? isReadable; public bool? mipmapEnabled; public bool? alphaIsTransparency; public string wrapMode; public string filterMode; public int? maxTextureSize; public bool apply; }
    [Serializable] public sealed class RenderPipelineInfoOutput { public string currentPipeline; public string defaultPipeline; public string qualityPipeline; public string colorSpace; public int qualityLevel; }
    [Serializable] public sealed class RenderSettingsOutput { public bool fog; public Color fogColor; public float fogDensity; public string fogMode; public string ambientMode; public Color ambientSkyColor; public Color ambientEquatorColor; public Color ambientGroundColor; public float ambientIntensity; public float reflectionIntensity; }
    [Serializable] public sealed class RenderSettingsInput { public bool? fog; public Color? fogColor; public float? fogDensity; public string fogMode; public string ambientMode; public Color? ambientSkyColor; public Color? ambientEquatorColor; public Color? ambientGroundColor; public float? ambientIntensity; public float? reflectionIntensity; public bool apply; }
    [Serializable] public sealed class ScreenshotCameraInput { public int? instanceId; public int width = 1280; public int height = 720; public bool includeAlpha; }
    [Serializable] public sealed class ScreenshotMultiViewInput { public List<int> cameraInstanceIds = new List<int>(); public int width = 960; public int height = 540; public bool includeAlpha; }
    [Serializable] public sealed class ScreenshotInfo { public int cameraInstanceId; public string cameraName; public int width; public int height; }
    [Serializable] public sealed class ScreenshotMultiViewOutput { public List<ScreenshotInfo> screenshots = new List<ScreenshotInfo>(); }

    /// <summary>Editor-only assets, rendering and camera screenshot tools with project-contained writes.</summary>
    public static class EditorVisualExpansionTools
    {
        [UnityMcpTool("material-create", Description = "Create a Material asset with a named Shader; dry-run unless apply is true.", Category = "material", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput MaterialCreate(MaterialCreateInput input, UnityMcpContext context)
        {
            ValidateAssetPath(input.path, ".mat");
            if (AssetDatabase.LoadMainAssetAtPath(input.path) != null) throw new InvalidOperationException("An asset already exists at " + input.path + ".");
            var shader = Shader.Find(input.shader ?? string.Empty);
            if (shader == null) throw new ArgumentException("Shader was not found: " + input.shader);
            if (!context.DryRun)
            {
                var material = new Material(shader) { name = string.IsNullOrWhiteSpace(input.name) ? Path.GetFileNameWithoutExtension(input.path) : input.name.Trim() };
                AssetDatabase.CreateAsset(material, input.path);
                AssetDatabase.SaveAssets();
            }
            return AssetChange(context, "Create material '" + input.path + "'.", "create-material", null, input.path);
        }

        [UnityMcpTool("material-assign", Description = "Assign a Material asset to a Renderer slot; dry-run unless apply is true.", Category = "material", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput MaterialAssign(MaterialAssignInput input, UnityMcpContext context)
        {
            if (!input.instanceId.HasValue) throw new ArgumentException("instanceId is required.");
            ValidateExistingAsset(input.materialPath, ".mat");
            var target = EditorUtility.EntityIdToObject((EntityId)input.instanceId.Value) as GameObject;
            if (target == null || !target.scene.IsValid()) throw new ArgumentException("instanceId must identify a loaded scene GameObject.");
            var renderers = target.GetComponents<Renderer>();
            if (input.rendererIndex < 0 || input.rendererIndex >= renderers.Length) throw new ArgumentOutOfRangeException(nameof(input.rendererIndex));
            var renderer = renderers[input.rendererIndex];
            var materials = renderer.sharedMaterials;
            if (input.materialIndex < 0 || input.materialIndex >= materials.Length) throw new ArgumentOutOfRangeException(nameof(input.materialIndex));
            var material = AssetDatabase.LoadAssetAtPath<Material>(input.materialPath);
            if (material == null) throw new ArgumentException("materialPath is not a Material asset.");
            if (!context.DryRun)
            {
                Undo.RecordObject(renderer, "UnityMCP Assign Material");
                materials[input.materialIndex] = material;
                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(renderer);
            }
            return Change(context, "Assign material '" + input.materialPath + "' to renderer slot " + input.materialIndex + ".", target.GetInstanceID());
        }

        [UnityMcpTool("shader-list", Description = "List project Shader assets with a bounded search.", Category = "shader", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static ShaderListOutput ShaderList(ShaderListInput input)
        {
            var limit = Mathf.Clamp(input.limit, 1, 1000);
            var guids = AssetDatabase.FindAssets("t:Shader");
            var output = new ShaderListOutput();
            foreach (var path in guids.Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                var name = shader == null ? Path.GetFileNameWithoutExtension(path) : shader.name;
                if (!string.IsNullOrWhiteSpace(input.search) && name.IndexOf(input.search, StringComparison.OrdinalIgnoreCase) < 0 && path.IndexOf(input.search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (output.shaders.Count >= limit) { output.truncated = true; break; }
                output.shaders.Add(new ShaderSummary { path = path, name = name, isSupported = shader != null && shader.isSupported });
            }
            return output;
        }

        [UnityMcpTool("shader-info", Description = "Read Shader asset metadata and declared properties.", Category = "shader", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static ShaderInfoOutput ShaderInfo(ShaderInfoInput input)
        {
            var shader = !string.IsNullOrWhiteSpace(input.path) ? AssetDatabase.LoadAssetAtPath<Shader>(input.path) : Shader.Find(input.name ?? string.Empty);
            if (shader == null) throw new ArgumentException("Shader was not found. Supply a project path or a loaded Shader name.");
            var output = new ShaderInfoOutput { path = AssetDatabase.GetAssetPath(shader), name = shader.name, isSupported = shader.isSupported };
            for (var index = 0; index < shader.GetPropertyCount(); index++)
                output.properties.Add(new MaterialPropertyInfo { name = shader.GetPropertyName(index), description = shader.GetPropertyDescription(index), type = shader.GetPropertyType(index).ToString() });
            return output;
        }

        [UnityMcpTool("texture-generate", Description = "Generate a solid PNG texture asset; dry-run unless apply is true.", Category = "texture", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput TextureGenerate(TextureGenerateInput input, UnityMcpContext context)
        {
            ValidateAssetPath(input.path, ".png");
            if (input.width < 1 || input.width > 4096 || input.height < 1 || input.height > 4096) throw new ArgumentOutOfRangeException("width/height", "Texture dimensions must be between 1 and 4096.");
            if (AssetDatabase.LoadMainAssetAtPath(input.path) != null || File.Exists(Path.GetFullPath(input.path))) throw new InvalidOperationException("An asset already exists at " + input.path + ".");
            if (!context.DryRun)
            {
                var texture = new Texture2D(input.width, input.height, TextureFormat.RGBA32, input.mipmaps, false);
                try
                {
                    var pixels = Enumerable.Repeat(input.color, input.width * input.height).ToArray();
                    texture.SetPixels(pixels);
                    texture.Apply(input.mipmaps, false);
                    File.WriteAllBytes(Path.GetFullPath(input.path), texture.EncodeToPNG());
                    AssetDatabase.ImportAsset(input.path, ImportAssetOptions.ForceSynchronousImport);
                }
                finally { UnityEngine.Object.DestroyImmediate(texture); }
            }
            return AssetChange(context, "Generate texture '" + input.path + "'.", "create-texture", null, input.path);
        }

        [UnityMcpTool("texture-import-settings-set", Description = "Set TextureImporter settings; dry-run unless apply is true.", Category = "texture", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput TextureImportSettingsSet(TextureImportSettingsInput input, UnityMcpContext context)
        {
            ValidateExistingAsset(input.path);
            var importer = AssetImporter.GetAtPath(input.path) as TextureImporter;
            if (importer == null) throw new ArgumentException("path is not imported by TextureImporter.");
            if (input.maxTextureSize.HasValue && (input.maxTextureSize.Value < 32 || input.maxTextureSize.Value > 16384)) throw new ArgumentOutOfRangeException(nameof(input.maxTextureSize));
            if (!string.IsNullOrWhiteSpace(input.wrapMode) && !Enum.TryParse(input.wrapMode, true, out TextureWrapMode _)) throw new ArgumentException("wrapMode is invalid.");
            if (!string.IsNullOrWhiteSpace(input.filterMode) && !Enum.TryParse(input.filterMode, true, out FilterMode _)) throw new ArgumentException("filterMode is invalid.");
            if (!context.DryRun)
            {
                if (input.isReadable.HasValue) importer.isReadable = input.isReadable.Value;
                if (input.mipmapEnabled.HasValue) importer.mipmapEnabled = input.mipmapEnabled.Value;
                if (input.alphaIsTransparency.HasValue) importer.alphaIsTransparency = input.alphaIsTransparency.Value;
                if (!string.IsNullOrWhiteSpace(input.wrapMode)) importer.wrapMode = (TextureWrapMode)Enum.Parse(typeof(TextureWrapMode), input.wrapMode, true);
                if (!string.IsNullOrWhiteSpace(input.filterMode)) importer.filterMode = (FilterMode)Enum.Parse(typeof(FilterMode), input.filterMode, true);
                if (input.maxTextureSize.HasValue) importer.maxTextureSize = input.maxTextureSize.Value;
                importer.SaveAndReimport();
            }
            return AssetChange(context, "Update texture import settings for '" + input.path + "'.", "set-import-settings", input.path, input.path);
        }

        [UnityMcpTool("render-pipeline-info", Description = "Read active render pipeline and color-space information.", Category = "rendering", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static RenderPipelineInfoOutput RenderPipelineInfo(EmptyInput input) => new RenderPipelineInfoOutput
        {
            currentPipeline = GraphicsSettings.currentRenderPipeline == null ? "Built-in" : GraphicsSettings.currentRenderPipeline.GetType().FullName,
            defaultPipeline = GraphicsSettings.defaultRenderPipeline == null ? "Built-in" : GraphicsSettings.defaultRenderPipeline.GetType().FullName,
            qualityPipeline = QualitySettings.renderPipeline == null ? "Built-in" : QualitySettings.renderPipeline.GetType().FullName,
            colorSpace = PlayerSettings.colorSpace.ToString(),
            qualityLevel = QualitySettings.GetQualityLevel()
        };

        [UnityMcpTool("render-settings-get", Description = "Read scene render and ambient settings.", Category = "rendering", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static RenderSettingsOutput RenderSettingsGet(EmptyInput input) => new RenderSettingsOutput
        {
            fog = RenderSettings.fog,
            fogColor = RenderSettings.fogColor,
            fogDensity = RenderSettings.fogDensity,
            fogMode = RenderSettings.fogMode.ToString(),
            ambientMode = RenderSettings.ambientMode.ToString(),
            ambientSkyColor = RenderSettings.ambientSkyColor,
            ambientEquatorColor = RenderSettings.ambientEquatorColor,
            ambientGroundColor = RenderSettings.ambientGroundColor,
            ambientIntensity = RenderSettings.ambientIntensity,
            reflectionIntensity = RenderSettings.reflectionIntensity
        };

        [UnityMcpTool("render-settings-set", Description = "Set scene render and ambient settings; dry-run unless apply is true.", Category = "rendering", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput RenderSettingsSet(RenderSettingsInput input, UnityMcpContext context)
        {
            if (input.fogDensity.HasValue && input.fogDensity.Value < 0) throw new ArgumentOutOfRangeException(nameof(input.fogDensity));
            if (input.ambientIntensity.HasValue && input.ambientIntensity.Value < 0) throw new ArgumentOutOfRangeException(nameof(input.ambientIntensity));
            if (input.reflectionIntensity.HasValue && input.reflectionIntensity.Value < 0) throw new ArgumentOutOfRangeException(nameof(input.reflectionIntensity));
            if (!string.IsNullOrWhiteSpace(input.fogMode) && !Enum.TryParse(input.fogMode, true, out FogMode _)) throw new ArgumentException("fogMode is invalid.");
            if (!string.IsNullOrWhiteSpace(input.ambientMode) && !Enum.TryParse(input.ambientMode, true, out AmbientMode _)) throw new ArgumentException("ambientMode is invalid.");
            if (!context.DryRun)
            {
                if (input.fog.HasValue) RenderSettings.fog = input.fog.Value;
                if (input.fogColor.HasValue) RenderSettings.fogColor = input.fogColor.Value;
                if (input.fogDensity.HasValue) RenderSettings.fogDensity = input.fogDensity.Value;
                if (!string.IsNullOrWhiteSpace(input.fogMode)) RenderSettings.fogMode = (FogMode)Enum.Parse(typeof(FogMode), input.fogMode, true);
                if (!string.IsNullOrWhiteSpace(input.ambientMode)) RenderSettings.ambientMode = (AmbientMode)Enum.Parse(typeof(AmbientMode), input.ambientMode, true);
                if (input.ambientSkyColor.HasValue) RenderSettings.ambientSkyColor = input.ambientSkyColor.Value;
                if (input.ambientEquatorColor.HasValue) RenderSettings.ambientEquatorColor = input.ambientEquatorColor.Value;
                if (input.ambientGroundColor.HasValue) RenderSettings.ambientGroundColor = input.ambientGroundColor.Value;
                if (input.ambientIntensity.HasValue) RenderSettings.ambientIntensity = input.ambientIntensity.Value;
                if (input.reflectionIntensity.HasValue) RenderSettings.reflectionIntensity = input.reflectionIntensity.Value;
                EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            }
            return Change(context, "Updated active-scene render settings.");
        }

        [UnityMcpTool("screenshot-camera", Description = "Capture a loaded Camera as a PNG image using the Camera component or its GameObject instance ID.", Category = "visual", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static UnityMcpResult ScreenshotCamera(ScreenshotCameraInput input)
        {
            if (!input.instanceId.HasValue) throw new ArgumentException("instanceId is required.");
            var camera = ResolveLoadedCamera(input.instanceId.Value, "instanceId must identify a loaded Camera component or a loaded scene GameObject with a Camera component.");
            return CaptureCamera(camera, input.width, input.height, input.includeAlpha, out _);
        }

        [UnityMcpTool("screenshot-scene-view", Description = "Capture the last active Scene View camera as a PNG image.", Category = "visual", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static UnityMcpResult ScreenshotSceneView(ScreenshotCameraInput input)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null) throw new InvalidOperationException("Open a Scene View before capturing it.");
            return CaptureCamera(sceneView.camera, input.width, input.height, input.includeAlpha, out _);
        }

        [UnityMcpTool("screenshot-multiview", Description = "Capture several loaded Cameras as PNG images.", Category = "visual", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static UnityMcpResult ScreenshotMultiview(ScreenshotMultiViewInput input)
        {
            if (input.cameraInstanceIds == null || input.cameraInstanceIds.Count == 0 || input.cameraInstanceIds.Count > 8) throw new ArgumentException("cameraInstanceIds must contain between 1 and 8 cameras.");
            var output = new ScreenshotMultiViewOutput();
            var content = new List<UnityMcpContent>();
            foreach (var id in input.cameraInstanceIds.Distinct())
            {
                var camera = ResolveLoadedCamera(id, "One or more cameraInstanceIds do not identify loaded Camera components or loaded scene GameObjects with Camera components.");
                var result = CaptureCamera(camera, input.width, input.height, input.includeAlpha, out var info);
                output.screenshots.Add(info);
                content.AddRange(result.content);
            }
            return new UnityMcpResult { content = content, structuredContent = output };
        }

        internal static Camera ResolveLoadedCamera(int instanceId, string errorMessage)
        {
            var target = EditorUtility.EntityIdToObject((EntityId)instanceId);
            var camera = target as Camera ?? (target as GameObject)?.GetComponent<Camera>();
            if (camera == null || !camera.gameObject.scene.IsValid()) throw new ArgumentException(errorMessage);
            return camera;
        }

        private static UnityMcpResult CaptureCamera(Camera camera, int width, int height, bool includeAlpha, out ScreenshotInfo info)
        {
            width = Mathf.Clamp(width, 16, 2048);
            height = Mathf.Clamp(height, 16, 2048);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var renderTexture = RenderTexture.GetTemporary(width, height, 24, includeAlpha ? RenderTextureFormat.ARGB32 : RenderTextureFormat.RGB565);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply(false, false);
                var png = texture.EncodeToPNG();
                info = new ScreenshotInfo { cameraInstanceId = camera.GetInstanceID(), cameraName = camera.name, width = width, height = height };
                return new UnityMcpResult
                {
                    content = new List<UnityMcpContent> { new UnityMcpContent { type = "image", data = Convert.ToBase64String(png), mimeType = "image/png" } },
                    structuredContent = info
                };
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
                UnityEngine.Object.DestroyImmediate(texture);
            }
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
    }
}
