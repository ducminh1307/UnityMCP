#pragma warning disable UAC0005 // Frame Debugger readback uses reflection on Unity's internal FrameDebuggerUtility.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable]
    public sealed class CaptureGameObjectInput
    {
        public int? instanceId;
        public string path;
        public int width = 512;
        public int height = 512;
        public string outputPath;
        public bool includeInlineImage;
    }

    [Serializable]
    public sealed class CaptureGameObjectOutput
    {
        public int instanceId;
        public string name;
        public string source;
        public int width;
        public int height;
        public string encoding = "png";
        public int bytes;
        public string savedPath;
        [JsonProperty("base64", NullValueHandling = NullValueHandling.Ignore)]
        public string base64;
    }

    [Serializable]
    public sealed class CaptureMultiviewInput
    {
        public List<int> cameraInstanceIds = new List<int>();
        public int? width;
        public int? height;
        public bool includeAlpha;
        public string outputDir;
        public bool includeInlineImage;
    }

    [Serializable]
    public sealed class CaptureMultiviewItem
    {
        public int cameraInstanceId;
        public string cameraName;
        public int width;
        public int height;
        public string encoding = "png";
        public int bytes;
        public string source;
        public string savedPath;
        [JsonProperty("base64", NullValueHandling = NullValueHandling.Ignore)]
        public string base64;
    }

    [Serializable]
    public sealed class CaptureMultiviewOutput
    {
        public List<CaptureMultiviewItem> screenshots = new List<CaptureMultiviewItem>();
    }

    [Serializable]
    public sealed class FrameDebuggerEventsInput
    {
        public int startIndex;
        public int limit = 100;
        public bool includeObjectMetadata = true;
    }

    [Serializable]
    public sealed class FrameDebuggerEventSummary
    {
        public int index;
        public string eventType;
        public string name;
        public int? objectInstanceId;
        public string objectName;
        public string objectType;
    }

    [Serializable]
    public sealed class FrameDebuggerEventsOutput
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

    /// <summary>
    /// Visual QA capture and Frame Debugger inspection commands.
    /// </summary>
    public static class EditorVisualQaCommands
    {
        private const string Group = "visual_ext";

        private const long ScreenshotResponseLimitBytes = 16L * 1024L * 1024L;
        private const long ScreenshotJsonReserveBytes = 512L * 1024L;
        private const long PngOverheadReservePerImageBytes = 64L * 1024L;
        private const long MetadataReservePerImageBytes = 4096L;
        private const int MaxScreenshotCameraNameCharacters = 256;

        [CliGroupCommand(
            Group,
            "Capture Unity's generated preview image for a GameObject or Component as PNG.",
            Name = "capture_gameobject",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "visual", "capture" })]
        internal static ExitCode CaptureGameObjectCommand(
            CommandContext cmd,
            [CliArg("instance-id", "Instance ID of the target GameObject or Component.")] int? instanceId = null,
            [CliArg("path", "Hierarchy path or Assets/... prefab path of the target GameObject.")] string path = null,
            [CliArg("width", "Output width in pixels (16-2048, default 512).")] int width = 512,
            [CliArg("height", "Output height in pixels (16-2048, default 512).")] int height = 512,
            [CliArg("output-path", "Optional PNG output path. When omitted, saves under Temp/unity-mcp-captures/ and includes base64.")] string outputPath = null,
            [CliArg("include-inline-image", "Also include base64 PNG data when output-path is specified.")] bool includeInlineImage = false)
        {
            return Execute(cmd, () => CaptureGameObject(new CaptureGameObjectInput
            {
                instanceId = instanceId,
                path = path,
                width = width,
                height = height,
                outputPath = outputPath,
                includeInlineImage = includeInlineImage
            }));
        }

        [CliGroupCommand(
            Group,
            "Capture several loaded Cameras as PNG images.",
            Name = "capture_multiview",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "visual", "capture" })]
        internal static ExitCode CaptureMultiviewCommand(
            CommandContext cmd,
            [CliArg("camera-instance-ids", "Comma-separated list or JSON array of 1-8 Camera (or Camera GameObject) instance IDs.", Required = true)] string cameraInstanceIds = null,
            [CliArg("width", "Optional output width in pixels (16-2048; defaults to Game View width).")] int? width = null,
            [CliArg("height", "Optional output height in pixels (16-2048; defaults to Game View height).")] int? height = null,
            [CliArg("include-alpha", "Use RGBA32 instead of RGB24 (default false).")] bool includeAlpha = false,
            [CliArg("output-dir", "Optional directory for per-camera PNG files. When omitted, saves under Temp/unity-mcp-captures/ and includes base64.")] string outputDir = null,
            [CliArg("include-inline-image", "Also include base64 PNG data when output-dir is specified.")] bool includeInlineImage = false)
        {
            return Execute(cmd, () => CaptureMultiview(new CaptureMultiviewInput
            {
                cameraInstanceIds = ParseInstanceIds(cameraInstanceIds),
                width = width,
                height = height,
                includeAlpha = includeAlpha,
                outputDir = outputDir,
                includeInlineImage = includeInlineImage
            }));
        }

        [CliGroupCommand(
            Group,
            "Read currently available Unity Frame Debugger events without enabling or changing the debugger.",
            Name = "frame_debugger_events",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "visual", "diagnostics" })]
        internal static ExitCode FrameDebuggerEventsCommand(
            CommandContext cmd,
            [CliArg("start-index", "Zero-based starting event index (default 0).")] int startIndex = 0,
            [CliArg("limit", "Maximum events to return (1-500, default 100).")] int limit = 100,
            [CliArg("include-object-metadata", "Include resolved Unity object metadata per event (default true).")] bool includeObjectMetadata = true)
        {
            return Execute(cmd, () => GetFrameDebuggerEvents(new FrameDebuggerEventsInput
            {
                startIndex = startIndex,
                limit = limit,
                includeObjectMetadata = includeObjectMetadata
            }));
        }

        internal static CaptureGameObjectOutput CaptureGameObject(CaptureGameObjectInput input)
        {
            if (input == null)
                throw new ArgumentException("input is required.");

            var gameObject = ResolveTargetGameObject(input.instanceId, input.path);
            var width = Mathf.Clamp(input.width, 16, 2048);
            var height = Mathf.Clamp(input.height, 16, 2048);
            var preview = AssetPreview.GetAssetPreview(gameObject) ?? AssetPreview.GetMiniThumbnail(gameObject);
            if (preview == null)
                throw new InvalidOperationException("Unity has not generated a preview for this GameObject yet. Try again after the Editor has updated.");

            var renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            byte[] png;
            try
            {
                Graphics.Blit(preview, renderTexture);
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply(false, false);
                png = texture.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
                UnityEngine.Object.DestroyImmediate(texture);
            }

            var captured = EditorCommandHelpers.WriteCapturedPng(
                png,
                width,
                height,
                "unity-asset-preview",
                input.outputPath,
                "unity-mcp-captures",
                input.includeInlineImage);

            return new CaptureGameObjectOutput
            {
                instanceId = UnityMcpObjectId.Get(gameObject),
                name = gameObject.name,
                source = captured.source,
                width = captured.width,
                height = captured.height,
                encoding = captured.encoding,
                bytes = captured.bytes,
                savedPath = captured.savedPath,
                base64 = captured.base64
            };
        }

        internal static CaptureMultiviewOutput CaptureMultiview(CaptureMultiviewInput input)
        {
            if (input == null)
                throw new ArgumentException("input is required.");

            if (input.cameraInstanceIds == null || input.cameraInstanceIds.Count == 0 || input.cameraInstanceIds.Count > 8)
                throw new ArgumentException("cameraInstanceIds must contain between 1 and 8 cameras.");

            var cameraIds = input.cameraInstanceIds.Distinct().ToArray();
            var size = ResolveScreenshotSize(input.width, input.height);
            var clampedWidth = Mathf.Clamp(size.x, 16, 2048);
            var clampedHeight = Mathf.Clamp(size.y, 16, 2048);

            var hasOutputDir = !string.IsNullOrWhiteSpace(input.outputDir);
            if (!hasOutputDir || input.includeInlineImage)
                ValidateScreenshotCaptureBudget(cameraIds.Length, clampedWidth, clampedHeight);

            var cameras = cameraIds.Select(id => ResolveLoadedCamera(
                id,
                "One or more cameraInstanceIds do not identify loaded Camera components or loaded scene GameObjects with Camera components.")).ToArray();

            var output = new CaptureMultiviewOutput();
            for (var index = 0; index < cameras.Length; index++)
            {
                var camera = cameras[index];
                var png = RenderCameraToPng(camera, clampedWidth, clampedHeight, input.includeAlpha);
                var cameraName = camera.name ?? string.Empty;
                if (cameraName.Length > MaxScreenshotCameraNameCharacters)
                    cameraName = cameraName.Substring(0, MaxScreenshotCameraNameCharacters);

                var perCameraOutputPath = hasOutputDir
                    ? BuildPerCameraOutputPath(input.outputDir, index, cameraName)
                    : null;

                var captured = EditorCommandHelpers.WriteCapturedPng(
                    png,
                    clampedWidth,
                    clampedHeight,
                    $"camera:{cameraName}",
                    perCameraOutputPath,
                    "unity-mcp-captures",
                    input.includeInlineImage);

                output.screenshots.Add(new CaptureMultiviewItem
                {
                    cameraInstanceId = UnityMcpObjectId.Get(camera),
                    cameraName = cameraName,
                    width = captured.width,
                    height = captured.height,
                    encoding = captured.encoding,
                    bytes = captured.bytes,
                    source = captured.source,
                    savedPath = captured.savedPath,
                    base64 = captured.base64
                });
            }

            return output;
        }

        internal static FrameDebuggerEventsOutput GetFrameDebuggerEvents(FrameDebuggerEventsInput input)
        {
            if (input == null)
                input = new FrameDebuggerEventsInput();

            if (input.startIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(input.startIndex));

            var limit = Mathf.Clamp(input.limit, 1, 500);
            var utility = typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility", false);
            if (utility == null)
                return new FrameDebuggerEventsOutput { note = "Frame Debugger readback is unavailable in this Unity Editor build." };

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
                if (item == null)
                    continue;

                var typeField = item.GetType().GetField("m_Type", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var summary = new FrameDebuggerEventSummary
                {
                    index = index,
                    eventType = typeField == null ? null : Convert.ToString(typeField.GetValue(item))
                };

                try
                {
                    summary.name = getName.Invoke(null, new object[] { index }) as string;
                }
                catch
                {
                    summary.name = "<unavailable>";
                }

                if (input.includeObjectMetadata)
                {
                    try
                    {
                        var objectValue = getObject.Invoke(null, new object[] { index }) as UnityEngine.Object;
                        if (objectValue != null)
                        {
                            summary.objectInstanceId = UnityMcpObjectId.Get(objectValue);
                            summary.objectName = objectValue.name;
                            summary.objectType = objectValue.GetType().FullName;
                        }
                    }
                    catch
                    {
                        // Frame Debugger may not expose an object for every event.
                    }
                }

                output.events.Add(summary);
            }

            output.truncated = input.startIndex + output.events.Count < max;
            return output;
        }

        internal static Camera ResolveLoadedCamera(int instanceId, string errorMessage)
        {
            var target = UnityMcpEditorObjectId.Resolve(instanceId);
            var camera = target as Camera ?? (target as GameObject)?.GetComponent<Camera>();
            if (camera == null || !camera.gameObject.scene.IsValid())
                throw new ArgumentException(errorMessage);
            return camera;
        }

        internal static void ValidateScreenshotCaptureBudget(int cameraCount, int width, int height)
        {
            if (cameraCount < 1 || cameraCount > 8)
                throw new ArgumentOutOfRangeException(nameof(cameraCount), "cameraCount must be between 1 and 8.");

            var clampedWidth = Mathf.Clamp(width, 16, 2048);
            var clampedHeight = Mathf.Clamp(height, 16, 2048);
            var pixelsPerImage = checked((long)clampedWidth * clampedHeight);

            // A PNG scanline needs four color bytes per pixel plus one filter byte.
            var worstCasePngBytes = checked(pixelsPerImage * 5L + PngOverheadReservePerImageBytes);
            var worstCaseBase64Bytes = checked(((worstCasePngBytes + 2L) / 3L) * 4L);
            var estimatedResponseBytes = checked(
                ScreenshotJsonReserveBytes + cameraCount * (worstCaseBase64Bytes + MetadataReservePerImageBytes));
            if (estimatedResponseBytes > ScreenshotResponseLimitBytes)
                throw new ArgumentException("The requested screenshot capture can exceed the 16 MiB encoded response budget; reduce camera count or dimensions.");
        }

        internal static Vector2Int ResolveScreenshotSize(int? requestedWidth, int? requestedHeight)
        {
            var gameViewSize = Handles.GetMainGameViewSize();
            var gameViewWidth = Mathf.RoundToInt(gameViewSize.x);
            var gameViewHeight = Mathf.RoundToInt(gameViewSize.y);
            return new Vector2Int(
                requestedWidth ?? (gameViewWidth > 0 ? gameViewWidth : 1280),
                requestedHeight ?? (gameViewHeight > 0 ? gameViewHeight : 720));
        }

        private static GameObject ResolveTargetGameObject(int? instanceId, string path)
        {
            GameObject byInstanceId = null;
            if (instanceId.HasValue)
            {
                var target = UnityMcpEditorObjectId.Resolve(instanceId.Value);
                byInstanceId = target as GameObject ?? (target as Component)?.gameObject;
                if (byInstanceId == null)
                    throw new ArgumentException("instanceId must identify a GameObject or Component.");
            }

            GameObject byPath = null;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var trimmed = path.Trim();
                if (trimmed.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal))
                {
                    var assetPath = EditorCommandHelpers.ValidateExistingAsset(trimmed);
                    byPath = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    if (byPath == null)
                        throw new ArgumentException("Asset path must identify a GameObject prefab/model asset.");
                }
                else
                {
                    byPath = RuntimeCommandHelpers.RequireGameObject(null, trimmed);
                }
            }

            if (byInstanceId != null && byPath != null && byInstanceId != byPath)
                throw new ArgumentException("instanceId and path must identify the same GameObject.");

            var result = byInstanceId ?? byPath;
            if (result == null)
                throw new ArgumentException("Provide a valid instanceId or path.");

            return result;
        }

        private static byte[] RenderCameraToPng(Camera camera, int width, int height, bool includeAlpha)
        {
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var texture = new Texture2D(width, height, includeAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false, false);
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply(false, false);
                return texture.EncodeToPNG();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static List<int> ParseInstanceIds(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException("cameraInstanceIds is required.");

            var trimmed = raw.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                try
                {
                    var array = JArray.Parse(trimmed);
                    var ids = new List<int>(array.Count);
                    foreach (var token in array)
                    {
                        if (token.Type != JTokenType.Integer)
                            throw new ArgumentException("cameraInstanceIds JSON array must contain only integers.");
                        ids.Add(token.Value<int>());
                    }
                    return ids;
                }
                catch (JsonException ex)
                {
                    throw new ArgumentException($"cameraInstanceIds is not valid JSON: {ex.Message}", ex);
                }
            }

            var result = new List<int>();
            foreach (var part in trimmed.Split(','))
            {
                var segment = part.Trim();
                if (string.IsNullOrEmpty(segment))
                    continue;
                if (!int.TryParse(segment, out var id))
                    throw new ArgumentException($"Invalid camera instance ID '{segment}'.");
                result.Add(id);
            }

            return result;
        }

        private static string BuildPerCameraOutputPath(string outputDir, int index, string cameraName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var safeName = new string((cameraName ?? "camera")
                .Select(c => invalidChars.Contains(c) ? '_' : c)
                .ToArray()).Trim();
            if (string.IsNullOrEmpty(safeName))
                safeName = "camera";

            return Path.Combine(outputDir.Trim(), $"view-{index + 1}-{safeName}.png");
        }

        private static T ReadStatic<T>(Type type, string name)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (property == null || !property.CanRead)
                return default;
            var value = property.GetValue(null, null);
            return value is T typed ? typed : default;
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
