using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable]
    public sealed class RgbaColorDto
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public static RgbaColorDto From(Color color)
        {
            return new RgbaColorDto
            {
                r = color.r,
                g = color.g,
                b = color.b,
                a = color.a
            };
        }
    }

    [Serializable]
    public sealed class RenderSettingsOutput
    {
        public bool fog;
        public RgbaColorDto fogColor;
        public float fogDensity;
        public string fogMode;
        public string ambientMode;
        public RgbaColorDto ambientSkyColor;
        public RgbaColorDto ambientEquatorColor;
        public RgbaColorDto ambientGroundColor;
        public float ambientIntensity;
        public float reflectionIntensity;
    }

    [Serializable]
    public sealed class RenderSettingsSetInput
    {
        public bool? fog;
        public Color? fogColor;
        public float? fogDensity;
        public string fogMode;
        public string ambientMode;
        public Color? ambientSkyColor;
        public Color? ambientEquatorColor;
        public Color? ambientGroundColor;
        public float? ambientIntensity;
        public float? reflectionIntensity;
        public bool dryRun;
    }

    [Serializable]
    public sealed class TextureGenerateInput
    {
        public string path;
        public int width = 256;
        public int height = 256;
        public Color color = Color.white;
        public bool mipmaps;
        public bool dryRun;
    }

    /// <summary>
    /// Active-scene RenderSettings inspection/mutation and solid PNG texture generation commands.
    /// </summary>
    public static class EditorRenderExtensionCommands
    {
        private const string Group = "render_ext";

        [CliGroupCommand(
            Group,
            "Read active-scene render and ambient lighting settings.",
            Name = "get_settings",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "rendering" })]
        internal static ExitCode GetSettingsCommand(CommandContext cmd)
        {
            return Execute(cmd, GetSettings);
        }

        [CliGroupCommand(
            Group,
            "Set active-scene render and ambient lighting settings.",
            Name = "set_settings",
            Effect = CommandEffect.Idempotent,
            Tags = new[] { "rendering" })]
        internal static ExitCode SetSettingsCommand(
            CommandContext cmd,
            [CliArg("fog", "Enable or disable scene fog.")] bool? fog = null,
            [CliArg("fog-color", "Fog color (#RRGGBB, named color, [r,g,b(,a)], r,g,b(,a), or {r,g,b,a}).")] string fogColor = null,
            [CliArg("fog-density", "Exponential fog density (>= 0).")] float? fogDensity = null,
            [CliArg("fog-mode", "FogMode enum value (Linear, Exponential, ExponentialSquared).")] string fogMode = null,
            [CliArg("ambient-mode", "AmbientMode enum value (Skybox, Trilight, Flat, Custom).")] string ambientMode = null,
            [CliArg("ambient-sky-color", "Ambient sky color.")] string ambientSkyColor = null,
            [CliArg("ambient-equator-color", "Ambient equator color.")] string ambientEquatorColor = null,
            [CliArg("ambient-ground-color", "Ambient ground color.")] string ambientGroundColor = null,
            [CliArg("ambient-intensity", "Ambient lighting intensity (>= 0).")] float? ambientIntensity = null,
            [CliArg("reflection-intensity", "Global reflection intensity (>= 0).")] float? reflectionIntensity = null,
            [CliArg("dry-run", "Validate and preview changes without modifying RenderSettings.")] bool dryRun = false)
        {
            return Execute(cmd, () => SetSettings(new RenderSettingsSetInput
            {
                fog = fog,
                fogColor = RuntimeCommandHelpers.ParseColorNullable(fogColor, "fog-color"),
                fogDensity = fogDensity,
                fogMode = fogMode,
                ambientMode = ambientMode,
                ambientSkyColor = RuntimeCommandHelpers.ParseColorNullable(ambientSkyColor, "ambient-sky-color"),
                ambientEquatorColor = RuntimeCommandHelpers.ParseColorNullable(ambientEquatorColor, "ambient-equator-color"),
                ambientGroundColor = RuntimeCommandHelpers.ParseColorNullable(ambientGroundColor, "ambient-ground-color"),
                ambientIntensity = ambientIntensity,
                reflectionIntensity = reflectionIntensity,
                dryRun = dryRun
            }));
        }

        [CliGroupCommand(
            Group,
            "Generate a solid PNG texture asset under Assets/.",
            Name = "generate_texture",
            Effect = CommandEffect.Additive,
            Tags = new[] { "rendering", "texture" })]
        internal static ExitCode GenerateTextureCommand(
            CommandContext cmd,
            [CliArg("path", "Project-relative PNG output path (Assets/.../*.png).", Required = true)] string path = null,
            [CliArg("width", "Texture width in pixels (1-4096, default 256).")] int width = 256,
            [CliArg("height", "Texture height in pixels (1-4096, default 256).")] int height = 256,
            [CliArg("color", "Fill color (#RRGGBB, named color, [r,g,b(,a)], r,g,b(,a), or {r,g,b,a}; default white).")] string color = null,
            [CliArg("mipmaps", "Generate mipmaps on the temporary texture before encoding (default false).")] bool mipmaps = false,
            [CliArg("dry-run", "Validate and preview texture creation without writing files.")] bool dryRun = false)
        {
            return Execute(cmd, () => GenerateTexture(new TextureGenerateInput
            {
                path = path,
                width = width,
                height = height,
                color = RuntimeCommandHelpers.ParseColorNullable(color, "color") ?? Color.white,
                mipmaps = mipmaps,
                dryRun = dryRun
            }));
        }

        internal static RenderSettingsOutput GetSettings()
        {
            return new RenderSettingsOutput
            {
                fog = RenderSettings.fog,
                fogColor = RgbaColorDto.From(RenderSettings.fogColor),
                fogDensity = RenderSettings.fogDensity,
                fogMode = RenderSettings.fogMode.ToString(),
                ambientMode = RenderSettings.ambientMode.ToString(),
                ambientSkyColor = RgbaColorDto.From(RenderSettings.ambientSkyColor),
                ambientEquatorColor = RgbaColorDto.From(RenderSettings.ambientEquatorColor),
                ambientGroundColor = RgbaColorDto.From(RenderSettings.ambientGroundColor),
                ambientIntensity = RenderSettings.ambientIntensity,
                reflectionIntensity = RenderSettings.reflectionIntensity
            };
        }

        internal static EditorChangeOutput SetSettings(RenderSettingsSetInput input)
        {
            if (input == null)
                throw new ArgumentException("input is required.");

            if (!HasAnyRenderChange(input))
                throw new ArgumentException("Supply at least one render setting to update.");

            if (input.fogDensity.HasValue && input.fogDensity.Value < 0f)
                throw new ArgumentOutOfRangeException(nameof(input.fogDensity));
            if (input.ambientIntensity.HasValue && input.ambientIntensity.Value < 0f)
                throw new ArgumentOutOfRangeException(nameof(input.ambientIntensity));
            if (input.reflectionIntensity.HasValue && input.reflectionIntensity.Value < 0f)
                throw new ArgumentOutOfRangeException(nameof(input.reflectionIntensity));

            FogMode? parsedFogMode = null;
            if (!string.IsNullOrWhiteSpace(input.fogMode))
            {
                if (!Enum.TryParse<FogMode>(input.fogMode, true, out var mode) || !Enum.IsDefined(typeof(FogMode), mode))
                    throw new ArgumentException("fogMode is invalid.");
                parsedFogMode = mode;
            }

            UnityEngine.Rendering.AmbientMode? parsedAmbientMode = null;
            if (!string.IsNullOrWhiteSpace(input.ambientMode))
            {
                if (!Enum.TryParse<UnityEngine.Rendering.AmbientMode>(input.ambientMode, true, out var mode) ||
                    !Enum.IsDefined(typeof(UnityEngine.Rendering.AmbientMode), mode))
                {
                    throw new ArgumentException("ambientMode is invalid.");
                }
                parsedAmbientMode = mode;
            }

            if (!input.dryRun)
            {
                if (input.fog.HasValue) RenderSettings.fog = input.fog.Value;
                if (input.fogColor.HasValue) RenderSettings.fogColor = input.fogColor.Value;
                if (input.fogDensity.HasValue) RenderSettings.fogDensity = input.fogDensity.Value;
                if (parsedFogMode.HasValue) RenderSettings.fogMode = parsedFogMode.Value;
                if (parsedAmbientMode.HasValue) RenderSettings.ambientMode = parsedAmbientMode.Value;
                if (input.ambientSkyColor.HasValue) RenderSettings.ambientSkyColor = input.ambientSkyColor.Value;
                if (input.ambientEquatorColor.HasValue) RenderSettings.ambientEquatorColor = input.ambientEquatorColor.Value;
                if (input.ambientGroundColor.HasValue) RenderSettings.ambientGroundColor = input.ambientGroundColor.Value;
                if (input.ambientIntensity.HasValue) RenderSettings.ambientIntensity = input.ambientIntensity.Value;
                if (input.reflectionIntensity.HasValue) RenderSettings.reflectionIntensity = input.reflectionIntensity.Value;
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            return new EditorChangeOutput
            {
                dryRun = input.dryRun,
                changed = !input.dryRun,
                summary = "Updated active-scene render settings.",
                rollbackSupported = false
            };
        }

        internal static EditorChangeOutput GenerateTexture(TextureGenerateInput input)
        {
            if (input == null)
                throw new ArgumentException("input is required.");

            var assetPath = EditorCommandHelpers.ValidateCreatableAssetPath(input.path, ".png", requireExistingParent: true);
            if (input.width < 1 || input.width > 4096 || input.height < 1 || input.height > 4096)
                throw new ArgumentOutOfRangeException(nameof(input.width), "Texture dimensions must be between 1 and 4096.");

            if (!input.dryRun)
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                var fullPath = string.IsNullOrEmpty(projectRoot)
                    ? Path.GetFullPath(assetPath)
                    : Path.GetFullPath(Path.Combine(projectRoot, assetPath));

                var texture = new Texture2D(input.width, input.height, TextureFormat.RGBA32, input.mipmaps, false);
                try
                {
                    var pixels = Enumerable.Repeat(input.color, input.width * input.height).ToArray();
                    texture.SetPixels(pixels);
                    texture.Apply(input.mipmaps, false);
                    File.WriteAllBytes(fullPath, texture.EncodeToPNG());
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }

            return new EditorChangeOutput
            {
                dryRun = input.dryRun,
                changed = !input.dryRun,
                summary = $"Generate texture '{assetPath}'.",
                path = assetPath,
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry>
                {
                    new ChangeJournalEntry { operation = "create-texture", before = null, after = assetPath }
                }
            };
        }

        private static bool HasAnyRenderChange(RenderSettingsSetInput input)
        {
            return input.fog.HasValue ||
                   input.fogColor.HasValue ||
                   input.fogDensity.HasValue ||
                   !string.IsNullOrWhiteSpace(input.fogMode) ||
                   !string.IsNullOrWhiteSpace(input.ambientMode) ||
                   input.ambientSkyColor.HasValue ||
                   input.ambientEquatorColor.HasValue ||
                   input.ambientGroundColor.HasValue ||
                   input.ambientIntensity.HasValue ||
                   input.reflectionIntensity.HasValue;
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
