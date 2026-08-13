using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable] public sealed class TimelineCreateInput
    {
        public string path;
        public string name;
        /// <summary>Optional loaded GameObject that receives or reuses a PlayableDirector.</summary>
        public int? directorGameObjectInstanceId;
        public bool apply;
    }

    [Serializable] public sealed class TimelineCreateOutput
    {
        public bool dryRun;
        public bool created;
        public string path;
        public string name;
        public string revision;
        public int? directorInstanceId;
        public bool directorCreated;
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    [Serializable] public sealed class TimelineEditInput
    {
        public string timelinePath;
        /// <summary>Required for apply=true. Omit only when using dry-run to inspect the current revision.</summary>
        public string expectedRevision;
        /// <summary>group, activation, animation, audio, control, playable, or signal.</summary>
        public string trackKind = "group";
        public string trackName;
        public bool? muted;
        public bool? locked;
        /// <summary>Loaded GameObject that has a PlayableDirector, required only when bindingObjectInstanceId is supplied.</summary>
        public int? directorGameObjectInstanceId;
        /// <summary>Loaded GameObject or Component to bind to the newly-created root track.</summary>
        public int? bindingObjectInstanceId;
        public bool apply;
    }

    [Serializable] public sealed class TimelineEditOutput
    {
        public bool dryRun;
        public bool changed;
        public string timelinePath;
        public string revisionBefore;
        public string revisionAfter;
        public string trackKind;
        public string trackName;
        public int? trackInstanceId;
        public int? directorInstanceId;
        public int? bindingObjectInstanceId;
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    /// <summary>
    /// Timeline integration compiled without a Timeline assembly reference. The registry only adds
    /// these tools when TimelineAsset is present; all Timeline API calls are otherwise reflected.
    /// Timeline edit intentionally supports one bounded operation: adding a standard root track
    /// and, optionally, binding that new track to an existing PlayableDirector scene object.
    /// </summary>
    public static class EditorTimelineOptionalTools
    {
        private const long MaxTimelineFileBytes = 32L * 1024L * 1024L;

        [UnityMcpTool("timeline-create", Description = "Create a Timeline asset and optionally assign it to a loaded GameObject's PlayableDirector; dry-run unless apply is true.", Category = "animation-timeline", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, RequiredType = "UnityEngine.Timeline.TimelineAsset")]
        public static TimelineCreateOutput TimelineCreate(TimelineCreateInput input, UnityMcpContext context)
        {
            var path = NormalizeCreatableAssetPath(input.path, ".playable");
            var fullPath = ToFullPath(path);
            if (File.Exists(fullPath) || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new InvalidOperationException("An asset already exists at the requested Timeline path.");
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(parent) || !AssetDatabase.IsValidFolder(parent)) throw new ArgumentException("The target Timeline folder must already exist under Assets.");
            EnsureNoReparsePoint(AssetsRoot, ToFullPath(parent));
            var name = string.IsNullOrWhiteSpace(input.name) ? Path.GetFileNameWithoutExtension(path) : RequireName(input.name, "name");
            var timelineType = RequireType("UnityEngine.Timeline.TimelineAsset");
            var target = input.directorGameObjectInstanceId.HasValue ? RequireSceneGameObject(input.directorGameObjectInstanceId.Value, "directorGameObjectInstanceId") : null;
            var existingDirector = target == null ? null : target.GetComponent<PlayableDirector>();

            TimelineCreateOutput output;
            if (context.DryRun)
            {
                output = new TimelineCreateOutput
                {
                    dryRun = true,
                    created = false,
                    path = path,
                    name = name,
                    directorInstanceId = existingDirector == null ? (int?)null : existingDirector.GetInstanceID(),
                    directorCreated = target != null && existingDirector == null,
                    rollbackSupported = true,
                    journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "create-timeline", after = path } }
                };
                return output;
            }

            var timeline = ScriptableObject.CreateInstance(timelineType);
            if (timeline == null) throw new InvalidOperationException("Unity could not create a TimelineAsset instance.");
            timeline.name = name;
            AssetDatabase.CreateAsset(timeline, path);
            Undo.RegisterCreatedObjectUndo(timeline, "UnityMCP Create Timeline");
            PlayableDirector director = null;
            var createdDirector = false;
            if (target != null)
            {
                director = existingDirector;
                if (director == null)
                {
                    director = Undo.AddComponent(target, typeof(PlayableDirector)) as PlayableDirector;
                    if (director == null) throw new InvalidOperationException("Unity could not add a PlayableDirector to the requested GameObject.");
                    createdDirector = true;
                }
                else Undo.RecordObject(director, "UnityMCP Assign Timeline");
                director.playableAsset = timeline as PlayableAsset;
                EditorUtility.SetDirty(director);
                EditorSceneManager.MarkSceneDirty(target.scene);
            }
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();

            output = new TimelineCreateOutput
            {
                dryRun = false,
                created = true,
                path = path,
                name = name,
                revision = FileRevision(fullPath),
                directorInstanceId = director == null ? (int?)null : director.GetInstanceID(),
                directorCreated = createdDirector,
                rollbackSupported = true,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "create-timeline", after = path } }
            };
            return output;
        }

        [UnityMcpTool("timeline-edit", Description = "Add one validated standard root Timeline track and optional director binding with revision protection; dry-run unless apply is true.", Category = "animation-timeline", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, RequiredType = "UnityEngine.Timeline.TimelineAsset")]
        public static TimelineEditOutput TimelineEdit(TimelineEditInput input, UnityMcpContext context)
        {
            var path = NormalizeExistingAssetPath(input.timelinePath, ".playable");
            var fullPath = ToFullPath(path);
            var before = FileRevision(fullPath);
            if (!string.IsNullOrWhiteSpace(input.expectedRevision) && !string.Equals(input.expectedRevision, before, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("expectedRevision does not match the current Timeline asset contents.");
            if (!context.DryRun && string.IsNullOrWhiteSpace(input.expectedRevision))
                throw new ArgumentException("expectedRevision is required when apply=true. First call with apply=false to obtain the current revision.");

            var timelineType = RequireType("UnityEngine.Timeline.TimelineAsset");
            var trackAssetType = RequireType("UnityEngine.Timeline.TrackAsset");
            var timeline = AssetDatabase.LoadAssetAtPath(path, timelineType);
            if (timeline == null) throw new ArgumentException("timelinePath must identify a TimelineAsset.");
            var trackKind = NormalizeTrackKind(input.trackKind);
            var trackType = RequireType(TrackTypeName(trackKind));
            if (!trackAssetType.IsAssignableFrom(trackType)) throw new InvalidOperationException("The installed Timeline type for '" + trackKind + "' is not a TrackAsset.");
            var trackName = RequireName(input.trackName, "trackName");
            if (RootTrackNames(timeline).Any(name => string.Equals(name, trackName, StringComparison.Ordinal)))
                throw new ArgumentException("The Timeline already has a root track named '" + trackName + "'.");
            if (input.bindingObjectInstanceId.HasValue && trackKind == "group")
                throw new ArgumentException("Group tracks cannot be bound to a PlayableDirector object.");

            PlayableDirector director = null;
            UnityEngine.Object binding = null;
            if (input.bindingObjectInstanceId.HasValue)
            {
                if (!input.directorGameObjectInstanceId.HasValue) throw new ArgumentException("directorGameObjectInstanceId is required when bindingObjectInstanceId is supplied.");
                var directorTarget = RequireSceneGameObject(input.directorGameObjectInstanceId.Value, "directorGameObjectInstanceId");
                director = directorTarget.GetComponent<PlayableDirector>();
                if (director == null) throw new ArgumentException("directorGameObjectInstanceId must identify a GameObject that already has a PlayableDirector.");
                binding = RequireSceneObject(input.bindingObjectInstanceId.Value, "bindingObjectInstanceId");
            }
            else if (input.directorGameObjectInstanceId.HasValue)
            {
                var directorTarget = RequireSceneGameObject(input.directorGameObjectInstanceId.Value, "directorGameObjectInstanceId");
                director = directorTarget.GetComponent<PlayableDirector>();
                if (director == null) throw new ArgumentException("directorGameObjectInstanceId must identify a GameObject that already has a PlayableDirector.");
            }

            var createTrack = timelineType.GetMethod("CreateTrack", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Type), trackAssetType, typeof(string) }, null);
            if (createTrack == null) throw new InvalidOperationException("This Timeline package does not expose TimelineAsset.CreateTrack(Type, TrackAsset, string).");
            ValidateOptionalBoolMember(trackType, "muted", input.muted);
            ValidateOptionalBoolMember(trackType, "locked", input.locked);

            if (context.DryRun)
            {
                return new TimelineEditOutput
                {
                    dryRun = true,
                    changed = false,
                    timelinePath = path,
                    revisionBefore = before,
                    revisionAfter = before,
                    trackKind = trackKind,
                    trackName = trackName,
                    directorInstanceId = director == null ? (int?)null : director.GetInstanceID(),
                    bindingObjectInstanceId = binding == null ? (int?)null : binding.GetInstanceID(),
                    rollbackSupported = true,
                    journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "add-timeline-track", after = path + "#" + trackName } }
                };
            }

            Undo.RecordObject(timeline, "UnityMCP Add Timeline Track");
            var track = createTrack.Invoke(timeline, new object[] { trackType, null, trackName }) as UnityEngine.Object;
            if (track == null) throw new InvalidOperationException("Unity could not create the requested Timeline track.");
            Undo.RegisterCreatedObjectUndo(track, "UnityMCP Add Timeline Track");
            if (input.muted.HasValue) SetMember(track, "muted", input.muted.Value);
            if (input.locked.HasValue) SetMember(track, "locked", input.locked.Value);
            if (director != null && binding != null)
            {
                Undo.RecordObject(director, "UnityMCP Bind Timeline Track");
                director.SetGenericBinding(track, binding);
                EditorUtility.SetDirty(director);
                EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
            }
            EditorUtility.SetDirty(track);
            EditorUtility.SetDirty(timeline as UnityEngine.Object);
            AssetDatabase.SaveAssets();
            var after = FileRevision(fullPath);
            return new TimelineEditOutput
            {
                dryRun = false,
                changed = true,
                timelinePath = path,
                revisionBefore = before,
                revisionAfter = after,
                trackKind = trackKind,
                trackName = trackName,
                trackInstanceId = track.GetInstanceID(),
                directorInstanceId = director == null ? (int?)null : director.GetInstanceID(),
                bindingObjectInstanceId = binding == null ? (int?)null : binding.GetInstanceID(),
                rollbackSupported = true,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "add-timeline-track", after = path + "#" + trackName } }
            };
        }

        private static IEnumerable<string> RootTrackNames(object timeline)
        {
            var method = timeline.GetType().GetMethod("GetRootTracks", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (method == null) throw new InvalidOperationException("This Timeline package does not expose TimelineAsset.GetRootTracks().");
            var tracks = method.Invoke(timeline, null) as IEnumerable;
            if (tracks == null) throw new InvalidOperationException("TimelineAsset.GetRootTracks() did not return an enumerable result.");
            var names = new List<string>();
            foreach (var item in tracks)
                if (item is UnityEngine.Object unityObject) names.Add(unityObject.name);
            return names;
        }

        private static string NormalizeTrackKind(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "group":
                case "activation":
                case "animation":
                case "audio":
                case "control":
                case "playable":
                case "signal": return (value ?? string.Empty).Trim().ToLowerInvariant();
                default: throw new ArgumentException("trackKind must be group, activation, animation, audio, control, playable, or signal.");
            }
        }

        private static string TrackTypeName(string kind)
        {
            switch (kind)
            {
                case "group": return "UnityEngine.Timeline.GroupTrack";
                case "activation": return "UnityEngine.Timeline.ActivationTrack";
                case "animation": return "UnityEngine.Timeline.AnimationTrack";
                case "audio": return "UnityEngine.Timeline.AudioTrack";
                case "control": return "UnityEngine.Timeline.ControlTrack";
                case "playable": return "UnityEngine.Timeline.PlayableTrack";
                case "signal": return "UnityEngine.Timeline.SignalTrack";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static void ValidateOptionalBoolMember(Type type, string member, bool? value)
        {
            if (!value.HasValue) return;
            var property = type.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
            var field = type.GetField(member, BindingFlags.Public | BindingFlags.Instance);
            var memberType = property?.PropertyType ?? field?.FieldType;
            if (memberType != typeof(bool) || (property != null && !property.CanWrite))
                throw new InvalidOperationException("The installed Timeline track type does not expose a writable bool '" + member + "' member.");
        }

        private static void SetMember(object target, string member, object value)
        {
            var type = target.GetType();
            var property = type.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite) { property.SetValue(target, value, null); return; }
            var field = type.GetField(member, BindingFlags.Public | BindingFlags.Instance);
            if (field != null) { field.SetValue(target, value); return; }
            throw new InvalidOperationException(type.FullName + " has no writable member '" + member + "'.");
        }

        private static GameObject RequireSceneGameObject(int instanceId, string inputName)
        {
            var value = EditorUtility.InstanceIDToObject(instanceId);
            var gameObject = value as GameObject ?? (value as Component)?.gameObject;
            if (gameObject == null || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded)
                throw new ArgumentException(inputName + " must identify a GameObject or Component in a loaded scene.");
            return gameObject;
        }

        private static UnityEngine.Object RequireSceneObject(int instanceId, string inputName)
        {
            var value = EditorUtility.InstanceIDToObject(instanceId);
            var gameObject = value as GameObject ?? (value as Component)?.gameObject;
            if (value == null || gameObject == null || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded)
                throw new ArgumentException(inputName + " must identify a GameObject or Component in a loaded scene.");
            return value;
        }

        private static Type RequireType(string fullName)
        {
            var type = Type.GetType(fullName, false);
            if (type != null) return type;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(fullName, false);
                    if (type != null) return type;
                }
                catch { }
            }
            throw new InvalidOperationException("The required optional Unity type is unavailable: " + fullName);
        }

        private static string RequireName(string value, string inputName)
        {
            var result = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(result) || result.Length > 128 || result.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException(inputName + " must be a non-empty name of at most 128 characters.");
            return result;
        }

        private static string FileRevision(string fullPath)
        {
            var info = new FileInfo(fullPath);
            if (!info.Exists) throw new ArgumentException("Asset file does not exist.");
            if (info.Length > MaxTimelineFileBytes) throw new ArgumentException("Timeline asset exceeds the 32 MiB revision limit.");
            using (var stream = File.OpenRead(fullPath))
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string NormalizeExistingAssetPath(string path, string extension)
        {
            var result = NormalizeAssetPath(path, extension);
            var fullPath = ToFullPath(result);
            if (!File.Exists(fullPath)) throw new ArgumentException("Asset file was not found: " + result);
            EnsureNoReparsePoint(AssetsRoot, fullPath);
            return result;
        }

        private static string NormalizeCreatableAssetPath(string path, string extension)
        {
            var result = NormalizeAssetPath(path, extension);
            if (!IsContained(AssetsRoot, ToFullPath(result))) throw new ArgumentException("Asset path must remain under Assets/.");
            return result;
        }

        private static string NormalizeAssetPath(string path, string extension)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A project-relative path below Assets/ is required.");
            var result = path.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(result) || result.Contains(":")) throw new ArgumentException("Asset paths must be project-relative below Assets/.");
            var parts = result.Split('/');
            if (parts.Length < 2 || !string.Equals(parts[0], "Assets", StringComparison.Ordinal) || parts.Any(part => string.IsNullOrEmpty(part) || part == "." || part == ".."))
                throw new ArgumentException("Asset paths must be normalized below Assets/ without traversal segments.");
            if (result.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Unity .meta files are not valid tool targets.");
            if (extension != null && !result.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Asset path must end with " + extension + ".");
            return result;
        }

        private static string AssetsRoot => Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        private static string ProjectRoot => Directory.GetParent(AssetsRoot).FullName;

        private static string ToFullPath(string assetPath)
        {
            var result = Path.GetFullPath(Path.Combine(ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsContained(AssetsRoot, result)) throw new ArgumentException("Asset path escapes the project's Assets directory.");
            return result;
        }

        private static bool IsContained(string root, string path)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedPath = Path.GetFullPath(path);
            return string.Equals(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase) || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureNoReparsePoint(string root, string fullPath)
        {
            if (!IsContained(root, fullPath)) throw new ArgumentException("Path must remain within the allowed root.");
            var current = Path.GetFullPath(root);
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("The Assets root may not be a symbolic link or junction.");
            var relative = fullPath.Substring(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var part in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                if (!File.Exists(current) && !Directory.Exists(current)) break;
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Symbolic links and junctions are not permitted in tool paths.");
            }
        }
    }
}
