using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable]
    public sealed class ChangeJournalEntry
    {
        public string operation;
        public string before;
        public string after;
    }

    [Serializable]
    public sealed class EditorChangeOutput
    {
        public bool dryRun;
        public bool changed;
        public string summary;
        public int? instanceId;
        public string path;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
        public bool rollbackSupported;
    }

    [Serializable]
    public sealed class CapturedImageResult
    {
        [JsonProperty("width")]
        public int width;

        [JsonProperty("height")]
        public int height;

        [JsonProperty("encoding")]
        public string encoding = "png";

        [JsonProperty("base64", NullValueHandling = NullValueHandling.Ignore)]
        public string base64;

        [JsonProperty("bytes")]
        public int bytes;

        [JsonProperty("source")]
        public string source;

        [JsonProperty("savedPath")]
        public string savedPath;
    }

    /// <summary>
    /// Shared asset-path validation, scene resolution, reflection lookup, and PNG capture output helpers for Editor commands.
    /// </summary>
    public static class EditorCommandHelpers
    {
        private static string AssetsRoot => Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        private static string ProjectRoot => Directory.GetParent(AssetsRoot).FullName;

        public static string ValidateAssetPath(string path, string extension = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path must be project-relative under Assets and may not contain '..'.");

            var normalized = path.Trim().Replace('\\', '/');
            if (normalized.Length > "Assets".Length)
                normalized = normalized.TrimEnd('/');

            if (Path.IsPathRooted(normalized) || normalized.Contains(":"))
                throw new ArgumentException("Path must be project-relative under Assets and may not contain '..'.");

            var segments = normalized.Split('/');
            if (segments.Any(segment => string.IsNullOrEmpty(segment) || segment == "." || segment == ".."))
                throw new ArgumentException("Path must be project-relative under Assets and may not contain '..'.");

            if (!(normalized == "Assets" || normalized.StartsWith("Assets/", StringComparison.Ordinal)))
                throw new ArgumentException("Path must be project-relative under Assets and may not contain '..'.");

            if (normalized.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Unity .meta files are not valid tool targets.");

            if (extension != null && !normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Path must end with " + extension + ".");

            EnsureContainedInAssets(normalized);
            return normalized;
        }

        public static string ValidateExistingAsset(string path, string extension = null)
        {
            var normalized = ValidateAssetPath(path, extension);
            if (AssetDatabase.LoadMainAssetAtPath(normalized) == null && !AssetDatabase.IsValidFolder(normalized))
                throw new ArgumentException("Asset was not found: " + normalized);
            return normalized;
        }

        public static string ValidateCreatableAssetPath(string path, string extension = null, bool requireExistingParent = true)
        {
            var normalized = ValidateAssetPath(path, extension);
            if (normalized == "Assets")
                throw new ArgumentException("Path must name an asset under Assets/.");

            var parent = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
            if (requireExistingParent && (string.IsNullOrWhiteSpace(parent) || !AssetDatabase.IsValidFolder(parent)))
                throw new ArgumentException("The asset's parent folder does not exist.");

            var fullPath = EnsureContainedInAssets(normalized);
            if (AssetDatabase.LoadMainAssetAtPath(normalized) != null || AssetDatabase.IsValidFolder(normalized) || File.Exists(fullPath))
                throw new ArgumentException("An asset already exists at the requested path.");

            return normalized;
        }

        public static void EnsureParentFolder(string assetPath)
        {
            var normalized = ValidateAssetPath(assetPath);
            var parent = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) || AssetDatabase.IsValidFolder(parent))
                return;

            var parts = parent.Split('/');
            var current = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        public static Scene RequireLoadedScene(string selector = null, bool requireExplicit = false)
        {
            if (requireExplicit && string.IsNullOrWhiteSpace(selector))
                throw new ArgumentException("scene is required for this operation.");

            var result = string.IsNullOrWhiteSpace(selector) ? SceneManager.GetActiveScene() : default;
            if (!string.IsNullOrWhiteSpace(selector))
            {
                for (var index = 0; index < SceneManager.sceneCount; index++)
                {
                    var candidate = SceneManager.GetSceneAt(index);
                    if (string.Equals(candidate.name, selector, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(candidate.path, selector, StringComparison.OrdinalIgnoreCase))
                    {
                        result = candidate;
                        break;
                    }
                }
            }

            if (!result.IsValid() || !result.isLoaded)
                throw new ArgumentException("The requested scene is not loaded.");

            return result;
        }

        public static Type TryResolveOptionalType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            var trimmed = typeName.Trim();
            var direct = Type.GetType(trimmed, false);
            if (direct != null)
                return direct;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var candidate = assembly.GetType(trimmed, false);
                    if (candidate != null)
                        return candidate;
                }
                catch
                {
                    // Dynamic or partially loaded assemblies may throw on type lookup; skip them cleanly.
                }
            }

            return null;
        }

        public static CapturedImageResult WriteCapturedPng(
            byte[] png,
            int width,
            int height,
            string source,
            string outputPath = null,
            string defaultSubdir = "unity-mcp-captures",
            bool includeInlineImage = false)
        {
            if (png == null || png.Length == 0)
                throw new ArgumentException("Captured PNG data is empty.", nameof(png));
            if (width < 1)
                throw new ArgumentOutOfRangeException(nameof(width), "Captured image width must be positive.");
            if (height < 1)
                throw new ArgumentOutOfRangeException(nameof(height), "Captured image height must be positive.");

            var hasOutputPath = !string.IsNullOrWhiteSpace(outputPath);
            var wantsInline = !hasOutputPath || includeInlineImage;
            var fullPath = ResolveCaptureFilePath(outputPath, defaultSubdir);

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(fullPath, png);

            var assetPath = ToProjectAssetPath(fullPath);
            if (assetPath != null)
                AssetDatabase.ImportAsset(assetPath);

            return new CapturedImageResult
            {
                width = width,
                height = height,
                encoding = "png",
                base64 = wantsInline ? Convert.ToBase64String(png) : null,
                bytes = png.Length,
                source = source,
                savedPath = fullPath
            };
        }

        private static string ResolveCaptureFilePath(string outputPath, string defaultSubdir)
        {
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                var trimmed = outputPath.Trim();
                return Path.GetFullPath(Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(ProjectRoot, trimmed));
            }

            var subdir = string.IsNullOrWhiteSpace(defaultSubdir) ? "unity-mcp-captures" : defaultSubdir.Trim().Replace('\\', '/').Trim('/');
            if (Path.IsPathRooted(subdir) || subdir.Split('/').Any(segment => string.IsNullOrEmpty(segment) || segment == "." || segment == ".."))
                throw new ArgumentException("defaultSubdir must be a safe relative subdirectory under Temp.", nameof(defaultSubdir));

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            var shortId = Guid.NewGuid().ToString("N").Substring(0, 8);
            return Path.GetFullPath(Path.Combine(ProjectRoot, "Temp", subdir, $"capture-{timestamp}-{shortId}.png"));
        }

        private static string EnsureContainedInAssets(string assetPath)
        {
            var fullPath = Path.GetFullPath(Path.Combine(ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
            var normalizedAssets = AssetsRoot;
            var isRootOrChild = string.Equals(fullPath, normalizedAssets, StringComparison.OrdinalIgnoreCase) ||
                                fullPath.StartsWith(normalizedAssets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (!isRootOrChild)
                throw new ArgumentException("Path escapes the project Assets directory.");
            return fullPath;
        }

        private static string ToProjectAssetPath(string fullPath)
        {
            var normalizedFile = Path.GetFullPath(fullPath).Replace('\\', '/');
            var normalizedAssets = AssetsRoot.Replace('\\', '/');
            if (!normalizedFile.StartsWith(normalizedAssets + "/", StringComparison.OrdinalIgnoreCase))
                return null;
            return "Assets" + normalizedFile.Substring(normalizedAssets.Length);
        }
    }
}
