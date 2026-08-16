using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DucMinh.UnityMcp
{
    public interface IUnityMcpEnablementStore
    {
        bool? GetOverride(string toolName);
        void SetOverride(string toolName, bool enabled);
    }

    internal sealed class UnityMcpMemoryEnablementStore : IUnityMcpEnablementStore
    {
        private readonly Dictionary<string, bool> values = new Dictionary<string, bool>(StringComparer.Ordinal);
        public bool? GetOverride(string toolName) => values.TryGetValue(toolName, out var value) ? value : (bool?)null;
        public void SetOverride(string toolName, bool enabled) => values[toolName] = enabled;
    }

    internal sealed class UnityMcpToolBinding
    {
        public MethodInfo Method;
        public UnityMcpToolAttribute Attribute;
        public UnityMcpToolDescriptor Descriptor;
    }

    public sealed class UnityMcpRegistry
    {
        private static readonly Regex ValidName = new Regex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);
        private readonly object gate = new object();
        private readonly Dictionary<string, UnityMcpToolBinding> bindings = new Dictionary<string, UnityMcpToolBinding>(StringComparer.Ordinal);
        private readonly UnityMcpScope activeScope;
        private IUnityMcpEnablementStore enablement;
        private string revision = Guid.NewGuid().ToString("N");

        public UnityMcpRegistry(UnityMcpScope activeScope, IUnityMcpEnablementStore enablement = null)
        {
            this.activeScope = activeScope;
            this.enablement = enablement ?? new UnityMcpMemoryEnablementStore();
        }

        public static Func<IEnumerable<MethodInfo>> DiscoveryOverride { get; set; }
        public string RegistryRevision { get { lock (gate) return revision; } }
        public event Action Changed;

        public IReadOnlyList<UnityMcpToolDescriptor> Tools
        {
            get { lock (gate) return bindings.Values.Select(v => v.Descriptor).OrderBy(v => v.name, StringComparer.Ordinal).ToArray(); }
        }

        public void SetEnablementStore(IUnityMcpEnablementStore store)
        {
            enablement = store ?? throw new ArgumentNullException(nameof(store));
            Reload();
        }

        public void SetEnabled(string toolName, bool enabled)
        {
            SetEnabled(new[] { toolName }, enabled);
        }

        public void SetEnabled(IEnumerable<string> toolNames, bool enabled)
        {
            if (toolNames == null) throw new ArgumentNullException(nameof(toolNames));
            var names = toolNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).ToArray();
            if (names.Length == 0) return;
            foreach (var toolName in names) enablement.SetOverride(toolName, enabled);
            lock (gate)
            {
                foreach (var toolName in names)
                    if (bindings.TryGetValue(toolName, out var binding)) binding.Descriptor.enabled = enabled;
                revision = Guid.NewGuid().ToString("N");
            }
            Changed?.Invoke();
        }

        public void Reload()
        {
            var methods = (DiscoveryOverride?.Invoke() ?? DiscoverByReflection()).Distinct()
                .OrderByDescending(IsBuiltIn).ThenBy(m => m.DeclaringType?.FullName).ThenBy(m => m.Name).ToArray();
            var next = new Dictionary<string, UnityMcpToolBinding>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in methods)
            {
                var attribute = method.GetCustomAttribute<UnityMcpToolAttribute>(false);
                if (attribute == null || (attribute.Scope & activeScope) == 0) continue;
                if (!IsRequiredTypeAvailable(attribute.RequiredType)) continue;
                var builtIn = IsBuiltIn(method);
                if (!method.IsStatic || method.ContainsGenericParameters || !ValidName.IsMatch(attribute.Name) || (!builtIn && !method.IsPublic))
                {
                    Debug.LogWarning($"UnityMCP ignored invalid tool method {method.DeclaringType?.FullName}.{method.Name}.");
                    continue;
                }
                if (next.TryGetValue(attribute.Name, out var existing))
                {
                    if (IsBuiltIn(existing.Method) && !builtIn)
                    {
                        Debug.LogError($"UnityMCP project tool '{attribute.Name}' collides with a reserved built-in name and was ignored.");
                        continue;
                    }
                    Debug.LogError($"UnityMCP ignored duplicate tool name '{attribute.Name}'.");
                    next.Remove(attribute.Name);
                    duplicates.Add(attribute.Name);
                    continue;
                }
                if (duplicates.Contains(attribute.Name)) continue;

                try
                {
                    var source = builtIn ? "builtin" : method.DeclaringType?.Assembly.GetName().Name ?? "project";
                    var isBuiltIn = source == "builtin";
                    var defaultEnabled = isBuiltIn && attribute.DefaultEnabled && attribute.Safety == UnityMcpSafety.SafeRead;
                    var enabled = enablement.GetOverride(attribute.Name) ?? defaultEnabled;
                    var schemaProvider = CreateSchemaProvider(attribute, method);
                    var input = schemaProvider?.GetInputSchema(method) ?? UnityMcpSchemaGenerator.InputFor(method, attribute.SupportsDryRun);
                    var output = schemaProvider?.GetOutputSchema(method) ?? UnityMcpSchemaGenerator.OutputFor(method);
                    if (!IsRootObjectSchema(input)) throw new InvalidOperationException("Input schema root must have type 'object'.");
                    if (output != null && output.Count > 0 && !IsRootObjectSchema(output)) throw new InvalidOperationException("Output schema root must have type 'object'.");
                    var descriptor = new UnityMcpToolDescriptor
                    {
                        name = attribute.Name,
                        title = string.IsNullOrWhiteSpace(attribute.Title) ? attribute.Name : attribute.Title,
                        description = attribute.Description ?? string.Empty,
                        category = attribute.Category ?? "project",
                        scopes = ScopeNames(attribute.Scope),
                        safety = SafetyName(attribute.Safety),
                        enabled = enabled,
                        defaultEnabled = defaultEnabled,
                        source = source,
                        inputSchema = input,
                        outputSchema = output,
                        schemaHash = UnityMcpSchemaGenerator.StableHash(new object[] { input, output }),
                        annotations = new Dictionary<string, object>
                        {
                            ["readOnlyHint"] = attribute.Safety == UnityMcpSafety.SafeRead,
                            ["destructiveHint"] = attribute.Safety == UnityMcpSafety.Destructive || attribute.Safety == UnityMcpSafety.Unsafe,
                            ["idempotentHint"] = attribute.Safety == UnityMcpSafety.SafeRead,
                            ["openWorldHint"] = false
                        },
                        mainThread = attribute.MainThread,
                        supportsDryRun = attribute.SupportsDryRun,
                        supportsCancel = attribute.SupportsCancellation,
                        returnsJob = attribute.ReturnsJob,
                        timeoutMs = Math.Max(100, attribute.TimeoutMs)
                    };
                    next.Add(attribute.Name, new UnityMcpToolBinding { Method = method, Attribute = attribute, Descriptor = descriptor });
                }
                catch (Exception exception)
                {
                    Debug.LogError($"UnityMCP failed to register '{attribute.Name}': {exception.Message}");
                }
            }

            lock (gate)
            {
                bindings.Clear();
                foreach (var pair in next) bindings.Add(pair.Key, pair.Value);
                revision = Guid.NewGuid().ToString("N");
            }
            Changed?.Invoke();
        }

        public async Task<UnityMcpResult> InvokeAsync(string name, JObject arguments, string expectedRevision, CancellationToken cancellationToken)
        {
            UnityMcpToolBinding binding;
            lock (gate)
            {
                if (!bindings.TryGetValue(name, out binding)) return UnityMcpResult.Error($"Unknown tool '{name}'.", "tool_not_found");
                if (!binding.Descriptor.enabled) return UnityMcpResult.Error($"Tool '{name}' is disabled by the local UnityMCP profile.", "tool_disabled");
                if (!string.IsNullOrEmpty(expectedRevision) && expectedRevision != revision)
                    return UnityMcpResult.Error("Registry revision is stale; refresh tools/list before retrying.", "stale_registry");
            }

            arguments = arguments ?? new JObject();
            var apply = arguments["apply"]?.Type == JTokenType.Boolean && arguments["apply"].Value<bool>();
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(Math.Max(100, Math.Min(binding.Attribute.TimeoutMs, 600000)));
                var invocationToken = timeout.Token;
                var context = new UnityMcpContext(name, binding.Attribute.SupportsDryRun && !apply, invocationToken);
                object invocation;
                try
                {
                    Func<object> call = () =>
                    {
                        var undoGroup = binding.Attribute.Safety == UnityMcpSafety.SafeRead || context.DryRun ? -1 : UnityMcpUndo.Begin("UnityMCP " + name);
                        try { return binding.Method.Invoke(null, BuildArguments(binding.Method, arguments, context, invocationToken)); }
                        finally { UnityMcpUndo.End(undoGroup); }
                    };
                    if (binding.Attribute.MainThread)
                    {
                        var dispatch = UnityMcpMainThread.RunAsync(call, invocationToken);
                        await AwaitWithCancellation(dispatch, invocationToken);
                        invocation = dispatch.Result;
                    }
                    else invocation = call();
                    if (invocation is Task task)
                    {
                        await AwaitWithCancellation(task, invocationToken);
                        invocation = task.GetType().IsGenericType ? task.GetType().GetProperty("Result")?.GetValue(task) : null;
                    }
                    return NormalizeResult(invocation, binding.Method);
                }
                catch (TargetInvocationException exception)
                {
                    var failure = exception.InnerException ?? exception;
                    if (failure is UnityMcpValidationException validation)
                    {
                        var result = UnityMcpResult.Error(validation.Message, validation.ErrorCode);
                        result.structuredContent = validation.StructuredContent;
                        return result;
                    }
                    if (failure is ArgumentException)
                        return UnityMcpResult.Error(failure.Message, "invalid_arguments");
                    Debug.LogWarning($"UnityMCP tool '{name}' failed ({failure.GetType().Name}); details were redacted.");
                    return UnityMcpResult.Error("Unity tool execution failed. See the local Unity Console for details.", "execution_failed");
                }
                catch (OperationCanceledException)
                {
                    return UnityMcpResult.Error(timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested ? "Tool invocation timed out." : "Tool invocation was cancelled.");
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"UnityMCP tool '{name}' failed ({exception.GetType().Name}); details were redacted.");
                    return UnityMcpResult.Error("Unity tool execution failed. See the local Unity Console for details.", "execution_failed");
                }
            }
        }

        private static object[] BuildArguments(MethodInfo method, JObject arguments, UnityMcpContext context, CancellationToken cancellationToken)
        {
            var parameters = method.GetParameters();
            var dataParameters = parameters.Where(p => p.ParameterType != typeof(UnityMcpContext) && p.ParameterType != typeof(CancellationToken)).ToArray();
            var result = new object[parameters.Length];
            var serializer = JsonSerializer.CreateDefault();
            serializer.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
            serializer.Converters.Add(UnityMcpValueJsonConverter.Instance);
            foreach (var parameter in parameters)
            {
                if (parameter.ParameterType == typeof(UnityMcpContext)) result[parameter.Position] = context;
                else if (parameter.ParameterType == typeof(CancellationToken)) result[parameter.Position] = cancellationToken;
                else if (dataParameters.Length == 1 && (IsDtoParameter(parameter.ParameterType) || IsOpaque(parameter.ParameterType))) result[parameter.Position] = arguments.ToObject(parameter.ParameterType, serializer);
                else
                {
                    var token = arguments[parameter.Name];
                    result[parameter.Position] = token == null
                        ? (parameter.HasDefaultValue ? parameter.DefaultValue : DefaultValue(parameter.ParameterType))
                        : token.ToObject(parameter.ParameterType, serializer);
                }
            }
            return result;
        }

        private static UnityMcpResult NormalizeResult(object result, MethodInfo method)
        {
            if (result == null) return UnityMcpResult.Success();
            if (result is UnityMcpResult direct) return direct;
            var type = result.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(UnityMcpResult<>))
            {
                var isError = (bool)(type.GetField("isError")?.GetValue(result) ?? false);
                var message = type.GetField("message")?.GetValue(result) as string;
                var structured = type.GetField("structuredContent")?.GetValue(result);
                return isError ? UnityMcpResult.Error(message) : UnityMcpResult.Success(WrapStructured(UnwrapReturnType(method.ReturnType), structured), message);
            }
            return UnityMcpResult.Success(WrapStructured(UnwrapReturnType(method.ReturnType), result));
        }

        private static object WrapStructured(Type type, object value)
        {
            if (type == null || IsDtoParameter(type) || IsOpaque(type)) return value;
            return new Dictionary<string, object> { ["result"] = value };
        }

        private static object DefaultValue(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

        private static async Task AwaitWithCancellation(Task task, CancellationToken cancellationToken)
        {
            var cancellation = Task.Delay(Timeout.Infinite, cancellationToken);
            if (await Task.WhenAny(task, cancellation) != task) cancellationToken.ThrowIfCancellationRequested();
            await task;
        }

        private static IUnityMcpSchemaProvider CreateSchemaProvider(UnityMcpToolAttribute attribute, MethodInfo method)
        {
            var hasOpaque = method.GetParameters().Any(p => ContainsOpaque(p.ParameterType, new HashSet<Type>())) || ContainsOpaque(UnwrapReturnType(method.ReturnType), new HashSet<Type>());
            if (attribute.SchemaProvider == null)
            {
                if (hasOpaque) throw new InvalidOperationException("object/JToken tool contracts require an explicit IUnityMcpSchemaProvider.");
                return null;
            }
            if (!typeof(IUnityMcpSchemaProvider).IsAssignableFrom(attribute.SchemaProvider))
                throw new InvalidOperationException("SchemaProvider must implement IUnityMcpSchemaProvider.");
            return (IUnityMcpSchemaProvider)Activator.CreateInstance(attribute.SchemaProvider);
        }

        private static Type UnwrapReturnType(Type type)
        {
            if (typeof(Task).IsAssignableFrom(type) && type.IsGenericType) type = type.GetGenericArguments()[0];
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(UnityMcpResult<>)) type = type.GetGenericArguments()[0];
            return type == typeof(UnityMcpResult) || type == typeof(Task) || type == typeof(void) ? null : type;
        }

        private static bool IsOpaque(Type type) => type != null && (type == typeof(object) || typeof(JToken).IsAssignableFrom(type));

        private static bool ContainsOpaque(Type type, HashSet<Type> visited)
        {
            if (type == null || type == typeof(UnityMcpContext) || type == typeof(CancellationToken) || type == typeof(UnityMcpResult)) return false;
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (IsOpaque(type)) return true;
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid)) return false;
            if (!visited.Add(type)) return false;
            if (type.IsArray) return ContainsOpaque(type.GetElementType(), visited);
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(UnityMcpResult<>)) return ContainsOpaque(type.GetGenericArguments()[0], visited);
            var enumerable = type.GetInterfaces().Concat(new[] { type }).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (enumerable != null) return ContainsOpaque(enumerable.GetGenericArguments()[0], visited);
            return type.GetFields(BindingFlags.Instance | BindingFlags.Public).Any(f => ContainsOpaque(f.FieldType, visited)) ||
                   type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.GetIndexParameters().Length == 0).Any(p => ContainsOpaque(p.PropertyType, visited));
        }

        private static bool IsDtoParameter(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type.IsArray) return false;
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) || IsOpaque(type)) return false;
            return type != typeof(Vector2) && type != typeof(Vector3) && type != typeof(Vector4) &&
                   type != typeof(Quaternion) && type != typeof(Color) && type != typeof(Rect);
        }

        private static bool IsRootObjectSchema(Dictionary<string, object> schema)
        {
            return schema != null && schema.TryGetValue("type", out var value) && Convert.ToString(value) == "object";
        }

        private static bool IsRequiredTypeAvailable(string requiredType)
        {
            if (string.IsNullOrWhiteSpace(requiredType)) return true;
            if (Type.GetType(requiredType, false) != null) return true;
            var typeName = requiredType.Split(',')[0].Trim();
            return AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
            {
                try { return assembly.GetType(typeName, false) != null; }
                catch { return false; }
            });
        }

        private static IEnumerable<MethodInfo> DiscoverByReflection()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException exception) { types = exception.Types.Where(t => t != null).ToArray(); }
                catch { continue; }
                foreach (var type in types)
                    foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                        if (method.IsDefined(typeof(UnityMcpToolAttribute), false)) yield return method;
            }
        }

        private static bool IsBuiltIn(MethodInfo method)
        {
            var assembly = method.DeclaringType?.Assembly.GetName().Name;
            return assembly == "DucMinh.UnityMcp.Runtime" || assembly == "DucMinh.UnityMcp.Editor";
        }

        private static string[] ScopeNames(UnityMcpScope scope)
        {
            var values = new List<string>();
            if ((scope & UnityMcpScope.Editor) != 0) values.Add("editor");
            if ((scope & UnityMcpScope.Runtime) != 0) values.Add("runtime");
            return values.ToArray();
        }

        private static string SafetyName(UnityMcpSafety safety)
        {
            switch (safety)
            {
                case UnityMcpSafety.SafeRead: return "safe-read";
                case UnityMcpSafety.Write: return "write";
                case UnityMcpSafety.Destructive: return "destructive";
                default: return "unsafe";
            }
        }
    }
}
