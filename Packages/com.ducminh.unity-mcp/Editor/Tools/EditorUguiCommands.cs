using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable]
    public sealed class UiRaycastInput
    {
        public Vector2 screenPosition;
        public int? cameraInstanceId;
        public int? rootInstanceId;
        public bool includeInactive;
        public int limit = 50;
    }

    [Serializable]
    public sealed class UiRaycastHit
    {
        public int instanceId;
        public string name;
        public string hierarchyPath;
        public int hierarchyDepth;
        public bool active;
    }

    [Serializable]
    public sealed class UiRaycastOutput
    {
        public List<UiRaycastHit> hits = new List<UiRaycastHit>();
        public bool truncated;
    }

    /// <summary>
    /// Geometry-based UGUI RectTransform raycast commands.
    /// </summary>
    public static class EditorUguiCommands
    {
        private const string Group = "ugui";

        [CliGroupCommand(
            Group,
            "Return Canvas RectTransforms containing a screen point.",
            Name = "raycast",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "ui", "ugui" })]
        internal static ExitCode RaycastCommand(
            CommandContext cmd,
            [CliArg("screen-position", "Screen point in pixels as 'x,y', '[x,y]', or '{\"x\":0,\"y\":0}'.", Required = true)] string screenPosition = null,
            [CliArg("camera-instance-id", "Optional Camera instance ID for Screen Space - Camera or World Space Canvases.")] int? cameraInstanceId = null,
            [CliArg("root-instance-id", "Optional root UI GameObject instance ID to scope hits.")] int? rootInstanceId = null,
            [CliArg("include-inactive", "Include inactive RectTransforms (default false).")] bool includeInactive = false,
            [CliArg("limit", "Maximum hits to return (1-200, default 50).")] int limit = 50)
        {
            try
            {
                var result = Raycast(new UiRaycastInput
                {
                    screenPosition = ParseVector2(screenPosition, "screen-position"),
                    cameraInstanceId = cameraInstanceId,
                    rootInstanceId = rootInstanceId,
                    includeInactive = includeInactive,
                    limit = limit
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
            catch (JsonException ex)
            {
                cmd.Log.Error(ex.Message);
                return ExitCode.InvalidUsage;
            }
            catch (Exception ex)
            {
                cmd.Log.Error(ex.Message);
                return ExitCode.Failed;
            }
        }

        internal static UiRaycastOutput Raycast(UiRaycastInput input)
        {
            if (input == null)
                throw new ArgumentException("input is required.");

            var camera = input.cameraInstanceId.HasValue ? RequireCamera(input.cameraInstanceId.Value) : null;
            var root = input.rootInstanceId.HasValue ? RequireSceneGameObject(input.rootInstanceId.Value) : null;
            var clampedLimit = Math.Max(1, Math.Min(200, input.limit));
            var candidates = new List<RectTransform>();

            foreach (var scene in LoadedScenes())
            {
                foreach (var rootObject in scene.GetRootGameObjects())
                    candidates.AddRange(rootObject.GetComponentsInChildren<RectTransform>(input.includeInactive));
            }

            var output = new UiRaycastOutput();
            foreach (var rect in candidates.Where(value => value != null).Distinct())
            {
                if (!input.includeInactive && !rect.gameObject.activeInHierarchy)
                    continue;
                if (root != null && rect.gameObject != root && !rect.IsChildOf(root.transform))
                    continue;
                if (rect.GetComponentInParent<Canvas>() == null)
                    continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(rect, input.screenPosition, camera))
                    continue;

                output.hits.Add(new UiRaycastHit
                {
                    instanceId = UnityMcpObjectId.Get(rect.gameObject),
                    name = rect.gameObject.name,
                    hierarchyPath = RuntimeCommandHelpers.HierarchyPath(rect.gameObject),
                    hierarchyDepth = HierarchyDepth(rect),
                    active = rect.gameObject.activeInHierarchy
                });
            }

            var orderedHits = output.hits
                .OrderByDescending(hit => hit.hierarchyDepth)
                .ThenByDescending(hit => SiblingIndex(hit.instanceId))
                .ToList();
            output.truncated = orderedHits.Count > clampedLimit;
            output.hits = orderedHits.Take(clampedLimit).ToList();
            return output;
        }

        internal static Vector2 ParseVector2(string value, string paramName = "vector")
        {
            var label = string.IsNullOrWhiteSpace(paramName) ? "vector" : paramName;
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"'{label}' is required.");

            var trimmed = value.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                JObject obj;
                try
                {
                    obj = JToken.Parse(trimmed) as JObject;
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"'{label}' is not valid JSON: {ex.Message}", ex);
                }

                if (obj == null)
                    throw new ArgumentException($"'{label}' must be a JSON object.");

                if (!obj.TryGetValue("x", StringComparison.OrdinalIgnoreCase, out _) &&
                    !obj.TryGetValue("y", StringComparison.OrdinalIgnoreCase, out _))
                {
                    throw new ArgumentException($"'{label}' JSON object must specify at least one of x, y.");
                }

                return new Vector2(ReadFloat(obj, "x", label), ReadFloat(obj, "y", label));
            }

            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                JArray array;
                try
                {
                    array = JArray.Parse(trimmed);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"'{label}' is not a valid JSON array: {ex.Message}", ex);
                }

                if (array.Count != 2)
                    throw new ArgumentException($"'{label}' must contain 2 numbers.");

                return new Vector2(ReadArrayFloat(array[0], label, 0), ReadArrayFloat(array[1], label, 1));
            }

            var parts = trimmed.Split(',');
            if (parts.Length != 2)
                throw new ArgumentException($"'{label}' must contain 2 comma-separated numbers.");

            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                throw new ArgumentException($"'{label}' contains invalid numeric components.");
            }

            return new Vector2(RequireFinite(x, label), RequireFinite(y, label));
        }

        private static float ReadFloat(JObject obj, string key, string label)
        {
            if (!obj.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var token) || token.Type == JTokenType.Null)
                return 0f;
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                throw new ArgumentException($"'{label}.{key}' must be a finite number.");
            return RequireFinite(token.Value<float>(), label);
        }

        private static float ReadArrayFloat(JToken token, string label, int index)
        {
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                throw new ArgumentException($"'{label}[{index}]' must be a finite number.");
            return RequireFinite(token.Value<float>(), label);
        }

        private static float RequireFinite(float value, string label)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException($"'{label}' must contain only finite numbers.");
            return value;
        }

        private static GameObject RequireSceneGameObject(int instanceId)
        {
            var value = UnityMcpEditorObjectId.Resolve(instanceId);
            var gameObject = value as GameObject ?? (value as Component)?.gameObject;
            if (gameObject == null || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded)
                throw new ArgumentException("A loaded scene GameObject was not found for instanceId.");
            return gameObject;
        }

        private static Camera RequireCamera(int instanceId)
        {
            var gameObject = RequireSceneGameObject(instanceId);
            var camera = gameObject.GetComponent<Camera>();
            if (camera == null)
                throw new ArgumentException("instanceId does not identify a GameObject with a Camera component.");
            return camera;
        }

        private static IEnumerable<Scene> LoadedScenes()
        {
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene.IsValid() && scene.isLoaded)
                    yield return scene;
            }
        }

        private static int HierarchyDepth(Transform transform)
        {
            var depth = 0;
            for (var current = transform.parent; current != null; current = current.parent)
                depth++;
            return depth;
        }

        private static int SiblingIndex(int instanceId)
        {
            var gameObject = UnityMcpEditorObjectId.Resolve(instanceId) as GameObject;
            return gameObject == null ? 0 : gameObject.transform.GetSiblingIndex();
        }
    }
}
