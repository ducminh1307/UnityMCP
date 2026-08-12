using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DucMinh.UnityMcp
{
    public interface IUnityMcpSchemaProvider
    {
        Dictionary<string, object> GetInputSchema(MethodInfo method);
        Dictionary<string, object> GetOutputSchema(MethodInfo method);
    }

    public static class UnityMcpSchemaGenerator
    {
        private static readonly HashSet<Type> ContextTypes = new HashSet<Type>
        {
            typeof(UnityMcpContext), typeof(System.Threading.CancellationToken)
        };

        public static Dictionary<string, object> InputFor(MethodInfo method, bool supportsDryRun)
        {
            var parameters = method.GetParameters().Where(p => !ContextTypes.Contains(p.ParameterType)).ToArray();
            JObject schema;
            if (parameters.Length == 0)
            {
                schema = ObjectSchema();
            }
            else if (parameters.Length == 1 && IsDtoObject(parameters[0].ParameterType))
            {
                schema = SchemaFor(parameters[0].ParameterType, new HashSet<Type>()) as JObject ?? ObjectSchema();
                // DTO field initializers are the defaults. Required fields can be enforced by a
                // project schema provider or the handler, without forcing value-type defaults into MCP calls.
                schema.Remove("required");
            }
            else
            {
                schema = ObjectSchema();
                var properties = (JObject)schema["properties"];
                var required = new JArray();
                foreach (var parameter in parameters)
                {
                    properties[parameter.Name] = SchemaFor(parameter.ParameterType, new HashSet<Type>());
                    if (!parameter.IsOptional && !IsNullable(parameter.ParameterType)) required.Add(parameter.Name);
                }
                if (required.Count > 0) schema["required"] = required;
            }

            if (supportsDryRun)
            {
                if (schema["type"]?.Value<string>() != "object") schema = ObjectSchema();
                ((JObject)schema["properties"])["apply"] = new JObject
                {
                    ["type"] = "boolean",
                    ["default"] = false,
                    ["description"] = "Set true to apply the mutation. False performs a dry run."
                };
                if (schema["required"] is JArray required)
                {
                    foreach (var token in required.Where(value => value.Value<string>() == "apply").ToArray()) token.Remove();
                    if (required.Count == 0) schema.Remove("required");
                }
            }
            return schema.ToObject<Dictionary<string, object>>();
        }

        public static Dictionary<string, object> OutputFor(MethodInfo method)
        {
            var type = method.ReturnType;
            if (typeof(System.Threading.Tasks.Task).IsAssignableFrom(type) && type.IsGenericType)
                type = type.GetGenericArguments()[0];
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(UnityMcpResult<>))
                type = type.GetGenericArguments()[0];
            else if (type == typeof(UnityMcpResult) || type == typeof(void) || type == typeof(System.Threading.Tasks.Task))
                return null;
            var schema = SchemaFor(type, new HashSet<Type>());
            if (schema["type"]?.Value<string>() == "object") return schema.ToObject<Dictionary<string, object>>();
            return new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject { ["result"] = schema },
                ["required"] = new JArray("result"),
                ["additionalProperties"] = false
            }.ToObject<Dictionary<string, object>>();
        }

        public static string StableHash(object schema)
        {
            var json = JsonConvert.SerializeObject(schema, Formatting.None, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json)).Take(8).Select(b => b.ToString("x2")));
            }
        }

        private static JToken SchemaFor(Type type, HashSet<Type> stack)
        {
            var nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null)
                return new JObject { ["anyOf"] = new JArray(SchemaFor(nullable, stack), new JObject { ["type"] = "null" }) };
            if (type == typeof(string) || type == typeof(char) || type == typeof(Guid)) return new JObject { ["type"] = "string" };
            if (type == typeof(bool)) return new JObject { ["type"] = "boolean" };
            if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
                type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong))
                return new JObject { ["type"] = "integer" };
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return new JObject { ["type"] = "number" };
            if (type.IsEnum) return new JObject { ["type"] = "string", ["enum"] = new JArray(Enum.GetNames(type)) };
            if (type == typeof(Vector2)) return NumericObject("x", "y");
            if (type == typeof(Vector3)) return NumericObject("x", "y", "z");
            if (type == typeof(Vector4) || type == typeof(Quaternion)) return NumericObject("x", "y", "z", "w");
            if (type == typeof(Color)) return NumericObject("r", "g", "b", "a");
            if (type == typeof(Rect)) return NumericObject("x", "y", "width", "height");
            if (type.IsArray) return new JObject { ["type"] = "array", ["items"] = SchemaFor(type.GetElementType(), stack) };
            var enumerable = type.GetInterfaces().Concat(new[] { type }).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (enumerable != null) return new JObject { ["type"] = "array", ["items"] = SchemaFor(enumerable.GetGenericArguments()[0], stack) };
            if (typeof(JToken).IsAssignableFrom(type) || type == typeof(object)) return new JObject();
            if (stack.Contains(type)) return new JObject { ["type"] = "object" };

            stack.Add(type);
            var result = ObjectSchema();
            var properties = (JObject)result["properties"];
            var required = new JArray();
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public).OrderBy(f => f.Name))
            {
                if (field.IsNotSerialized) continue;
                properties[field.Name] = SchemaFor(field.FieldType, stack);
                if (!IsNullable(field.FieldType)) required.Add(field.Name);
            }
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.CanRead && p.GetIndexParameters().Length == 0).OrderBy(p => p.Name))
            {
                if (properties[property.Name] != null || property.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                properties[property.Name] = SchemaFor(property.PropertyType, stack);
                if (!IsNullable(property.PropertyType)) required.Add(property.Name);
            }
            if (required.Count > 0) result["required"] = required;
            stack.Remove(type);
            return result;
        }

        private static JObject ObjectSchema() => new JObject
        {
            ["type"] = "object", ["properties"] = new JObject(), ["additionalProperties"] = false
        };

        private static JObject NumericObject(params string[] names)
        {
            var result = ObjectSchema();
            var props = (JObject)result["properties"];
            foreach (var name in names) props[name] = new JObject { ["type"] = "number" };
            return result;
        }

        private static bool IsNullable(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) != null;

        private static bool IsDtoObject(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type.IsArray) return false;
            if (typeof(IEnumerable).IsAssignableFrom(type) || typeof(JToken).IsAssignableFrom(type) || type == typeof(object)) return false;
            if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Quaternion) || type == typeof(Color) || type == typeof(Rect)) return false;
            return true;
        }
    }
}
