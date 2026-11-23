using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable]
    public sealed class AssetSummary
    {
        public string guid;
        public string path;
        public string name;
        public string type;
    }

    [Serializable]
    public sealed class AssetDependenciesInput
    {
        public string path;
        public bool recursive = true;
    }

    [Serializable]
    public sealed class AssetDependenciesOutput
    {
        public string path;
        public bool recursive;
        public List<string> dependencies = new List<string>();
    }

    [Serializable]
    public sealed class AssetReferencesInput
    {
        public string path;
        public int limit = 100;
    }

    [Serializable]
    public sealed class AssetReferencesOutput
    {
        public string path;
        public List<AssetSummary> references = new List<AssetSummary>();
        public int scannedAssets;
        public bool truncated;
    }

    /// <summary>
    /// Asset dependency and reverse-reference analysis commands.
    /// </summary>
    public static class EditorAssetAnalysisCommands
    {
        private const string Group = "asset_ext";
        private const int MaxAssetReferenceScan = 20000;

        [CliGroupCommand(
            Group,
            "List direct or recursive asset dependencies.",
            Name = "get_dependencies",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "asset", "dependencies" })]
        internal static ExitCode GetDependenciesCommand(
            CommandContext cmd,
            [CliArg("path", "Project-relative path to an existing asset under Assets/.", Required = true)] string path = null,
            [CliArg("recursive", "Include transitive dependencies (default true).")] bool recursive = true)
        {
            return Execute(cmd, () => GetDependencies(new AssetDependenciesInput
            {
                path = path,
                recursive = recursive
            }));
        }

        [CliGroupCommand(
            Group,
            "Find project assets with a direct AssetDatabase dependency on a target asset.",
            Name = "find_references",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "asset", "references" })]
        internal static ExitCode FindReferencesCommand(
            CommandContext cmd,
            [CliArg("path", "Project-relative path to an existing asset under Assets/.", Required = true)] string path = null,
            [CliArg("limit", "Maximum referencing assets to return (1-1000, default 100).")] int limit = 100)
        {
            return Execute(cmd, () => FindReferences(new AssetReferencesInput
            {
                path = path,
                limit = limit
            }));
        }

        internal static AssetDependenciesOutput GetDependencies(AssetDependenciesInput input)
        {
            if (input == null)
                throw new ArgumentException("input is required.");

            var normalized = EditorCommandHelpers.ValidateExistingAsset(input.path);
            return new AssetDependenciesOutput
            {
                path = normalized,
                recursive = input.recursive,
                dependencies = AssetDatabase.GetDependencies(normalized, input.recursive)
                    .Where(p => !string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        internal static AssetReferencesOutput FindReferences(AssetReferencesInput input)
        {
            if (input == null)
                throw new ArgumentException("input is required.");

            var target = EditorCommandHelpers.ValidateExistingAsset(input.path);
            if (AssetDatabase.IsValidFolder(target))
                throw new ArgumentException("A file asset, not a folder, is required.");

            var limit = Mathf.Clamp(input.limit, 1, 1000);
            var output = new AssetReferencesOutput { path = target };
            var guids = AssetDatabase.FindAssets(string.Empty, new[] { "Assets" });

            foreach (var guid in guids.OrderBy(value => value, StringComparer.Ordinal))
            {
                if (output.scannedAssets >= MaxAssetReferenceScan)
                {
                    output.truncated = true;
                    break;
                }

                output.scannedAssets++;
                var candidate = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(candidate) || string.Equals(candidate, target, StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] dependencies;
                try
                {
                    dependencies = AssetDatabase.GetDependencies(candidate, false);
                }
                catch
                {
                    continue;
                }

                if (!dependencies.Any(value => string.Equals(value, target, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (output.references.Count >= limit)
                {
                    output.truncated = true;
                    break;
                }

                var asset = AssetDatabase.LoadMainAssetAtPath(candidate);
                output.references.Add(new AssetSummary
                {
                    guid = guid,
                    path = candidate,
                    name = asset == null ? Path.GetFileNameWithoutExtension(candidate) : asset.name,
                    type = asset == null ? null : asset.GetType().FullName
                });
            }

            return output;
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
