using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable]
    public sealed class UnityMcpCSharpCommandRule
    {
        [Tooltip("Stable MCP-facing command identifier, for example rebuild-game-data.")]
        public string commandName;
        [Tooltip("Exact fully-qualified or assembly-qualified type name of a project command class.")]
        public string typeName;
        [Tooltip("Exact public static method name.")]
        public string methodName;
        [Tooltip("Exact input parameter type names, in order. Do not list a final UnityMcpContext parameter; it is supplied by the bridge.")]
        public List<string> parameterTypeNames = new List<string>();
    }

    [Serializable] public sealed class ExecuteCSharpInput { public string allowlistPath; public string command; public string argumentsJson = "{}"; public bool apply; }
    [Serializable] public sealed class ExecuteCSharpOutput
    {
        public bool dryRun;
        public bool invoked;
        public string command;
        public string declaringType;
        public string method;
        public string returnType;
        public string returnJson;
        public string summary;
    }

    /// <summary>
    /// A deliberately narrow alternative to arbitrary C# execution. It never compiles source,
    /// never accepts a type or method from the MCP caller, and only invokes an exact command that
    /// a local developer listed in <see cref="UnityMcpCSharpCommandAllowlist"/>.
    /// </summary>
    public static class EditorExecuteCSharpAllowlistTool
    {
        private const int MaxArgumentCharacters = 32768;
        private const int MaxResultCharacters = 32768;
        private const int MaxInputParameters = 8;
        private const int MaxCollectionItems = 128;
        private const int MaxObjectMembers = 32;
        private const int MaxTypeDepth = 6;
        private const int MaxJsonDepth = 12;
        private static readonly Regex CommandName = new Regex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);
        private static readonly string[] ForbiddenTargetNamespaces =
        {
            "System.Diagnostics", "System.Net", "System.Reflection", "System.IO", "Microsoft.Win32"
        };

        [UnityMcpTool("execute-csharp", Description = "Invoke one exact, developer-reviewed public static project command from a local C# command allowlist; dry-run unless apply is true. This tool never compiles or executes source strings.", Category = "custom", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Unsafe, SupportsDryRun = true, TimeoutMs = 60000)]
        public static ExecuteCSharpOutput ExecuteCSharp(ExecuteCSharpInput input, UnityMcpContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (context == null) throw new ArgumentNullException(nameof(context));
            var allowlist = LoadAllowlist(input.allowlistPath, out var allowlistPath);
            var command = RequireCommand(allowlist, input.command);
            var method = ResolveCommandMethod(command);
            var dataParameters = method.GetParameters().Where(parameter => parameter.ParameterType != typeof(UnityMcpContext)).ToArray();
            var arguments = BindArguments(input.argumentsJson, dataParameters);
            var output = new ExecuteCSharpOutput
            {
                dryRun = context.DryRun,
                command = command.commandName,
                declaringType = method.DeclaringType.FullName,
                method = MethodSignature(method),
                returnType = FriendlyTypeName(method.ReturnType),
                summary = "Invoke locally allowlisted C# command '" + command.commandName + "'."
            };

            // A dry run validates the exact allowlist rule and JSON binding but intentionally
            // never calls project code. An arbitrary command cannot be trusted to honor a
            // dry-run convention, so avoiding the invocation is the only reliable guarantee.
            if (context.DryRun) return output;

            var invocation = new object[method.GetParameters().Length];
            var inputIndex = 0;
            for (var index = 0; index < invocation.Length; index++)
            {
                if (method.GetParameters()[index].ParameterType == typeof(UnityMcpContext)) invocation[index] = context;
                else invocation[index] = arguments[inputIndex++];
            }
            var result = method.Invoke(null, invocation);
            output.invoked = true;
            if (method.ReturnType != typeof(void)) output.returnJson = SerializeResult(result, method.ReturnType);
            return output;
        }

        private static UnityMcpCSharpCommandAllowlist LoadAllowlist(string path, out string normalizedPath)
        {
            normalizedPath = NormalizeAllowlistPath(path);
            var allowlist = AssetDatabase.LoadAssetAtPath<UnityMcpCSharpCommandAllowlist>(normalizedPath);
            if (allowlist == null)
                throw new ArgumentException("allowlistPath must identify an existing UnityMcpCSharpCommandAllowlist asset under Assets/.");
            return allowlist;
        }

        private static UnityMcpCSharpCommandRule RequireCommand(UnityMcpCSharpCommandAllowlist allowlist, string requestedName)
        {
            if (!CommandName.IsMatch(requestedName ?? string.Empty))
                throw new ArgumentException("command must be a lower-case hyphenated command identifier.");
            var all = allowlist.commands ?? new List<UnityMcpCSharpCommandRule>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in all)
            {
                if (rule == null || !CommandName.IsMatch(rule.commandName ?? string.Empty))
                    throw new ArgumentException("The C# command allowlist contains an invalid command rule.");
                if (!names.Add(rule.commandName))
                    throw new ArgumentException("The C# command allowlist contains duplicate command '" + rule.commandName + "'.");
            }
            var matches = all.Where(rule => rule != null && string.Equals(rule.commandName, requestedName, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
                throw new ArgumentException("The requested command is not explicitly listed in the supplied local C# command allowlist.");
            return matches[0];
        }

        private static MethodInfo ResolveCommandMethod(UnityMcpCSharpCommandRule rule)
        {
            if (string.IsNullOrWhiteSpace(rule.typeName) || string.IsNullOrWhiteSpace(rule.methodName))
                throw new ArgumentException("The C# command allowlist rule must include exact typeName and methodName values.");
            var targetType = ResolveType(rule.typeName);
            if (targetType == null || !IsProjectCommandType(targetType))
                throw new ArgumentException("The C# command target must be a public, non-generic project type compiled from source under Assets/.");
            if (IsForbiddenTargetNamespace(targetType.Namespace))
                throw new ArgumentException("The C# command target may not be in a process, network, file-system, registry, or reflection namespace.");
            var configuredParameters = (rule.parameterTypeNames ?? new List<string>()).Select(ResolveConfiguredValueType).ToArray();
            var matches = targetType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => string.Equals(method.Name, rule.methodName, StringComparison.Ordinal) && !method.IsSpecialName && !method.ContainsGenericParameters)
                .Where(method => IsSupportedCommandSignature(method, configuredParameters))
                .ToArray();
            if (matches.Length != 1)
                throw new ArgumentException("The C# command allowlist rule must resolve to exactly one public synchronous static method with the configured safe parameter types.");
            return matches[0];
        }

        private static bool IsSupportedCommandSignature(MethodInfo method, Type[] configuredParameters)
        {
            if (method.ReturnType != typeof(void) && !IsSafeValueType(method.ReturnType, new HashSet<Type>(), 0)) return false;
            var parameters = method.GetParameters();
            var hasContext = parameters.Length > 0 && parameters[parameters.Length - 1].ParameterType == typeof(UnityMcpContext);
            if (parameters.Any(parameter => parameter.ParameterType == typeof(UnityMcpContext)) && !hasContext) return false;
            var data = hasContext ? parameters.Take(parameters.Length - 1).ToArray() : parameters;
            if (data.Length > MaxInputParameters || data.Length != configuredParameters.Length) return false;
            for (var index = 0; index < data.Length; index++)
            {
                var parameter = data[index];
                if (parameter.IsOut || parameter.ParameterType.IsByRef || parameter.ParameterType != configuredParameters[index]) return false;
                if (!IsSafeValueType(parameter.ParameterType, new HashSet<Type>(), 0)) return false;
            }
            return true;
        }

        private static Type ResolveConfiguredValueType(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("C# command parameter type names must be explicit and non-empty.");
            var type = ResolveType(name);
            if (type == null || !IsSafeValueType(type, new HashSet<Type>(), 0))
                throw new ArgumentException("A C# command allowlist parameter type is unavailable or not a safe serializable value/DTO type.");
            return type;
        }

        private static object[] BindArguments(string argumentsJson, ParameterInfo[] parameters)
        {
            var text = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson;
            if (text.Length > MaxArgumentCharacters) throw new ArgumentException("argumentsJson exceeds the 32 KiB limit.");
            JToken token;
            try { token = JToken.Parse(text); }
            catch (JsonException exception) { throw new ArgumentException("argumentsJson must be valid JSON.", exception); }
            ValidateJsonShape(token, 0);
            if (parameters.Length == 0)
            {
                if (token.Type != JTokenType.Null && (!(token is JObject objectToken) || objectToken.Properties().Any()))
                    throw new ArgumentException("A command with no input parameters requires argumentsJson to be {} or null.");
                return Array.Empty<object>();
            }
            var values = parameters.Length == 1 ? new[] { token } : RequireArgumentArray(token, parameters.Length);
            var result = new object[parameters.Length];
            for (var index = 0; index < parameters.Length; index++)
            {
                ValidateValueToken(values[index], parameters[index].ParameterType, new HashSet<Type>(), 0);
                try { result[index] = values[index].ToObject(parameters[index].ParameterType, CreateSerializer()); }
                catch (Exception exception) when (exception is JsonException || exception is InvalidCastException || exception is ArgumentException)
                {
                    throw new ArgumentException("argumentsJson does not match the allowlisted parameter type '" + FriendlyTypeName(parameters[index].ParameterType) + "'.", exception);
                }
            }
            return result;
        }

        private static JToken[] RequireArgumentArray(JToken token, int count)
        {
            if (!(token is JArray array) || array.Count != count)
                throw new ArgumentException("A command with multiple parameters requires argumentsJson to be a JSON array with exactly " + count + " entries.");
            return array.ToArray();
        }

        private static JsonSerializer CreateSerializer()
        {
            var serializer = JsonSerializer.CreateDefault(new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.None,
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Error,
                FloatParseHandling = FloatParseHandling.Decimal
            });
            serializer.Converters.Add(new StringEnumConverter());
            serializer.Converters.Add(StrictUnityValueJsonConverter.Instance);
            return serializer;
        }

        private static string SerializeResult(object value, Type type)
        {
            string json;
            try { json = JToken.FromObject(value, CreateSerializer()).ToString(Formatting.None); }
            catch (Exception exception) when (exception is JsonException || exception is ArgumentException || exception is InvalidOperationException)
            {
                throw new InvalidOperationException("The allowlisted command returned a value that cannot be safely serialized.", exception);
            }
            if (json.Length > MaxResultCharacters) throw new InvalidOperationException("The allowlisted command result exceeds the 32 KiB result limit.");
            return json;
        }

        private static bool IsSafeValueType(Type rawType, HashSet<Type> stack, int depth)
        {
            if (rawType == null || depth > MaxTypeDepth) return false;
            var type = Nullable.GetUnderlyingType(rawType) ?? rawType;
            if (type == typeof(string) || type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte) ||
                type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
                type == typeof(long) || type == typeof(ulong) || type == typeof(float) || type == typeof(double) ||
                type == typeof(decimal) || type == typeof(Guid) || type.IsEnum || IsUnityValueType(type)) return true;
            if (type == typeof(object) || typeof(JToken).IsAssignableFrom(type) || typeof(Delegate).IsAssignableFrom(type) ||
                typeof(MemberInfo).IsAssignableFrom(type) || type == typeof(Type) || typeof(UnityEngine.Object).IsAssignableFrom(type) ||
                type.IsPointer || type.IsByRef || type.IsInterface || type.IsAbstract || type.ContainsGenericParameters) return false;
            if (type.IsArray) return type.GetArrayRank() == 1 && IsSafeValueType(type.GetElementType(), stack, depth + 1);
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return IsSafeValueType(type.GetGenericArguments()[0], stack, depth + 1);
            if (!IsProjectDtoType(type) || !stack.Add(type)) return false;
            try
            {
                var fields = SerializableFields(type).ToArray();
                if (fields.Length > MaxObjectMembers || type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Any()) return false;
                return fields.All(field => IsSafeValueType(field.FieldType, stack, depth + 1));
            }
            finally { stack.Remove(type); }
        }

        private static bool IsProjectDtoType(Type type)
        {
            if (!type.IsPublic || type.IsNested || type.IsGenericType || IsForbiddenTargetNamespace(type.Namespace)) return false;
            if (!type.IsDefined(typeof(SerializableAttribute), false) || !IsProjectAssembly(type.Assembly)) return false;
            return type.IsValueType || type.GetConstructor(Type.EmptyTypes) != null;
        }

        private static IEnumerable<FieldInfo> SerializableFields(Type type)
        {
            return type.GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => !field.IsStatic && !field.IsNotSerialized)
                .OrderBy(field => field.Name, StringComparer.Ordinal);
        }

        private static bool IsUnityValueType(Type type)
        {
            return type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) ||
                   type == typeof(Quaternion) || type == typeof(Color) || type == typeof(Rect);
        }

        private static void ValidateJsonShape(JToken token, int depth)
        {
            if (depth > MaxJsonDepth) throw new ArgumentException("argumentsJson exceeds the maximum nesting depth.");
            if (token is JObject obj)
            {
                if (obj.Count > MaxObjectMembers) throw new ArgumentException("argumentsJson contains too many object members.");
                foreach (var property in obj.Properties())
                {
                    if (property.Name.StartsWith("$", StringComparison.Ordinal)) throw new ArgumentException("argumentsJson may not contain JSON metadata properties.");
                    ValidateJsonShape(property.Value, depth + 1);
                }
            }
            else if (token is JArray array)
            {
                if (array.Count > MaxCollectionItems) throw new ArgumentException("argumentsJson contains too many collection items.");
                foreach (var item in array) ValidateJsonShape(item, depth + 1);
            }
            else if (token.Type == JTokenType.String && (token.Value<string>() ?? string.Empty).Length > MaxArgumentCharacters)
                throw new ArgumentException("argumentsJson contains an oversized string.");
        }

        private static void ValidateValueToken(JToken token, Type rawType, HashSet<Type> stack, int depth)
        {
            if (depth > MaxTypeDepth) throw new ArgumentException("argumentsJson nesting does not match the allowlisted value type.");
            var nullable = Nullable.GetUnderlyingType(rawType);
            var type = nullable ?? rawType;
            if (token.Type == JTokenType.Null)
            {
                if (rawType.IsValueType && nullable == null) throw new ArgumentException("A non-nullable command parameter cannot be null.");
                return;
            }
            if (type == typeof(string) || type == typeof(Guid)) { RequireTokenType(token, JTokenType.String, type); return; }
            if (type == typeof(bool)) { RequireTokenType(token, JTokenType.Boolean, type); return; }
            if (IsInteger(type)) { RequireTokenType(token, JTokenType.Integer, type); return; }
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            {
                if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) throw new ArgumentException("Expected a numeric value for " + FriendlyTypeName(type) + ".");
                return;
            }
            if (type.IsEnum)
            {
                RequireTokenType(token, JTokenType.String, type);
                if (!Enum.GetNames(type).Contains(token.Value<string>(), StringComparer.Ordinal)) throw new ArgumentException("The enum value is not declared by " + FriendlyTypeName(type) + ".");
                return;
            }
            if (IsUnityValueType(type)) { StrictUnityValueJsonConverter.ValidateToken(token, type); return; }
            if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
            {
                if (!(token is JArray array) || array.Count > MaxCollectionItems) throw new ArgumentException("Expected a bounded JSON array for " + FriendlyTypeName(type) + ".");
                var element = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                foreach (var item in array) ValidateValueToken(item, element, stack, depth + 1);
                return;
            }
            if (!stack.Add(type)) throw new ArgumentException("Recursive DTO value types are not supported by C# command binding.");
            try
            {
                if (!(token is JObject obj)) throw new ArgumentException("Expected a JSON object for DTO type " + FriendlyTypeName(type) + ".");
                var fields = SerializableFields(type).ToDictionary(field => field.Name, StringComparer.Ordinal);
                foreach (var property in obj.Properties())
                {
                    if (!fields.TryGetValue(property.Name, out var field)) throw new ArgumentException("argumentsJson contains member '" + property.Name + "' which is not a public serializable field of " + FriendlyTypeName(type) + ".");
                    ValidateValueToken(property.Value, field.FieldType, stack, depth + 1);
                }
            }
            finally { stack.Remove(type); }
        }

        private static void RequireTokenType(JToken token, JTokenType expected, Type type)
        {
            if (token.Type != expected) throw new ArgumentException("Expected a " + expected.ToString().ToLowerInvariant() + " JSON value for " + FriendlyTypeName(type) + ".");
        }

        private static bool IsInteger(Type type)
        {
            return type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
                   type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);
        }

        private static bool IsProjectCommandType(Type type)
        {
            return type.IsClass && type.IsPublic && !type.IsNested && !type.IsAbstract && !type.IsGenericType && IsProjectAssembly(type.Assembly);
        }

        private static bool IsProjectAssembly(System.Reflection.Assembly assembly)
        {
            if (assembly == null) return false;
            var assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            try
            {
                return CompilationPipeline.GetAssemblies().Any(candidate =>
                    string.Equals(candidate.name, assembly.GetName().Name, StringComparison.Ordinal) &&
                    (candidate.sourceFiles ?? Array.Empty<string>()).Any(path => IsAssetSourcePath(path, assetsRoot)));
            }
            catch { return false; }
        }

        private static bool IsAssetSourcePath(string sourcePath, string assetsRoot)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return false;
            try
            {
                var fullPath = Path.GetFullPath(sourcePath);
                return fullPath.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                       fullPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool IsForbiddenTargetNamespace(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return ForbiddenTargetNamespaces.Any(prefix => value.Equals(prefix, StringComparison.Ordinal) || value.StartsWith(prefix + ".", StringComparison.Ordinal));
        }

        private static Type ResolveType(string name)
        {
            var direct = Type.GetType(name, false);
            if (direct != null) return direct;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(name, false);
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }

        private static string NormalizeAllowlistPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("allowlistPath is required.");
            var normalized = path.Trim().Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) || !normalized.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(":") || normalized.Split('/').Any(part => string.IsNullOrEmpty(part) || part == "." || part == ".."))
                throw new ArgumentException("allowlistPath must be a normalized project-relative Assets/*.asset path.");
            var assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var projectRoot = Directory.GetParent(assetsRoot)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) throw new InvalidOperationException("Could not determine the Unity project root.");
            var fullPath = Path.GetFullPath(Path.Combine(projectRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
                throw new ArgumentException("allowlistPath must identify an existing asset under Assets/.");
            EnsureNoReparsePoint(assetsRoot, fullPath);
            return normalized;
        }

        private static void EnsureNoReparsePoint(string root, string fullPath)
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("The Assets root may not be a symbolic link or junction.");
            var relative = fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = root;
            foreach (var part in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("allowlistPath may not traverse symbolic links or junctions.");
            }
        }

        private static string MethodSignature(MethodInfo method)
        {
            var parts = method.GetParameters().Select(parameter => FriendlyTypeName(parameter.ParameterType)).ToArray();
            return method.Name + "(" + string.Join(", ", parts) + ")";
        }

        private static string FriendlyTypeName(Type type)
        {
            var nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null) return FriendlyTypeName(nullable) + "?";
            return type.FullName ?? type.Name;
        }

        private sealed class StrictUnityValueJsonConverter : JsonConverter
        {
            public static readonly StrictUnityValueJsonConverter Instance = new StrictUnityValueJsonConverter();

            public override bool CanConvert(Type objectType) => IsUnityValueType(objectType);

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var token = JObject.Load(reader);
                ValidateToken(token, objectType);
                var values = token.Properties().ToDictionary(property => property.Name, property => property.Value.Value<float>(), StringComparer.Ordinal);
                if (objectType == typeof(Vector2)) return new Vector2(values["x"], values["y"]);
                if (objectType == typeof(Vector3)) return new Vector3(values["x"], values["y"], values["z"]);
                if (objectType == typeof(Vector4)) return new Vector4(values["x"], values["y"], values["z"], values["w"]);
                if (objectType == typeof(Quaternion)) return new Quaternion(values["x"], values["y"], values["z"], values["w"]);
                if (objectType == typeof(Color)) return new Color(values["r"], values["g"], values["b"], values["a"]);
                if (objectType == typeof(Rect)) return new Rect(values["x"], values["y"], values["width"], values["height"]);
                throw new JsonSerializationException("Unsupported Unity value type.");
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                if (value is Vector2 vector2) { Write(writer, "x", vector2.x); Write(writer, "y", vector2.y); }
                else if (value is Vector3 vector3) { Write(writer, "x", vector3.x); Write(writer, "y", vector3.y); Write(writer, "z", vector3.z); }
                else if (value is Vector4 vector4) { Write(writer, "x", vector4.x); Write(writer, "y", vector4.y); Write(writer, "z", vector4.z); Write(writer, "w", vector4.w); }
                else if (value is Quaternion quaternion) { Write(writer, "x", quaternion.x); Write(writer, "y", quaternion.y); Write(writer, "z", quaternion.z); Write(writer, "w", quaternion.w); }
                else if (value is Color color) { Write(writer, "r", color.r); Write(writer, "g", color.g); Write(writer, "b", color.b); Write(writer, "a", color.a); }
                else if (value is Rect rect) { Write(writer, "x", rect.x); Write(writer, "y", rect.y); Write(writer, "width", rect.width); Write(writer, "height", rect.height); }
                writer.WriteEndObject();
            }

            public static void ValidateToken(JToken token, Type type)
            {
                if (!(token is JObject obj)) throw new ArgumentException("Expected a JSON object for " + FriendlyTypeName(type) + ".");
                var names = UnityValueMemberNames(type);
                if (obj.Properties().Any(property => !names.Contains(property.Name, StringComparer.Ordinal)) || names.Any(name => obj[name] == null))
                    throw new ArgumentException("The JSON object for " + FriendlyTypeName(type) + " must contain exactly " + string.Join(", ", names) + ".");
                foreach (var name in names)
                {
                    var value = obj[name];
                    if ((value.Type != JTokenType.Integer && value.Type != JTokenType.Float) || !IsFinite(value.Value<float>()))
                        throw new ArgumentException("Unity value member '" + name + "' must be finite numeric data.");
                }
            }

            private static string[] UnityValueMemberNames(Type type)
            {
                if (type == typeof(Vector2)) return new[] { "x", "y" };
                if (type == typeof(Vector3)) return new[] { "x", "y", "z" };
                if (type == typeof(Vector4) || type == typeof(Quaternion)) return new[] { "x", "y", "z", "w" };
                if (type == typeof(Color)) return new[] { "r", "g", "b", "a" };
                if (type == typeof(Rect)) return new[] { "x", "y", "width", "height" };
                throw new ArgumentException("Unsupported Unity value type.");
            }

            private static void Write(JsonWriter writer, string name, float value)
            {
                if (!IsFinite(value)) throw new JsonSerializationException("UnityMCP cannot serialize non-finite Unity values.");
                writer.WritePropertyName(name);
                writer.WriteValue(value);
            }

            private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
