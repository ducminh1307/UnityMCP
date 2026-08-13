using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable] public sealed class AssetReferencesInput { public string path; public int limit = 100; }
    [Serializable] public sealed class AssetReferencesOutput { public string path; public List<AssetSummary> references = new List<AssetSummary>(); public int scannedAssets; public bool truncated; }

    [Serializable] public sealed class ShaderCreateInput { public string path; public string shaderName; public string content; public bool apply; }
    [Serializable] public sealed class ShaderEditInput { public string path; public string expectedRevision; public string content; public bool apply; }
    [Serializable] public sealed class ShaderWriteOutput
    {
        public bool dryRun;
        public bool changed;
        public string path;
        public string revisionBefore;
        public string revisionAfter;
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    [Serializable] public sealed class LightingSettingsOutput
    {
        public string scene;
        public string scenePath;
        public bool hasSettings;
        public string settingsPath;
        public bool isBaking;
        public float bakeProgress;
        public bool bakedGi;
        public bool realtimeGi;
        public string mixedBakeMode;
        public string lightmapper;
        public float lightmapResolution;
        public int lightmapMaxSize;
        public int directSampleCount;
        public int indirectSampleCount;
        public int environmentSampleCount;
        public float indirectScale;
    }

    [Serializable] public sealed class LightingSettingsSetInput
    {
        public bool? bakedGi;
        public bool? realtimeGi;
        public string mixedBakeMode;
        public string lightmapper;
        public float? lightmapResolution;
        public int? lightmapMaxSize;
        public int? directSampleCount;
        public int? indirectSampleCount;
        public int? environmentSampleCount;
        public float? indirectScale;
        public bool apply;
    }

    [Serializable] public sealed class ScreenshotGameObjectInput { public int instanceId; public int width = 512; public int height = 512; }
    [Serializable] public sealed class ScreenshotGameObjectOutput { public int instanceId; public string name; public string source; public int width; public int height; }

    [Serializable] public sealed class ProfilerCounter { public string name; public long value; public string unit = "bytes"; }
    [Serializable] public sealed class ProfilerCountersOutput { public int frame; public bool profilerEnabled; public List<ProfilerCounter> counters = new List<ProfilerCounter>(); }
    [Serializable] public sealed class ProfilerFrameCaptureOutput
    {
        public string captureKind;
        public int frame;
        public double editorTimeSinceStartup;
        public bool profilerEnabled;
        public List<ProfilerCounter> counters = new List<ProfilerCounter>();
    }

    [Serializable] public sealed class FrameDebuggerEventsInput { public int startIndex; public int limit = 100; public bool includeObjectMetadata = true; }
    [Serializable] public sealed class FrameDebuggerEventSummary
    {
        public int index;
        public string eventType;
        public string name;
        public int? objectInstanceId;
        public string objectName;
        public string objectType;
    }
    [Serializable] public sealed class FrameDebuggerEventsOutput
    {
        public bool supported;
        public bool receivingRemoteFrameEventData;
        public int totalEvents;
        public int eventsHash;
        public uint eventDataHash;
        public string note;
        public bool truncated;
        public List<FrameDebuggerEventSummary> events = new List<FrameDebuggerEventSummary>();
    }

    [Serializable] public sealed class StructuredScriptEdit { public string kind = "rename-identifier"; public string from; public string to; }
    [Serializable] public sealed class ScriptApplyStructuredEditsInput { public string path; public string expectedRevision; public List<StructuredScriptEdit> edits = new List<StructuredScriptEdit>(); public bool apply; }
    [Serializable] public sealed class StructuredScriptRenameResult { public string from; public string to; public int replacements; }
    [Serializable] public sealed class StructuredScriptWriteOutput
    {
        public bool dryRun;
        public bool changed;
        public string path;
        public string revisionBefore;
        public string revisionAfter;
        public bool rollbackSupported;
        public string semantics;
        public List<StructuredScriptRenameResult> edits = new List<StructuredScriptRenameResult>();
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    /// <summary>
    /// Remaining dependency-free Editor tools. They only use Unity's public APIs,
    /// except Frame Debugger readback which uses a version-checked reflection bridge
    /// to Unity's internal, read-only FrameDebuggerUtility.
    /// </summary>
    public static class EditorRemainingCoreTools
    {
        private const int MaxAssetReferenceScan = 20000;
        private const int MaxShaderBytes = 512 * 1024;
        private const int MaxStructuredScriptBytes = 512 * 1024;
        private const int MaxStructuredScriptEdits = 16;
        private static readonly Regex Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
        private static readonly HashSet<string> CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while", "add", "alias", "ascending", "async", "await", "by", "descending", "dynamic", "equals", "from", "get", "global", "group", "into", "join", "let", "nameof", "not", "on", "orderby", "partial", "remove", "select", "set", "unmanaged", "value", "var", "when", "where", "yield"
        };

        [UnityMcpTool("asset-references", Description = "Find project assets with a direct AssetDatabase dependency on a target asset. The scan is bounded.", Category = "asset", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static AssetReferencesOutput AssetReferences(AssetReferencesInput input)
        {
            var target = RequireExistingProjectAsset(input.path, null);
            var limit = Mathf.Clamp(input.limit, 1, 1000);
            var output = new AssetReferencesOutput { path = target };
            var guids = AssetDatabase.FindAssets(string.Empty, new[] { "Assets" });
            foreach (var guid in guids.OrderBy(value => value, StringComparer.Ordinal))
            {
                if (output.scannedAssets >= MaxAssetReferenceScan) { output.truncated = true; break; }
                output.scannedAssets++;
                var candidate = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(candidate) || string.Equals(candidate, target, StringComparison.OrdinalIgnoreCase)) continue;
                string[] dependencies;
                try { dependencies = AssetDatabase.GetDependencies(candidate, false); }
                catch { continue; }
                if (!dependencies.Any(value => string.Equals(value, target, StringComparison.OrdinalIgnoreCase))) continue;
                if (output.references.Count >= limit) { output.truncated = true; break; }
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

        [UnityMcpTool("shader-create", Description = "Create a contained ShaderLab source file; dry-run unless apply is true.", Category = "shader", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Unsafe, SupportsDryRun = true)]
        public static ShaderWriteOutput ShaderCreate(ShaderCreateInput input, UnityMcpContext context)
        {
            var fullPath = RequireProjectTextPath(input.path, ".shader", false);
            var assetPath = ToAssetPath(fullPath);
            if (File.Exists(fullPath) || AssetDatabase.LoadMainAssetAtPath(assetPath) != null) throw new InvalidOperationException("A Shader asset already exists at this path.");
            var parent = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) throw new ArgumentException("The target Shader folder must already exist under Assets.");
            var content = string.IsNullOrEmpty(input.content) ? DefaultShaderSource(input.shaderName, Path.GetFileNameWithoutExtension(assetPath)) : input.content;
            var after = EncodeUtf8(content, false);
            EnsureTextLength(after, MaxShaderBytes, "Shader content");
            if (!context.DryRun)
            {
                File.WriteAllBytes(fullPath, after);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }
            return TextChange(context, assetPath, null, Revision(after), "create-shader", null, assetPath, true);
        }

        [UnityMcpTool("shader-edit", Description = "Replace contained ShaderLab text after an exact revision check; dry-run unless apply is true.", Category = "shader", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Unsafe, SupportsDryRun = true)]
        public static ShaderWriteOutput ShaderEdit(ShaderEditInput input, UnityMcpContext context)
        {
            var fullPath = RequireProjectTextPath(input.path, ".shader", true);
            var assetPath = ToAssetPath(fullPath);
            var before = File.ReadAllBytes(fullPath);
            EnsureTextLength(before, MaxShaderBytes, "Shader source");
            var beforeRevision = Revision(before);
            if (string.IsNullOrWhiteSpace(input.expectedRevision) || !string.Equals(beforeRevision, input.expectedRevision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("expectedRevision does not match the current Shader source.");
            var hasBom = HasUtf8Bom(before);
            var after = EncodeUtf8(input.content ?? string.Empty, hasBom);
            EnsureTextLength(after, MaxShaderBytes, "Shader content");
            var changed = !before.SequenceEqual(after);
            if (!context.DryRun && changed)
            {
                File.WriteAllBytes(fullPath, after);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }
            var output = TextChange(context, assetPath, beforeRevision, Revision(after), "edit-shader", assetPath, assetPath, changed);
            output.changed = changed && !context.DryRun;
            return output;
        }

        [UnityMcpTool("lighting-settings-get", Description = "Read active-scene LightingSettings and current bake state without creating settings.", Category = "rendering", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static LightingSettingsOutput LightingSettingsGet(EmptyInput input)
        {
            var scene = SceneManager.GetActiveScene();
            return LightingOutput(scene, Lightmapping.GetLightingSettingsForScene(scene));
        }

        [UnityMcpTool("lighting-settings-set", Description = "Update active-scene LightingSettings; dry-run unless apply is true. A missing settings asset is created only on apply.", Category = "rendering", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ChangeOutput LightingSettingsSet(LightingSettingsSetInput input, UnityMcpContext context)
        {
            ValidateLightingInput(input);
            if (!HasLightingChanges(input)) throw new ArgumentException("Supply at least one lighting setting to change.");
            var scene = SceneManager.GetActiveScene();
            var settings = Lightmapping.GetLightingSettingsForScene(scene);
            var settingsPathBefore = settings == null ? null : AssetDatabase.GetAssetPath(settings);
            var createsSettings = settings == null;
            if (!context.DryRun)
            {
                if (settings == null)
                {
                    settings = new LightingSettings();
                    Undo.RegisterCreatedObjectUndo(settings, "UnityMCP Create Lighting Settings");
                    Lightmapping.SetLightingSettingsForScene(scene, settings);
                }
                Undo.RecordObject(settings, "UnityMCP Update Lighting Settings");
                ApplyLightingInput(settings, input);
                EditorUtility.SetDirty(settings);
                EditorSceneManager.MarkSceneDirty(scene);
            }
            return new ChangeOutput
            {
                dryRun = context.DryRun,
                changed = !context.DryRun,
                summary = "Update LightingSettings for active scene '" + scene.name + "'.",
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "set-lighting-settings", before = settingsPathBefore, after = createsSettings ? "created-for-active-scene" : AssetDatabase.GetAssetPath(settings) } }
            };
        }

        [UnityMcpTool("screenshot-gameobject", Description = "Capture Unity's generated preview image for a GameObject or Component as PNG.", Category = "visual", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static UnityMcpResult ScreenshotGameObject(ScreenshotGameObjectInput input)
        {
            var target = EditorUtility.InstanceIDToObject(input.instanceId);
            var gameObject = target as GameObject ?? (target as Component)?.gameObject;
            if (gameObject == null) throw new ArgumentException("instanceId must identify a GameObject or Component.");
            var width = Mathf.Clamp(input.width, 16, 2048);
            var height = Mathf.Clamp(input.height, 16, 2048);
            var preview = AssetPreview.GetAssetPreview(gameObject) ?? AssetPreview.GetMiniThumbnail(gameObject);
            if (preview == null) throw new InvalidOperationException("Unity has not generated a preview for this GameObject yet. Try again after the Editor has updated.");
            var renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            try
            {
                Graphics.Blit(preview, renderTexture);
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply(false, false);
                var data = texture.EncodeToPNG();
                var output = new ScreenshotGameObjectOutput { instanceId = gameObject.GetInstanceID(), name = gameObject.name, source = "unity-asset-preview", width = width, height = height };
                return new UnityMcpResult
                {
                    content = new List<UnityMcpContent> { new UnityMcpContent { type = "image", data = Convert.ToBase64String(data), mimeType = "image/png" } },
                    structuredContent = output
                };
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [UnityMcpTool("profiler-counters", Description = "Read a stable, bounded set of Unity memory profiler counters.", Category = "diagnostic", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static ProfilerCountersOutput ProfilerCounters(EmptyInput input) => new ProfilerCountersOutput
        {
            frame = Time.frameCount,
            profilerEnabled = Profiler.enabled,
            counters = ReadProfilerCounters()
        };

        [UnityMcpTool("profiler-frame-capture", Description = "Capture a point-in-time current-frame memory counter sample; this is not a full raw Profiler capture.", Category = "diagnostic", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static ProfilerFrameCaptureOutput ProfilerFrameCapture(EmptyInput input) => new ProfilerFrameCaptureOutput
        {
            captureKind = "current-frame-memory-counters",
            frame = Time.frameCount,
            editorTimeSinceStartup = EditorApplication.timeSinceStartup,
            profilerEnabled = Profiler.enabled,
            counters = ReadProfilerCounters()
        };

        [UnityMcpTool("frame-debugger-events", Description = "Read currently available Unity Frame Debugger events without enabling or changing the debugger.", Category = "diagnostic", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static FrameDebuggerEventsOutput FrameDebuggerEvents(FrameDebuggerEventsInput input)
        {
            if (input.startIndex < 0) throw new ArgumentOutOfRangeException(nameof(input.startIndex));
            var limit = Mathf.Clamp(input.limit, 1, 500);
            var utility = typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility", false);
            if (utility == null) return new FrameDebuggerEventsOutput { note = "Frame Debugger readback is unavailable in this Unity Editor build." };
            var output = new FrameDebuggerEventsOutput
            {
                supported = ReadStatic<bool>(utility, "locallySupported"),
                receivingRemoteFrameEventData = ReadStatic<bool>(utility, "receivingRemoteFrameEventData"),
                totalEvents = ReadStatic<int>(utility, "count"),
                eventsHash = ReadStatic<int>(utility, "eventsHash"),
                eventDataHash = ReadStatic<uint>(utility, "eventDataHash")
            };
            if (!output.supported)
            {
                output.note = "The current rendering target does not support Unity Frame Debugger readback.";
                return output;
            }
            var getEvents = utility.GetMethod("GetFrameEvents", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
            var getName = utility.GetMethod("GetFrameEventInfoName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(int) }, null);
            var getObject = utility.GetMethod("GetFrameEventObject", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(int) }, null);
            if (getEvents == null || getName == null || getObject == null)
            {
                output.note = "Frame Debugger readback API is incompatible with this Unity Editor build.";
                return output;
            }
            var events = getEvents.Invoke(null, null) as Array;
            if (events == null || events.Length == 0)
            {
                output.note = "No Frame Debugger event data is currently captured. Enable Frame Debugger in Unity first; this tool will not enable it.";
                return output;
            }
            var max = Math.Min(output.totalEvents, events.Length);
            for (var index = input.startIndex; index < max && output.events.Count < limit; index++)
            {
                var item = events.GetValue(index);
                if (item == null) continue;
                var typeField = item.GetType().GetField("m_Type", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var summary = new FrameDebuggerEventSummary { index = index, eventType = typeField == null ? null : Convert.ToString(typeField.GetValue(item)) };
                try { summary.name = getName.Invoke(null, new object[] { index }) as string; }
                catch { summary.name = "<unavailable>"; }
                if (input.includeObjectMetadata)
                {
                    try
                    {
                        var objectValue = getObject.Invoke(null, new object[] { index }) as UnityEngine.Object;
                        if (objectValue != null)
                        {
                            summary.objectInstanceId = objectValue.GetInstanceID();
                            summary.objectName = objectValue.name;
                            summary.objectType = objectValue.GetType().FullName;
                        }
                    }
                    catch { /* Frame Debugger may not expose an object for every event. */ }
                }
                output.events.Add(summary);
            }
            output.truncated = input.startIndex + output.events.Count < max;
            return output;
        }

        [UnityMcpTool("script-apply-structured-edits", Description = "Apply revision-checked lexical identifier renames outside comments and string literals; dry-run unless apply is true. This is not a semantic Roslyn rename.", Category = "scripts-compilation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static StructuredScriptWriteOutput ScriptApplyStructuredEdits(ScriptApplyStructuredEditsInput input, UnityMcpContext context)
        {
            var fullPath = RequireProjectTextPath(input.path, ".cs", true);
            var assetPath = ToAssetPath(fullPath);
            var before = File.ReadAllBytes(fullPath);
            EnsureTextLength(before, MaxStructuredScriptBytes, "Script source");
            var beforeRevision = Revision(before);
            if (string.IsNullOrWhiteSpace(input.expectedRevision) || !string.Equals(beforeRevision, input.expectedRevision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("expectedRevision does not match the current script contents.");
            var renames = ValidateStructuredRenames(input.edits);
            var source = DecodeUtf8(before);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var afterText = RenameIdentifierTokens(source, renames, counts);
            foreach (var pair in renames)
                if (!counts.TryGetValue(pair.Key, out var count) || count == 0) throw new InvalidOperationException("Identifier '" + pair.Key + "' was not found outside comments and string literals.");
            var after = EncodeUtf8(afterText, HasUtf8Bom(before));
            EnsureTextLength(after, MaxStructuredScriptBytes, "Edited script");
            var changed = !before.SequenceEqual(after);
            if (!context.DryRun && changed)
            {
                File.WriteAllBytes(fullPath, after);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }
            var output = new StructuredScriptWriteOutput
            {
                dryRun = context.DryRun,
                changed = changed && !context.DryRun,
                path = assetPath,
                revisionBefore = beforeRevision,
                revisionAfter = Revision(after),
                rollbackSupported = false,
                semantics = "Lexical identifier rename outside comments and string/character literals. It does not resolve symbols, partial classes, generated code, or references in other files.",
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "rename-identifiers", before = assetPath, after = assetPath } }
            };
            foreach (var pair in renames.OrderBy(value => value.Key, StringComparer.Ordinal))
                output.edits.Add(new StructuredScriptRenameResult { from = pair.Key, to = pair.Value, replacements = counts[pair.Key] });
            return output;
        }

        private static LightingSettingsOutput LightingOutput(Scene scene, LightingSettings settings)
        {
            return new LightingSettingsOutput
            {
                scene = scene.name,
                scenePath = scene.path,
                hasSettings = settings != null,
                settingsPath = settings == null ? null : AssetDatabase.GetAssetPath(settings),
                isBaking = Lightmapping.isRunning,
                bakeProgress = Lightmapping.buildProgress,
                bakedGi = settings == null ? Lightmapping.bakedGI : settings.bakedGI,
                realtimeGi = settings == null ? Lightmapping.realtimeGI : settings.realtimeGI,
                mixedBakeMode = settings == null ? null : settings.mixedBakeMode.ToString(),
                lightmapper = settings == null ? null : settings.lightmapper.ToString(),
                lightmapResolution = settings == null ? 0f : settings.lightmapResolution,
                lightmapMaxSize = settings == null ? 0 : settings.lightmapMaxSize,
                directSampleCount = settings == null ? 0 : settings.directSampleCount,
                indirectSampleCount = settings == null ? 0 : settings.indirectSampleCount,
                environmentSampleCount = settings == null ? 0 : settings.environmentSampleCount,
                indirectScale = settings == null ? 0f : settings.indirectScale
            };
        }

        private static bool HasLightingChanges(LightingSettingsSetInput input) => input.bakedGi.HasValue || input.realtimeGi.HasValue || !string.IsNullOrWhiteSpace(input.mixedBakeMode) || !string.IsNullOrWhiteSpace(input.lightmapper) || input.lightmapResolution.HasValue || input.lightmapMaxSize.HasValue || input.directSampleCount.HasValue || input.indirectSampleCount.HasValue || input.environmentSampleCount.HasValue || input.indirectScale.HasValue;

        private static void ValidateLightingInput(LightingSettingsSetInput input)
        {
            if (input.lightmapResolution.HasValue && (input.lightmapResolution.Value < 0.01f || input.lightmapResolution.Value > 1000f)) throw new ArgumentOutOfRangeException(nameof(input.lightmapResolution));
            if (input.lightmapMaxSize.HasValue && (input.lightmapMaxSize.Value < 32 || input.lightmapMaxSize.Value > 8192 || (input.lightmapMaxSize.Value & (input.lightmapMaxSize.Value - 1)) != 0)) throw new ArgumentException("lightmapMaxSize must be a power of two between 32 and 8192.");
            if (input.directSampleCount.HasValue && (input.directSampleCount.Value < 1 || input.directSampleCount.Value > 1000000)) throw new ArgumentOutOfRangeException(nameof(input.directSampleCount));
            if (input.indirectSampleCount.HasValue && (input.indirectSampleCount.Value < 1 || input.indirectSampleCount.Value > 1000000)) throw new ArgumentOutOfRangeException(nameof(input.indirectSampleCount));
            if (input.environmentSampleCount.HasValue && (input.environmentSampleCount.Value < 1 || input.environmentSampleCount.Value > 1000000)) throw new ArgumentOutOfRangeException(nameof(input.environmentSampleCount));
            if (input.indirectScale.HasValue && (input.indirectScale.Value < 0f || input.indirectScale.Value > 10f)) throw new ArgumentOutOfRangeException(nameof(input.indirectScale));
            if (!string.IsNullOrWhiteSpace(input.mixedBakeMode) && !Enum.TryParse(input.mixedBakeMode, true, out MixedLightingMode _)) throw new ArgumentException("mixedBakeMode is invalid.");
            if (!string.IsNullOrWhiteSpace(input.lightmapper) && !Enum.TryParse(input.lightmapper, true, out LightingSettings.Lightmapper _)) throw new ArgumentException("lightmapper is invalid.");
        }

        private static void ApplyLightingInput(LightingSettings settings, LightingSettingsSetInput input)
        {
            if (input.bakedGi.HasValue) settings.bakedGI = input.bakedGi.Value;
            if (input.realtimeGi.HasValue) settings.realtimeGI = input.realtimeGi.Value;
            if (!string.IsNullOrWhiteSpace(input.mixedBakeMode)) settings.mixedBakeMode = (MixedLightingMode)Enum.Parse(typeof(MixedLightingMode), input.mixedBakeMode, true);
            if (!string.IsNullOrWhiteSpace(input.lightmapper)) settings.lightmapper = (LightingSettings.Lightmapper)Enum.Parse(typeof(LightingSettings.Lightmapper), input.lightmapper, true);
            if (input.lightmapResolution.HasValue) settings.lightmapResolution = input.lightmapResolution.Value;
            if (input.lightmapMaxSize.HasValue) settings.lightmapMaxSize = input.lightmapMaxSize.Value;
            if (input.directSampleCount.HasValue) settings.directSampleCount = input.directSampleCount.Value;
            if (input.indirectSampleCount.HasValue) settings.indirectSampleCount = input.indirectSampleCount.Value;
            if (input.environmentSampleCount.HasValue) settings.environmentSampleCount = input.environmentSampleCount.Value;
            if (input.indirectScale.HasValue) settings.indirectScale = input.indirectScale.Value;
        }

        private static List<ProfilerCounter> ReadProfilerCounters() => new List<ProfilerCounter>
        {
            new ProfilerCounter { name = "total-allocated-memory", value = Profiler.GetTotalAllocatedMemoryLong() },
            new ProfilerCounter { name = "total-reserved-memory", value = Profiler.GetTotalReservedMemoryLong() },
            new ProfilerCounter { name = "total-unused-reserved-memory", value = Profiler.GetTotalUnusedReservedMemoryLong() },
            new ProfilerCounter { name = "graphics-driver-memory", value = Profiler.GetAllocatedMemoryForGraphicsDriver() },
            new ProfilerCounter { name = "mono-used-memory", value = Profiler.GetMonoUsedSizeLong() },
            new ProfilerCounter { name = "mono-heap-memory", value = Profiler.GetMonoHeapSizeLong() },
            new ProfilerCounter { name = "managed-gc-memory", value = GC.GetTotalMemory(false) }
        };

        private static T ReadStatic<T>(Type type, string name)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (property == null || !property.CanRead) return default(T);
            var value = property.GetValue(null, null);
            return value is T typed ? typed : default(T);
        }

        private static ShaderWriteOutput TextChange(UnityMcpContext context, string path, string before, string after, string operation, string journalBefore, string journalAfter, bool changed)
        {
            return new ShaderWriteOutput
            {
                dryRun = context.DryRun,
                changed = changed && !context.DryRun,
                path = path,
                revisionBefore = before,
                revisionAfter = after,
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = operation, before = journalBefore, after = journalAfter } }
            };
        }

        private static string RequireExistingProjectAsset(string path, string extension)
        {
            var full = RequireProjectTextPath(path, extension, true);
            var assetPath = ToAssetPath(full);
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null) throw new ArgumentException("Asset was not found: " + assetPath);
            return assetPath;
        }

        private static string RequireProjectTextPath(string path, string extension, bool mustExist)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required.");
            var normalized = path.Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) || normalized.IndexOf("..", StringComparison.Ordinal) >= 0 || Path.IsPathRooted(normalized))
                throw new ArgumentException("Path must be project-relative under Assets and may not contain '..'.");
            if (!string.IsNullOrEmpty(extension) && !normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Path must end with " + extension + ".");
            var assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var projectRoot = Directory.GetParent(assetsRoot).FullName;
            var full = Path.GetFullPath(Path.Combine(projectRoot, normalized));
            if (!IsUnder(assetsRoot, full)) throw new ArgumentException("Path must remain inside this project's Assets directory.");
            var parent = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(parent)) EnsureNoReparsePoint(assetsRoot, parent);
            if (mustExist && !File.Exists(full)) throw new ArgumentException("File was not found: " + normalized);
            return full;
        }

        private static bool IsUnder(string root, string candidate)
        {
            var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureNoReparsePoint(string root, string targetDirectory)
        {
            var current = targetDirectory;
            while (!string.IsNullOrEmpty(current) && IsUnder(root, current) && !string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("The target path may not traverse a reparse-point directory.");
                current = Path.GetDirectoryName(current);
            }
        }

        private static string ToAssetPath(string fullPath)
        {
            var assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!IsUnder(assetsRoot, fullPath) && !string.Equals(assetsRoot, fullPath, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Path is outside Assets.");
            var relative = fullPath.Substring(assetsRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
            return string.IsNullOrEmpty(relative) ? "Assets" : "Assets/" + relative;
        }

        private static void EnsureTextLength(byte[] bytes, int max, string name)
        {
            if (bytes == null || bytes.Length > max) throw new ArgumentException(name + " is limited to " + max + " bytes.");
        }

        private static bool HasUtf8Bom(byte[] bytes) => bytes != null && bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;

        private static string DecodeUtf8(byte[] bytes)
        {
            try
            {
                var offset = HasUtf8Bom(bytes) ? 3 : 0;
                return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
            }
            catch (DecoderFallbackException exception)
            {
                throw new ArgumentException("The source file must be valid UTF-8.", exception);
            }
        }

        private static byte[] EncodeUtf8(string text, bool withBom)
        {
            var payload = new UTF8Encoding(false, true).GetBytes(text ?? string.Empty);
            if (!withBom) return payload;
            var output = new byte[payload.Length + 3];
            output[0] = 0xef; output[1] = 0xbb; output[2] = 0xbf;
            Buffer.BlockCopy(payload, 0, output, 3, payload.Length);
            return output;
        }

        private static string Revision(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes ?? Array.Empty<byte>())).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string DefaultShaderSource(string shaderName, string fallbackName)
        {
            var name = string.IsNullOrWhiteSpace(shaderName) ? "Custom/" + fallbackName : shaderName.Trim();
            if (name.Length > 160 || name.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0) throw new ArgumentException("shaderName must be at most 160 characters and may not contain quotes or line breaks.");
            return "Shader \"" + name + "\"\n{\n    Properties { _Color (\"Color\", Color) = (1,1,1,1) }\n    SubShader\n    {\n        Tags { \"RenderType\"=\"Opaque\" }\n        Pass\n        {\n            CGPROGRAM\n            #pragma vertex vert\n            #pragma fragment frag\n            #include \"UnityCG.cginc\"\n            struct appdata { float4 vertex : POSITION; };\n            struct v2f { float4 pos : SV_POSITION; };\n            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }\n            fixed4 frag (v2f i) : SV_Target { return fixed4(1,1,1,1); }\n            ENDCG\n        }\n    }\n}\n";
        }

        private static Dictionary<string, string> ValidateStructuredRenames(List<StructuredScriptEdit> edits)
        {
            if (edits == null || edits.Count == 0) throw new ArgumentException("At least one structured rename is required.");
            if (edits.Count > MaxStructuredScriptEdits) throw new ArgumentException("Too many structured renames.");
            var renames = new Dictionary<string, string>(StringComparer.Ordinal);
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var edit in edits)
            {
                if (edit == null || !string.Equals(edit.kind, "rename-identifier", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Only kind 'rename-identifier' is supported.");
                if (!Identifier.IsMatch(edit.from ?? string.Empty) || !Identifier.IsMatch(edit.to ?? string.Empty)) throw new ArgumentException("Rename identifiers must use simple C# identifier syntax.");
                if (CSharpKeywords.Contains(edit.from) || CSharpKeywords.Contains(edit.to)) throw new ArgumentException("C# keywords cannot be renamed by this tool.");
                if (string.Equals(edit.from, edit.to, StringComparison.Ordinal)) throw new ArgumentException("A rename source and destination must differ.");
                if (renames.ContainsKey(edit.from)) throw new ArgumentException("Each rename source may only appear once.");
                renames.Add(edit.from, edit.to);
                if (!targets.Add(edit.to)) throw new ArgumentException("Each rename destination must be unique.");
            }
            if (renames.Keys.Any(targets.Contains)) throw new ArgumentException("Rename chains are not allowed in one call; use separate revision-checked calls.");
            return renames;
        }

        private static string RenameIdentifierTokens(string source, Dictionary<string, string> renames, Dictionary<string, int> counts)
        {
            var output = new StringBuilder(source.Length);
            for (var index = 0; index < source.Length;)
            {
                var current = source[index];
                if (current == '/' && index + 1 < source.Length && source[index + 1] == '/') { index = CopyLineComment(source, index, output); continue; }
                if (current == '/' && index + 1 < source.Length && source[index + 1] == '*') { index = CopyBlockComment(source, index, output); continue; }
                if (current == '\'') { index = CopyQuotedLiteral(source, index, output, false); continue; }
                if (current == '"' && index + 2 < source.Length && source[index + 1] == '"' && source[index + 2] == '"') { index = CopyRawString(source, index, output); continue; }
                if (current == '$' && StartsRawInterpolatedString(source, index, out var rawQuoteIndex))
                {
                    output.Append(source, index, rawQuoteIndex - index);
                    index = CopyRawString(source, rawQuoteIndex, output);
                    continue;
                }
                if (StartsStringLiteral(source, index, out var quoteIndex, out var verbatim))
                {
                    output.Append(source, index, quoteIndex - index);
                    index = CopyQuotedLiteral(source, quoteIndex, output, verbatim);
                    continue;
                }
                if (current == '@' && index + 1 < source.Length && IsIdentifierStart(source[index + 1]))
                {
                    output.Append(current);
                    index++;
                    index = CopyOrRenameIdentifier(source, index, output, renames, counts);
                    continue;
                }
                if (IsIdentifierStart(current)) { index = CopyOrRenameIdentifier(source, index, output, renames, counts); continue; }
                output.Append(current);
                index++;
            }
            return output.ToString();
        }

        private static bool StartsStringLiteral(string source, int index, out int quoteIndex, out bool verbatim)
        {
            quoteIndex = index;
            verbatim = false;
            if (source[index] == '"') return true;
            if (source[index] == '@' && index + 1 < source.Length && source[index + 1] == '"') { quoteIndex = index + 1; verbatim = true; return true; }
            if (source[index] == '$' && index + 1 < source.Length && source[index + 1] == '"') { quoteIndex = index + 1; return true; }
            if (index + 2 < source.Length && ((source[index] == '$' && source[index + 1] == '@') || (source[index] == '@' && source[index + 1] == '$')) && source[index + 2] == '"') { quoteIndex = index + 2; verbatim = true; return true; }
            return false;
        }

        private static bool StartsRawInterpolatedString(string source, int index, out int quoteIndex)
        {
            quoteIndex = index;
            if (source[index] != '$') return false;
            while (quoteIndex < source.Length && source[quoteIndex] == '$') quoteIndex++;
            return quoteIndex + 2 < source.Length && source[quoteIndex] == '"' && source[quoteIndex + 1] == '"' && source[quoteIndex + 2] == '"';
        }

        private static int CopyLineComment(string source, int index, StringBuilder output)
        {
            var end = source.IndexOf('\n', index + 2);
            if (end < 0) end = source.Length;
            output.Append(source, index, end - index);
            return end;
        }

        private static int CopyBlockComment(string source, int index, StringBuilder output)
        {
            var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
            end = end < 0 ? source.Length : end + 2;
            output.Append(source, index, end - index);
            return end;
        }

        private static int CopyQuotedLiteral(string source, int quoteIndex, StringBuilder output, bool verbatim)
        {
            var quote = source[quoteIndex];
            var index = quoteIndex + 1;
            output.Append(quote);
            while (index < source.Length)
            {
                var current = source[index++];
                output.Append(current);
                if (!verbatim && current == '\\' && index < source.Length) { output.Append(source[index++]); continue; }
                if (current != quote) continue;
                if (verbatim && quote == '"' && index < source.Length && source[index] == '"') { output.Append(source[index++]); continue; }
                return index;
            }
            return index;
        }

        private static int CopyRawString(string source, int index, StringBuilder output)
        {
            var delimiterLength = 0;
            while (index + delimiterLength < source.Length && source[index + delimiterLength] == '"') delimiterLength++;
            var scan = index + delimiterLength;
            while (scan < source.Length)
            {
                if (source[scan] != '"') { scan++; continue; }
                var quoteCount = 0;
                while (scan + quoteCount < source.Length && source[scan + quoteCount] == '"') quoteCount++;
                if (quoteCount >= delimiterLength)
                {
                    var end = scan + delimiterLength;
                    output.Append(source, index, end - index);
                    return end;
                }
                scan += quoteCount;
            }
            output.Append(source, index, source.Length - index);
            return source.Length;
        }

        private static int CopyOrRenameIdentifier(string source, int index, StringBuilder output, Dictionary<string, string> renames, Dictionary<string, int> counts)
        {
            var end = index + 1;
            while (end < source.Length && IsIdentifierPart(source[end])) end++;
            var identifier = source.Substring(index, end - index);
            if (renames.TryGetValue(identifier, out var replacement))
            {
                output.Append(replacement);
                counts[identifier] = counts.TryGetValue(identifier, out var count) ? count + 1 : 1;
            }
            else output.Append(identifier);
            return end;
        }

        private static bool IsIdentifierStart(char value) => value == '_' || char.IsLetter(value);
        private static bool IsIdentifierPart(char value) => value == '_' || char.IsLetterOrDigit(value);
    }
}
