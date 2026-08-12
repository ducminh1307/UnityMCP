using System;
using Newtonsoft.Json;
using UnityEngine;

namespace DucMinh.UnityMcp
{
    internal sealed class UnityMcpValueJsonConverter : JsonConverter
    {
        public static readonly UnityMcpValueJsonConverter Instance = new UnityMcpValueJsonConverter();
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Vector2) || objectType == typeof(Vector3) || objectType == typeof(Vector4) ||
                   objectType == typeof(Quaternion) || objectType == typeof(Color) || objectType == typeof(Rect);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            if (value is Vector2 vector2) { Number(writer, "x", vector2.x); Number(writer, "y", vector2.y); }
            else if (value is Vector3 vector3) { Number(writer, "x", vector3.x); Number(writer, "y", vector3.y); Number(writer, "z", vector3.z); }
            else if (value is Vector4 vector4) { Number(writer, "x", vector4.x); Number(writer, "y", vector4.y); Number(writer, "z", vector4.z); Number(writer, "w", vector4.w); }
            else if (value is Quaternion quaternion) { Number(writer, "x", quaternion.x); Number(writer, "y", quaternion.y); Number(writer, "z", quaternion.z); Number(writer, "w", quaternion.w); }
            else if (value is Color color) { Number(writer, "r", color.r); Number(writer, "g", color.g); Number(writer, "b", color.b); Number(writer, "a", color.a); }
            else if (value is Rect rect) { Number(writer, "x", rect.x); Number(writer, "y", rect.y); Number(writer, "width", rect.width); Number(writer, "height", rect.height); }
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var token = Newtonsoft.Json.Linq.JObject.Load(reader);
            float Value(string name, float fallback = 0f)
            {
                var component = token[name];
                return component == null || component.Type == Newtonsoft.Json.Linq.JTokenType.Null
                    ? fallback
                    : component.ToObject<float>();
            }
            if (objectType == typeof(Vector2)) return new Vector2(Value("x"), Value("y"));
            if (objectType == typeof(Vector3)) return new Vector3(Value("x"), Value("y"), Value("z"));
            if (objectType == typeof(Vector4)) return new Vector4(Value("x"), Value("y"), Value("z"), Value("w"));
            if (objectType == typeof(Quaternion)) return new Quaternion(Value("x"), Value("y"), Value("z"), Value("w", 1f));
            if (objectType == typeof(Color)) return new Color(Value("r"), Value("g"), Value("b"), Value("a", 1f));
            if (objectType == typeof(Rect)) return new Rect(Value("x"), Value("y"), Value("width"), Value("height"));
            throw new JsonSerializationException("Unsupported Unity value type " + objectType.FullName + ".");
        }

        private static void Number(JsonWriter writer, string name, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new JsonSerializationException("UnityMCP cannot serialize non-finite Unity values.");
            writer.WritePropertyName(name);
            writer.WriteValue(value);
        }
    }
}
