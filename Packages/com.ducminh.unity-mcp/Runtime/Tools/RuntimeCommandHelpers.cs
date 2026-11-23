using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DucMinh.UnityMcp
{
    public sealed class RuntimeVector3JsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Vector3) || objectType == typeof(Vector3?);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var isNullable = Nullable.GetUnderlyingType(objectType) != null;
            if (reader.TokenType == JsonToken.Null)
            {
                if (isNullable) return null;
                throw new ArgumentException("Vector3 value cannot be null.");
            }

            if (reader.TokenType == JsonToken.String)
            {
                var raw = (string)reader.Value;
                if (isNullable && string.IsNullOrWhiteSpace(raw))
                    return null;
                return RuntimeCommandHelpers.ParseVector3(raw, "vector");
            }

            var token = JToken.Load(reader);
            return RuntimeCommandHelpers.ParseVector3Token(token, "vector");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var vector = (Vector3)value;
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(vector.x);
            writer.WritePropertyName("y");
            writer.WriteValue(vector.y);
            writer.WritePropertyName("z");
            writer.WriteValue(vector.z);
            writer.WriteEndObject();
        }
    }

    public sealed class RuntimeColorJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Color) || objectType == typeof(Color?);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var isNullable = Nullable.GetUnderlyingType(objectType) != null;
            if (reader.TokenType == JsonToken.Null)
            {
                if (isNullable) return null;
                throw new ArgumentException("Color value cannot be null.");
            }

            if (reader.TokenType == JsonToken.String)
            {
                var raw = (string)reader.Value;
                if (isNullable && string.IsNullOrWhiteSpace(raw))
                    return null;
                return RuntimeCommandHelpers.ParseColorNullable(raw, "color")
                    ?? throw new ArgumentException("'color' is required.");
            }

            var token = JToken.Load(reader);
            return RuntimeCommandHelpers.ParseColorToken(token, "color");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var color = (Color)value;
            writer.WriteStartObject();
            writer.WritePropertyName("r");
            writer.WriteValue(color.r);
            writer.WritePropertyName("g");
            writer.WriteValue(color.g);
            writer.WritePropertyName("b");
            writer.WriteValue(color.b);
            writer.WritePropertyName("a");
            writer.WriteValue(color.a);
            writer.WriteEndObject();
        }
    }

    [Serializable]
    public sealed class GameObjectSummary
    {
        public int instanceId;
        public string name;
        public string path;
        public string scene;
        public bool activeSelf;
        public bool activeInHierarchy;
    }

    [Serializable]
    public sealed class RuntimeChangeResult
    {
        public bool dryRun;
        public bool changed;
        public string summary;
        public int? instanceId;
        public string hierarchyPath;
        public bool rollbackSupported;
    }

    /// <summary>
    /// Shared scene-object lookup, DTO, and CLI/JSON value-parsing helpers for runtime commands.
    /// Keeps dependencies restricted to UnityEngine and Newtonsoft.Json so Release Player builds compile cleanly.
    /// </summary>
    public static class RuntimeCommandHelpers
    {
        public static string HierarchyPath(GameObject gameObject)
        {
            if (gameObject == null) return null;
            return gameObject.scene.name + ":/" + TransformPath(gameObject.transform);
        }

        public static IEnumerable<GameObject> AllSceneObjects()
        {
            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var item in Traverse(root))
                        yield return item;
            }
        }

        public static IEnumerable<GameObject> Traverse(GameObject root)
        {
            if (root == null) yield break;
            yield return root;
            var transform = root.transform;
            for (var index = 0; index < transform.childCount; index++)
                foreach (var child in Traverse(transform.GetChild(index).gameObject))
                    yield return child;
        }

        public static GameObject RequireGameObject(int? instanceId, string hierarchyPath)
        {
            GameObject byInstanceId = null;
            GameObject byPath = null;

            if (instanceId.HasValue)
            {
                byInstanceId = AllSceneObjects().FirstOrDefault(go => UnityMcpObjectId.Get(go) == instanceId.Value);
                if (byInstanceId == null)
                    throw new ArgumentException("GameObject instanceId was not found.");
            }

            if (!string.IsNullOrWhiteSpace(hierarchyPath))
            {
                var trimmed = hierarchyPath.Trim();
                var matches = FindByHierarchyPath(trimmed);
                if (matches.Length == 0)
                    throw new ArgumentException("GameObject hierarchy path was not found.");
                if (matches.Length > 1)
                    throw new ArgumentException("GameObject hierarchy path is ambiguous; use a unique path or instanceId without path.");
                byPath = matches[0];
            }

            if (byInstanceId != null && byPath != null && byInstanceId != byPath)
                throw new ArgumentException("instanceId and hierarchyPath must identify the same GameObject.");

            var result = byInstanceId ?? byPath;
            if (result == null)
                throw new ArgumentException("GameObject was not found; supply a valid instanceId or full hierarchy path.");

            return result;
        }

        public static GameObjectSummary Summarize(GameObject gameObject)
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            return new GameObjectSummary
            {
                instanceId = UnityMcpObjectId.Get(gameObject),
                name = gameObject.name,
                path = HierarchyPath(gameObject),
                scene = gameObject.scene.name,
                activeSelf = gameObject.activeSelf,
                activeInHierarchy = gameObject.activeInHierarchy
            };
        }

        public static RuntimeChangeResult Change(bool dryRun, string summary, GameObject target = null, bool rollbackSupported = false)
        {
            return new RuntimeChangeResult
            {
                dryRun = dryRun,
                changed = !dryRun,
                summary = summary,
                instanceId = target == null ? (int?)null : UnityMcpObjectId.Get(target),
                hierarchyPath = target == null ? null : HierarchyPath(target),
                rollbackSupported = rollbackSupported
            };
        }

        internal static ExitCode Execute<T>(CommandContext cmd, Func<T> action)
        {
            try
            {
                var result = action();
                cmd.Result.Write(result);
                return ExitCode.Success;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException exception)
            {
                cmd.Log.Error(exception.Message);
                return IsNotFoundError(exception) ? ExitCode.NotFound : ExitCode.InvalidUsage;
            }
            catch (InvalidOperationException exception)
            {
                cmd.Log.Error(exception.Message);
                return ExitCode.Failed;
            }
            catch (Exception exception)
            {
                cmd.Log.Error(exception.Message);
                return ExitCode.Failed;
            }
        }

        private static bool IsNotFoundError(ArgumentException exception)
        {
            if (exception is ArgumentNullException || exception is ArgumentOutOfRangeException)
                return false;

            var message = exception.Message ?? string.Empty;
            if (message.IndexOf("supply a valid", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            return message.IndexOf("was not found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("has no ", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static Vector3 ParseVector3(string value, string paramName = "vector")
        {
            var label = string.IsNullOrWhiteSpace(paramName) ? "vector" : paramName;
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"'{label}' is required.");

            var trimmed = value.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
                return ParseVector3Object(ParseJsonObject(trimmed, label), label);

            var components = ParseFloatSequence(trimmed, label, 3, 3);
            return new Vector3(components[0], components[1], components[2]);
        }

        public static Vector3? ParseVector3Nullable(string value, string paramName = "vector")
        {
            return string.IsNullOrWhiteSpace(value) ? (Vector3?)null : ParseVector3(value, paramName);
        }

        internal static Vector3 ParseVector3Token(JToken token, string paramName = "vector")
        {
            var label = string.IsNullOrWhiteSpace(paramName) ? "vector" : paramName;
            if (token is JObject obj)
                return ParseVector3Object(obj, label);
            if (token is JArray array)
            {
                var components = ParseFloatArray(array, label, 3, 3);
                return new Vector3(components[0], components[1], components[2]);
            }
            if (token != null && token.Type == JTokenType.String)
                return ParseVector3(token.Value<string>(), label);

            throw new ArgumentException($"'{label}' must be a Vector3 object, array, or string.");
        }

        public static Quaternion? ParseQuaternionNullable(string value, string paramName = "quaternion")
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var label = string.IsNullOrWhiteSpace(paramName) ? "quaternion" : paramName;
            var trimmed = value.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                var obj = ParseJsonObject(trimmed, label);
                if (!HasAnyProperty(obj, "x", "y", "z", "w"))
                    throw new ArgumentException($"'{label}' JSON object must specify quaternion components x, y, z, w.");

                return new Quaternion(
                    ReadObjectFloat(obj, "x", 0f, label),
                    ReadObjectFloat(obj, "y", 0f, label),
                    ReadObjectFloat(obj, "z", 0f, label),
                    ReadObjectFloat(obj, "w", 1f, label));
            }

            var components = ParseFloatSequence(trimmed, label, 4, 4);
            return new Quaternion(components[0], components[1], components[2], components[3]);
        }

        public static Color? ParseColorNullable(string value, string paramName = "color")
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var label = string.IsNullOrWhiteSpace(paramName) ? "color" : paramName;
            var trimmed = value.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
                return ParseColorObject(ParseJsonObject(trimmed, label), label);

            if (trimmed.StartsWith("[", StringComparison.Ordinal) || trimmed.IndexOf(',') >= 0)
            {
                var components = ParseFloatSequence(trimmed, label, 3, 4);
                var alpha = components.Length == 4 ? components[3] : 1f;
                return new Color(components[0], components[1], components[2], alpha);
            }

            if (ColorUtility.TryParseHtmlString(trimmed, out var htmlColor))
                return htmlColor;

            throw new ArgumentException($"'{label}' must be a hex/named color, [r,g,b(,a)], r,g,b(,a), or {{r,g,b,a}}.");
        }

        internal static Color ParseColorToken(JToken token, string paramName = "color")
        {
            var label = string.IsNullOrWhiteSpace(paramName) ? "color" : paramName;
            if (token is JObject obj)
                return ParseColorObject(obj, label);
            if (token is JArray array)
            {
                var components = ParseFloatArray(array, label, 3, 4);
                var alpha = components.Length == 4 ? components[3] : 1f;
                return new Color(components[0], components[1], components[2], alpha);
            }
            if (token != null && token.Type == JTokenType.String)
                return ParseColorNullable(token.Value<string>(), label)
                    ?? throw new ArgumentException($"'{label}' is required.");

            throw new ArgumentException($"'{label}' must be a Color object, array, or hex/named string.");
        }

        private static Vector3 ParseVector3Object(JObject obj, string label)
        {
            if (!HasAnyProperty(obj, "x", "y", "z"))
                throw new ArgumentException($"'{label}' JSON object must specify at least one of x, y, z.");

            return new Vector3(
                ReadObjectFloat(obj, "x", 0f, label),
                ReadObjectFloat(obj, "y", 0f, label),
                ReadObjectFloat(obj, "z", 0f, label));
        }

        private static Color ParseColorObject(JObject obj, string label)
        {
            if (!HasAnyProperty(obj, "r", "g", "b", "a"))
                throw new ArgumentException($"'{label}' JSON object must specify color components r, g, b, a.");

            return new Color(
                ReadObjectFloat(obj, "r", 0f, label),
                ReadObjectFloat(obj, "g", 0f, label),
                ReadObjectFloat(obj, "b", 0f, label),
                ReadObjectFloat(obj, "a", 1f, label));
        }

        private static GameObject[] FindByHierarchyPath(string path)
        {
            var exact = AllSceneObjects()
                .Where(go => string.Equals(HierarchyPath(go), path, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (exact.Length > 0 || path.Contains(":/"))
                return exact;

            var normalized = "/" + path.TrimStart('/');
            return AllSceneObjects()
                .Where(go => string.Equals("/" + TransformPath(go.transform), normalized, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
        }

        private static string TransformPath(Transform transform)
        {
            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
                names.Push(current.name);
            return string.Join("/", names.ToArray());
        }

        private static JObject ParseJsonObject(string json, string label)
        {
            try
            {
                var token = JToken.Parse(json);
                if (token is JObject obj) return obj;
            }
            catch (Exception exception)
            {
                throw new ArgumentException($"'{label}' is not valid JSON: {exception.Message}", exception);
            }

            throw new ArgumentException($"'{label}' must be a JSON object.");
        }

        private static bool HasAnyProperty(JObject obj, params string[] names)
        {
            return names.Any(name => obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out _));
        }

        private static float ReadObjectFloat(JObject obj, string key, float fallback, string label)
        {
            if (!obj.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var token) || token.Type == JTokenType.Null)
                return fallback;

            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                throw new ArgumentException($"'{label}.{key}' must be a finite number.");

            return RequireFinite(token.Value<float>(), label);
        }

        private static float[] ParseFloatSequence(string trimmed, string label, int minCount, int maxCount)
        {
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                JArray array;
                try
                {
                    array = JArray.Parse(trimmed);
                }
                catch (Exception exception)
                {
                    throw new ArgumentException($"'{label}' is not a valid JSON array: {exception.Message}", exception);
                }

                return ParseFloatArray(array, label, minCount, maxCount);
            }

            var parts = trimmed.Split(',');
            if (parts.Length < minCount || parts.Length > maxCount)
                throw new ArgumentException($"'{label}' must contain {ExpectationCount(minCount, maxCount)} comma-separated numbers.");

            var result = new float[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw new ArgumentException($"'{label}' contains invalid number '{parts[i].Trim()}'.");
                result[i] = RequireFinite(number, label);
            }

            return result;
        }

        private static float[] ParseFloatArray(JArray array, string label, int minCount, int maxCount)
        {
            if (array.Count < minCount || array.Count > maxCount)
                throw new ArgumentException($"'{label}' must contain {ExpectationCount(minCount, maxCount)} numbers.");

            var result = new float[array.Count];
            for (var i = 0; i < array.Count; i++)
            {
                var item = array[i];
                if (item.Type != JTokenType.Integer && item.Type != JTokenType.Float)
                    throw new ArgumentException($"'{label}[{i}]' must be a finite number.");
                result[i] = RequireFinite(item.Value<float>(), label);
            }

            return result;
        }

        private static string ExpectationCount(int minCount, int maxCount)
        {
            return minCount == maxCount ? minCount.ToString(CultureInfo.InvariantCulture) : $"{minCount} or {maxCount}";
        }

        internal static float RequireFinite(float value, string label)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException($"'{label}' must contain only finite numbers.");
            return value;
        }

        internal static Vector3 RequireFinite(Vector3 value, string label)
        {
            RequireFinite(value.x, label);
            RequireFinite(value.y, label);
            RequireFinite(value.z, label);
            return value;
        }
    }
}
