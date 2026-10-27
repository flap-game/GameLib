using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlapGame.GameLib.Save.Json
{
    /// <summary>
    /// Newtonsoft converters for common Unity structs. Without these, serializing e.g. a Vector3
    /// throws "Self referencing loop detected for property 'normalized'" and bloats the JSON with
    /// magnitude/normalized properties. Applied by default through <see cref="SaveJson.DefaultSettings"/>.
    /// </summary>
    public static class UnityJsonConverters
    {
        public static JsonConverter[] Create() => new JsonConverter[]
        {
            new Vector2Converter(),
            new Vector3Converter(),
            new Vector4Converter(),
            new Vector2IntConverter(),
            new Vector3IntConverter(),
            new QuaternionConverter(),
            new ColorConverter(),
            new Color32Converter(),
            new RectConverter(),
        };

        private static float F(JObject o, string name) => o.TryGetValue(name, out JToken t) ? t.Value<float>() : 0f;
        private static int I(JObject o, string name) => o.TryGetValue(name, out JToken t) ? t.Value<int>() : 0;
        private static byte B(JObject o, string name) => o.TryGetValue(name, out JToken t) ? t.Value<byte>() : (byte)0;

        private static void Write(JsonWriter w, params (string name, float value)[] fields)
        {
            w.WriteStartObject();
            foreach ((string name, float value) in fields)
            {
                w.WritePropertyName(name);
                w.WriteValue(value);
            }
            w.WriteEndObject();
        }

        private static void WriteInt(JsonWriter w, params (string name, int value)[] fields)
        {
            w.WriteStartObject();
            foreach ((string name, int value) in fields)
            {
                w.WritePropertyName(name);
                w.WriteValue(value);
            }
            w.WriteEndObject();
        }

        public sealed class Vector2Converter : JsonConverter<Vector2>
        {
            public override void WriteJson(JsonWriter w, Vector2 v, JsonSerializer s) => Write(w, ("x", v.x), ("y", v.y));
            public override Vector2 ReadJson(JsonReader r, Type t, Vector2 e, bool h, JsonSerializer s)
            { JObject o = JObject.Load(r); return new Vector2(F(o, "x"), F(o, "y")); }
        }

        public sealed class Vector3Converter : JsonConverter<Vector3>
        {
            public override void WriteJson(JsonWriter w, Vector3 v, JsonSerializer s) => Write(w, ("x", v.x), ("y", v.y), ("z", v.z));
            public override Vector3 ReadJson(JsonReader r, Type t, Vector3 e, bool h, JsonSerializer s)
            { JObject o = JObject.Load(r); return new Vector3(F(o, "x"), F(o, "y"), F(o, "z")); }
        }

        public sealed class Vector4Converter : JsonConverter<Vector4>
        {
            public override void WriteJson(JsonWriter w, Vector4 v, JsonSerializer s) => Write(w, ("x", v.x), ("y", v.y), ("z", v.z), ("w", v.w));
            public override Vector4 ReadJson(JsonReader r, Type t, Vector4 e, bool h, JsonSerializer s)
            { JObject o = JObject.Load(r); return new Vector4(F(o, "x"), F(o, "y"), F(o, "z"), F(o, "w")); }
        }

        public sealed class Vector2IntConverter : JsonConverter<Vector2Int>
        {
            public override void WriteJson(JsonWriter w, Vector2Int v, JsonSerializer s) => WriteInt(w, ("x", v.x), ("y", v.y));
            public override Vector2Int ReadJson(JsonReader r, Type t, Vector2Int e, bool h, JsonSerializer s)
            { JObject o = JObject.Load(r); return new Vector2Int(I(o, "x"), I(o, "y")); }
        }

        public sealed class Vector3IntConverter : JsonConverter<Vector3Int>
        {
            public override void WriteJson(JsonWriter w, Vector3Int v, JsonSerializer s) => WriteInt(w, ("x", v.x), ("y", v.y), ("z", v.z));
            public override Vector3Int ReadJson(JsonReader r, Type t, Vector3Int e, bool h, JsonSerializer s)
            { JObject o = JObject.Load(r); return new Vector3Int(I(o, "x"), I(o, "y"), I(o, "z")); }
        }

        public sealed class QuaternionConverter : JsonConverter<Quaternion>
        {
            public override void WriteJson(JsonWriter w, Quaternion v, JsonSerializer s) => Write(w, ("x", v.x), ("y", v.y), ("z", v.z), ("w", v.w));
            public override Quaternion ReadJson(JsonReader r, Type t, Quaternion e, bool h, JsonSerializer s)
            { JObject o = JObject.Load(r); return new Quaternion(F(o, "x"), F(o, "y"), F(o, "z"), F(o, "w")); }
        }

        public sealed class ColorConverter : JsonConverter<Color>
        {
            public override void WriteJson(JsonWriter w, Color v, JsonSerializer s) => Write(w, ("r", v.r), ("g", v.g), ("b", v.b), ("a", v.a));
            public override Color ReadJson(JsonReader r, Type t, Color e, bool h, JsonSerializer s)
            { JObject o = JObject.Load(r); return new Color(F(o, "r"), F(o, "g"), F(o, "b"), o.ContainsKey("a") ? F(o, "a") : 1f); }
        }

        public sealed class Color32Converter : JsonConverter<Color32>
        {
            public override void WriteJson(JsonWriter w, Color32 v, JsonSerializer s) => WriteInt(w, ("r", v.r), ("g", v.g), ("b", v.b), ("a", v.a));
            public override Color32 ReadJson(JsonReader r, Type t, Color32 e, bool h, JsonSerializer s)
            { JObject o = JObject.Load(r); return new Color32(B(o, "r"), B(o, "g"), B(o, "b"), o.ContainsKey("a") ? B(o, "a") : (byte)255); }
        }

        public sealed class RectConverter : JsonConverter<Rect>
        {
            public override void WriteJson(JsonWriter w, Rect v, JsonSerializer s) => Write(w, ("x", v.x), ("y", v.y), ("width", v.width), ("height", v.height));
            public override Rect ReadJson(JsonReader r, Type t, Rect e, bool h, JsonSerializer s)
            { JObject o = JObject.Load(r); return new Rect(F(o, "x"), F(o, "y"), F(o, "width"), F(o, "height")); }
        }
    }
}
