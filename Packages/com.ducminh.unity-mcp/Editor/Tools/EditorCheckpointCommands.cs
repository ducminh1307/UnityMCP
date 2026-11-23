using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable]
    public sealed class CheckpointCreateInput
    {
        public string scenePath;
        public string checkpointId;
        public bool dryRun;
    }

    [Serializable]
    public sealed class CheckpointListInput
    {
        public int limit = 100;
    }

    [Serializable]
    public sealed class CheckpointDiffInput
    {
        public string checkpointId;
        public string scenePath;
        public int maxLines = 50;
    }

    [Serializable]
    public sealed class CheckpointRestoreInput
    {
        public string checkpointId;
        public bool confirm;
        public bool dryRun;
    }

    [Serializable]
    public sealed class CheckpointInfo
    {
        public string checkpointId;
        public string scenePath;
        public string createdUtc;
        public string sourceSha256;
        public long sourceBytes;
        public bool snapshotExists;
    }

    [Serializable]
    public sealed class CheckpointCreateOutput
    {
        public bool dryRun;
        public bool created;
        public CheckpointInfo checkpoint;
        public string summary;
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    [Serializable]
    public sealed class CheckpointListOutput
    {
        public List<CheckpointInfo> checkpoints = new List<CheckpointInfo>();
        public bool truncated;
    }

    [Serializable]
    public sealed class CheckpointLineDiff
    {
        public int line;
        public string checkpoint;
        public string current;
    }

    [Serializable]
    public sealed class CheckpointDiffOutput
    {
        public string checkpointId;
        public string scenePath;
        public bool sameContent;
        public string checkpointSha256;
        public string currentSha256;
        public long checkpointBytes;
        public long currentBytes;
        public string comparison = "bounded-line-by-line";
        public List<CheckpointLineDiff> lines = new List<CheckpointLineDiff>();
        public bool truncated;
    }

    [Serializable]
    public sealed class CheckpointRestoreOutput
    {
        public bool dryRun;
        public bool restored;
        public string checkpointId;
        public string scenePath;
        public string backupPath;
        public string summary;
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    /// <summary>
    /// Bounded on-disk scene snapshot, listing, diff, and confirmed restore commands.
    /// </summary>
    public static class EditorCheckpointCommands
    {
        private const string Group = "checkpoint";
        private const long MaxCheckpointCompareBytes = 32L * 1024L * 1024L;
        private static readonly Regex CheckpointIdPattern = new Regex("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.Compiled);

        [CliGroupCommand(
            Group,
            "Create a bounded on-disk snapshot of an Assets/*.unity scene under Library/UnityMCP/checkpoints.",
            Name = "create",
            Effect = CommandEffect.Additive,
            Tags = new[] { "scene", "checkpoint" })]
        internal static ExitCode CreateCommand(
            CommandContext cmd,
            [CliArg("scene-path", "Project-relative path to the clean scene asset (Assets/.../*.unity).", Required = true)] string scenePath = null,
            [CliArg("checkpoint-id", "Optional checkpoint ID (1-64 alphanumeric, underscore, or hyphen characters).")] string checkpointId = null,
            [CliArg("dry-run", "Validate and preview checkpoint creation without writing files.")] bool dryRun = false)
        {
            return Execute(cmd, () => Create(new CheckpointCreateInput
            {
                scenePath = scenePath,
                checkpointId = checkpointId,
                dryRun = dryRun
            }));
        }

        [CliGroupCommand(
            Group,
            "List valid UnityMCP scene checkpoints stored under Library/UnityMCP/checkpoints.",
            Name = "list",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "scene", "checkpoint" })]
        internal static ExitCode ListCommand(
            CommandContext cmd,
            [CliArg("limit", "Maximum checkpoints to list (1-1000, default 100).")] int limit = 100)
        {
            return Execute(cmd, () => List(new CheckpointListInput { limit = limit }));
        }

        [CliGroupCommand(
            Group,
            "Compare a checkpoint with its Assets/*.unity source scene using hashes and a bounded line-by-line diff.",
            Name = "diff",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "scene", "checkpoint" })]
        internal static ExitCode DiffCommand(
            CommandContext cmd,
            [CliArg("checkpoint-id", "Checkpoint ID to compare against the scene on disk.", Required = true)] string checkpointId = null,
            [CliArg("scene-path", "Optional scene path (must match the scene recorded in the checkpoint if specified).")] string scenePath = null,
            [CliArg("max-lines", "Maximum differing lines to return (1-200, default 50).")] int maxLines = 50)
        {
            return Execute(cmd, () => Diff(new CheckpointDiffInput
            {
                checkpointId = checkpointId,
                scenePath = scenePath,
                maxLines = maxLines
            }));
        }

        [CliGroupCommand(
            Group,
            "Restore an unloaded, clean Assets/*.unity scene from a local checkpoint (requires --confirm true unless --dry-run true).",
            Name = "restore",
            Effect = CommandEffect.Destructive,
            Tags = new[] { "scene", "checkpoint" })]
        internal static ExitCode RestoreCommand(
            CommandContext cmd,
            [CliArg("checkpoint-id", "Checkpoint ID to restore.", Required = true)] string checkpointId = null,
            [CliArg("confirm", "Explicitly confirm overwriting the target .unity scene file (required unless --dry-run true).")] bool confirm = false,
            [CliArg("dry-run", "Validate and preview the restore without modifying files.")] bool dryRun = false)
        {
            return Execute(cmd, () => Restore(new CheckpointRestoreInput
            {
                checkpointId = checkpointId,
                confirm = confirm,
                dryRun = dryRun
            }));
        }

        internal static CheckpointCreateOutput Create(CheckpointCreateInput input)
        {
            if (input == null)
                throw new ArgumentException("input is required.");

            var scenePath = RequireExistingSceneAsset(input.scenePath);
            RefuseDirtyLoadedScene(scenePath, "create a checkpoint");

            var id = string.IsNullOrWhiteSpace(input.checkpointId)
                ? NewCheckpointId(scenePath)
                : RequireCheckpointId(input.checkpointId);

            var manifestPath = CheckpointManifestPath(id);
            var snapshotPath = CheckpointSnapshotPath(id);
            if (File.Exists(manifestPath) || File.Exists(snapshotPath))
                throw new InvalidOperationException("A checkpoint with this id already exists.");

            var sourcePath = AssetFullPath(scenePath);
            var manifest = new CheckpointManifest
            {
                id = id,
                scenePath = scenePath,
                createdUtc = DateTime.UtcNow.ToString("O"),
                sourceSha256 = HashFile(sourcePath),
                sourceBytes = new FileInfo(sourcePath).Length,
                snapshotFile = Path.GetFileName(snapshotPath)
            };

            if (!input.dryRun)
            {
                Directory.CreateDirectory(CheckpointDirectory);
                File.Copy(sourcePath, snapshotPath, false);
                File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented), new UTF8Encoding(false));
            }

            return new CheckpointCreateOutput
            {
                dryRun = input.dryRun,
                created = !input.dryRun,
                checkpoint = ToCheckpointInfo(manifest),
                summary = $"Create checkpoint '{id}' for '{scenePath}'.",
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry>
                {
                    new ChangeJournalEntry
                    {
                        operation = "checkpoint-scene",
                        before = scenePath,
                        after = "Library/UnityMCP/checkpoints/" + manifest.snapshotFile
                    }
                }
            };
        }

        internal static CheckpointListOutput List(CheckpointListInput input)
        {
            var output = new CheckpointListOutput();
            if (!Directory.Exists(CheckpointDirectory))
                return output;

            var limit = Math.Max(1, Math.Min(input?.limit ?? 100, 1000));
            foreach (var manifestPath in Directory.GetFiles(CheckpointDirectory, "*.json", SearchOption.TopDirectoryOnly)
                         .OrderByDescending(value => value, StringComparer.Ordinal))
            {
                if (output.checkpoints.Count >= limit)
                {
                    output.truncated = true;
                    break;
                }

                CheckpointManifest manifest;
                try
                {
                    manifest = ReadCheckpointManifest(manifestPath);
                }
                catch
                {
                    continue;
                }

                output.checkpoints.Add(ToCheckpointInfo(manifest));
            }

            return output;
        }

        internal static CheckpointDiffOutput Diff(CheckpointDiffInput input)
        {
            if (input == null)
                throw new ArgumentException("input is required.");

            var manifest = LoadCheckpoint(input.checkpointId);
            var scenePath = string.IsNullOrWhiteSpace(input.scenePath)
                ? manifest.scenePath
                : RequireExistingSceneAsset(input.scenePath);

            if (!string.Equals(scenePath, manifest.scenePath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("scenePath must match the scene stored in the checkpoint.");

            var currentPath = AssetFullPath(scenePath);
            var snapshotPath = CheckpointSnapshotPath(manifest);
            RequireComparableCheckpointFile(snapshotPath);
            RequireComparableCheckpointFile(currentPath);

            var output = new CheckpointDiffOutput
            {
                checkpointId = manifest.id,
                scenePath = scenePath,
                checkpointSha256 = HashFile(snapshotPath),
                currentSha256 = HashFile(currentPath),
                checkpointBytes = new FileInfo(snapshotPath).Length,
                currentBytes = new FileInfo(currentPath).Length
            };

            output.sameContent = string.Equals(output.checkpointSha256, output.currentSha256, StringComparison.Ordinal);
            if (output.sameContent)
                return output;

            var maxLines = Math.Max(1, Math.Min(input.maxLines, 200));
            AppendLineDifferences(snapshotPath, currentPath, maxLines, output);
            return output;
        }

        internal static CheckpointRestoreOutput Restore(CheckpointRestoreInput input)
        {
            if (input == null)
                throw new ArgumentException("input is required.");

            var manifest = LoadCheckpoint(input.checkpointId);
            var scenePath = RequireExistingSceneAsset(manifest.scenePath);
            RefuseLoadedOrDirtyScene(scenePath, "restore a checkpoint");

            var snapshotPath = CheckpointSnapshotPath(manifest);
            if (!File.Exists(snapshotPath))
                throw new FileNotFoundException("The checkpoint snapshot is missing.");

            if (!input.dryRun && !input.confirm)
                throw new InvalidOperationException($"Restoring checkpoint '{manifest.id}' overwrites '{scenePath}'; pass confirm=true to proceed (or dryRun=true to preview).");

            var sourcePath = AssetFullPath(scenePath);
            var backupName = $"{manifest.id}-before-restore-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 8)}.unity";
            var backupPath = ContainedCheckpointPath(backupName);

            if (!input.dryRun)
            {
                Directory.CreateDirectory(CheckpointDirectory);
                File.Copy(sourcePath, backupPath, false);
                File.Copy(snapshotPath, sourcePath, true);
                AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }

            return new CheckpointRestoreOutput
            {
                dryRun = input.dryRun,
                restored = !input.dryRun,
                checkpointId = manifest.id,
                scenePath = scenePath,
                backupPath = "Library/UnityMCP/checkpoints/" + Path.GetFileName(backupPath),
                summary = $"Restore checkpoint '{manifest.id}' to unloaded scene '{scenePath}'.",
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry>
                {
                    new ChangeJournalEntry
                    {
                        operation = "restore-scene-checkpoint",
                        before = scenePath,
                        after = "Library/UnityMCP/checkpoints/" + Path.GetFileName(snapshotPath)
                    },
                    new ChangeJournalEntry
                    {
                        operation = "backup-before-restore",
                        before = scenePath,
                        after = "Library/UnityMCP/checkpoints/" + Path.GetFileName(backupPath)
                    }
                }
            };
        }

        [Serializable]
        private sealed class CheckpointManifest
        {
            public string id;
            public string scenePath;
            public string createdUtc;
            public string sourceSha256;
            public long sourceBytes;
            public string snapshotFile;
        }

        private static string ProjectRoot
        {
            get
            {
                var root = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(root))
                    throw new InvalidOperationException("Could not determine the Unity project root.");
                return Path.GetFullPath(root);
            }
        }

        private static string CheckpointDirectory => Path.Combine(ProjectRoot, "Library", "UnityMCP", "checkpoints");

        private static string RequireExistingSceneAsset(string path)
        {
            var normalized = EditorCommandHelpers.ValidateAssetPath(path, ".unity");
            if (!File.Exists(AssetFullPath(normalized)))
                throw new ArgumentException("The scene asset was not found: " + normalized);
            return normalized;
        }

        private static void RefuseDirtyLoadedScene(string scenePath, string operation)
        {
            var scene = FindLoadedScene(scenePath);
            if (scene.IsValid() && scene.isDirty)
                throw new InvalidOperationException($"Cannot {operation} while the target scene has unsaved changes. Save or close it first.");
        }

        private static void RefuseLoadedOrDirtyScene(string scenePath, string operation)
        {
            var scene = FindLoadedScene(scenePath);
            if (!scene.IsValid())
                return;
            if (scene.isDirty)
                throw new InvalidOperationException($"Cannot {operation} while the target scene is loaded and dirty. Save or close it first.");
            throw new InvalidOperationException($"Cannot {operation} while the target scene is loaded. Close it first to avoid invalidating the Editor scene state.");
        }

        private static Scene FindLoadedScene(string scenePath)
        {
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene.isLoaded && string.Equals(scene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                    return scene;
            }
            return default;
        }

        private static string RequireCheckpointId(string checkpointId)
        {
            if (!CheckpointIdPattern.IsMatch(checkpointId ?? string.Empty))
                throw new ArgumentException("checkpointId must contain 1-64 letters, numbers, underscores, or hyphens and may not start with punctuation.");
            return checkpointId;
        }

        private static string NewCheckpointId(string scenePath)
        {
            var baseName = Path.GetFileNameWithoutExtension(scenePath);
            var safe = new string(baseName.Select(character =>
                (character >= 'A' && character <= 'Z') ||
                (character >= 'a' && character <= 'z') ||
                (character >= '0' && character <= '9')
                    ? character
                    : '-').ToArray()).Trim('-');
            if (string.IsNullOrEmpty(safe))
                safe = "scene";
            var id = $"{safe}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            return id.Substring(0, Math.Min(64, id.Length));
        }

        private static string CheckpointManifestPath(string id) => ContainedCheckpointPath(RequireCheckpointId(id) + ".json");
        private static string CheckpointSnapshotPath(string id) => ContainedCheckpointPath(RequireCheckpointId(id) + ".unity");

        private static string CheckpointSnapshotPath(CheckpointManifest manifest)
        {
            ValidateCheckpointManifest(manifest);
            return ContainedCheckpointPath(manifest.snapshotFile);
        }

        private static string ContainedCheckpointPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
                throw new ArgumentException("Checkpoint storage must use a simple file name.");

            var directory = Path.GetFullPath(CheckpointDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var path = Path.GetFullPath(Path.Combine(directory, fileName));
            if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Checkpoint path escapes local UnityMCP storage.");
            return path;
        }

        private static CheckpointManifest LoadCheckpoint(string checkpointId)
        {
            var path = CheckpointManifestPath(RequireCheckpointId(checkpointId));
            if (!File.Exists(path))
                throw new FileNotFoundException("The requested checkpoint was not found.");
            return ReadCheckpointManifest(path);
        }

        private static CheckpointManifest ReadCheckpointManifest(string path)
        {
            var manifest = JsonConvert.DeserializeObject<CheckpointManifest>(File.ReadAllText(path));
            ValidateCheckpointManifest(manifest);
            return manifest;
        }

        private static void ValidateCheckpointManifest(CheckpointManifest manifest)
        {
            if (manifest == null)
                throw new ArgumentException("Checkpoint metadata is invalid.");
            RequireCheckpointId(manifest.id);
            EditorCommandHelpers.ValidateAssetPath(manifest.scenePath, ".unity");
            if (string.IsNullOrWhiteSpace(manifest.snapshotFile) || !string.Equals(manifest.snapshotFile, manifest.id + ".unity", StringComparison.Ordinal))
                throw new ArgumentException("Checkpoint metadata references an invalid snapshot file.");
            if (manifest.sourceBytes < 0 || string.IsNullOrWhiteSpace(manifest.sourceSha256))
                throw new ArgumentException("Checkpoint metadata is incomplete.");
        }

        private static CheckpointInfo ToCheckpointInfo(CheckpointManifest manifest)
        {
            return new CheckpointInfo
            {
                checkpointId = manifest.id,
                scenePath = manifest.scenePath,
                createdUtc = manifest.createdUtc,
                sourceSha256 = manifest.sourceSha256,
                sourceBytes = manifest.sourceBytes,
                snapshotExists = File.Exists(CheckpointSnapshotPath(manifest))
            };
        }

        private static void RequireComparableCheckpointFile(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                throw new FileNotFoundException("A checkpoint comparison file is missing.");
            if (info.Length > MaxCheckpointCompareBytes)
                throw new InvalidOperationException("Checkpoint comparisons are limited to 32 MiB scene files.");
        }

        private static void AppendLineDifferences(string checkpointPath, string currentPath, int maxLines, CheckpointDiffOutput output)
        {
            var checkpointLines = File.ReadAllLines(checkpointPath, Encoding.UTF8);
            var currentLines = File.ReadAllLines(currentPath, Encoding.UTF8);
            var count = Math.Max(checkpointLines.Length, currentLines.Length);
            for (var index = 0; index < count; index++)
            {
                var checkpoint = index < checkpointLines.Length ? checkpointLines[index] : null;
                var current = index < currentLines.Length ? currentLines[index] : null;
                if (string.Equals(checkpoint, current, StringComparison.Ordinal))
                    continue;
                if (output.lines.Count >= maxLines)
                {
                    output.truncated = true;
                    break;
                }

                output.lines.Add(new CheckpointLineDiff
                {
                    line = index + 1,
                    checkpoint = ClipLine(checkpoint),
                    current = ClipLine(current)
                });
            }
        }

        private static string ClipLine(string value)
        {
            if (value == null)
                return null;
            return value.Length <= 512 ? value : value.Substring(0, 512) + "...";
        }

        private static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")).ToArray());
        }

        private static string AssetFullPath(string assetPath)
        {
            var normalized = EditorCommandHelpers.ValidateAssetPath(assetPath);
            return Path.GetFullPath(Path.Combine(ProjectRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
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
            catch (FileNotFoundException ex)
            {
                cmd.Log.Error(ex.Message);
                return ExitCode.NotFound;
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
