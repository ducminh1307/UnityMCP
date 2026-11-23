using System;
using System.Collections.Generic;
using System.IO;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using UnityEditor;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable]
    public sealed class ParticlePrefabSaveInput
    {
        public int? instanceId;
        public string path;
        public int componentIndex;
        public string assetPath;
        public bool overwrite;
        public bool confirm;
        public bool dryRun;
    }

    /// <summary>
    /// Editor-only ParticleSystem prefab authoring commands.
    /// </summary>
    public static class EditorParticleCommands
    {
        private const string Group = "particle";

        [CliGroupCommand(
            Group,
            "Save a scene ParticleSystem GameObject as a prefab asset.",
            Name = "save_prefab",
            Effect = CommandEffect.Destructive,
            Tags = new[] { "vfx", "particle" })]
        internal static ExitCode SavePrefabCommand(
            CommandContext cmd,
            [CliArg("instance-id", "Instance ID of the scene ParticleSystem GameObject.")] int? instanceId = null,
            [CliArg("path", "Hierarchy path of the scene ParticleSystem GameObject.")] string path = null,
            [CliArg("component-index", "Zero-based ParticleSystem component index on the GameObject (default 0).")] int componentIndex = 0,
            [CliArg("asset-path", "Destination prefab asset path (Assets/.../*.prefab).", Required = true)] string assetPath = null,
            [CliArg("overwrite", "Allow replacing an existing prefab asset (also requires --confirm true).")] bool overwrite = false,
            [CliArg("confirm", "Explicitly confirm overwriting an existing prefab asset.")] bool confirm = false,
            [CliArg("dry-run", "Validate and preview without creating or overwriting the prefab asset.")] bool dryRun = false)
        {
            try
            {
                var result = SavePrefab(new ParticlePrefabSaveInput
                {
                    instanceId = instanceId,
                    path = path,
                    componentIndex = componentIndex,
                    assetPath = assetPath,
                    overwrite = overwrite,
                    confirm = confirm,
                    dryRun = dryRun
                });
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

        internal static EditorChangeOutput SavePrefab(ParticlePrefabSaveInput input)
        {
            if (input == null)
                throw new ArgumentException("input is required.");

            var assetPath = EditorCommandHelpers.ValidateAssetPath(input.assetPath, ".prefab");
            if (string.Equals(assetPath, "Assets", StringComparison.Ordinal))
                throw new ArgumentException("assetPath must name a prefab file under Assets/.");

            var parentFolder = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(parentFolder) || !AssetDatabase.IsValidFolder(parentFolder))
                throw new ArgumentException("The destination prefab's parent folder does not exist.");

            var target = RuntimeCommandHelpers.RequireGameObject(input.instanceId, input.path);
            var systems = target.GetComponents<ParticleSystem>();
            if (input.componentIndex < 0 || input.componentIndex >= systems.Length)
                throw new ArgumentOutOfRangeException(nameof(input.componentIndex), "Target GameObject does not have a ParticleSystem at componentIndex.");

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            var fullAssetPath = string.IsNullOrEmpty(projectRoot)
                ? Path.GetFullPath(assetPath)
                : Path.GetFullPath(Path.Combine(projectRoot, assetPath));

            var existingMainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            var exists = existingMainAsset != null || AssetDatabase.IsValidFolder(assetPath) || File.Exists(fullAssetPath);
            if (exists)
            {
                if (AssetDatabase.IsValidFolder(assetPath) || (existingMainAsset != null && !(existingMainAsset is GameObject)))
                    throw new InvalidOperationException($"A non-prefab asset or folder already exists at '{assetPath}'.");

                if (!input.overwrite)
                    throw new InvalidOperationException($"A prefab already exists at '{assetPath}'. Set overwrite=true and confirm=true to replace it.");

                if (!input.dryRun && !input.confirm)
                    throw new InvalidOperationException($"Overwriting existing prefab '{assetPath}' is destructive; pass confirm=true to proceed (or dryRun=true to preview).");
            }

            if (input.dryRun)
            {
                return new EditorChangeOutput
                {
                    dryRun = true,
                    changed = false,
                    summary = $"Save ParticleSystem '{target.name}' as prefab '{assetPath}'.",
                    instanceId = UnityMcpObjectId.Get(systems[input.componentIndex]),
                    path = assetPath,
                    rollbackSupported = false,
                    journal = new List<ChangeJournalEntry>
                    {
                        new ChangeJournalEntry
                        {
                            operation = exists ? "overwrite-particle-prefab" : "create-particle-prefab",
                            before = exists ? assetPath : null,
                            after = assetPath
                        }
                    }
                };
            }

            GameObject prefab;
            using (new AuthoringUndoScope("Save ParticleSystem Prefab"))
            {
                prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(target, assetPath, InteractionMode.AutomatedAction);
            }

            if (prefab == null)
                throw new InvalidOperationException("Unity could not save the ParticleSystem prefab.");

            AssetDatabase.SaveAssets();

            return new EditorChangeOutput
            {
                dryRun = false,
                changed = true,
                summary = $"Saved ParticleSystem prefab '{assetPath}'.",
                instanceId = UnityMcpObjectId.Get(prefab),
                path = assetPath,
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry>
                {
                    new ChangeJournalEntry
                    {
                        operation = exists ? "overwrite-particle-prefab" : "create-particle-prefab",
                        before = exists ? assetPath : null,
                        after = assetPath
                    }
                }
            };
        }
    }
}
