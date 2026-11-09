using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FlapGame.GameLib.Analytics.Local
{
    /// <summary>
    /// The body of one event, in the format the internal tracking server reads (the same as aframework's local analytics):
    /// <code>{"bundleId":"..","appVersion":"..","idfa":"..","eventName":"..","params":{..}}</code>
    /// <c>params</c> is left out when the event has none. Written by hand so the package needs no JSON dependency.
    /// </summary>
    public static class LocalTrackingJson
    {
        // U+2028 / U+2029: valid JSON strings, but some readers (and JavaScript before ES2019) break on them.
        private const char LineSeparator = (char)0x2028;
        private const char ParagraphSeparator = (char)0x2029;

        public static string BuildBody(string bundleId, string appVersion, string deviceId, LocalTrackingEvent trackingEvent)
        {
            if (trackingEvent == null) throw new ArgumentNullException(nameof(trackingEvent));

            var sb = new StringBuilder(256);
            sb.Append('{');
            WriteMember(sb, "bundleId", bundleId, first: true);
            WriteMember(sb, "appVersion", appVersion, first: false);
            WriteMember(sb, "idfa", deviceId, first: false);
            WriteMember(sb, "eventName", trackingEvent.Name, first: false);
            if (trackingEvent.Parameters != null && trackingEvent.Parameters.Count > 0)
            {
                sb.Append(",\"params\":");
                WriteValue(sb, trackingEvent.Parameters);
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static void WriteMember(StringBuilder sb, string name, string value, bool first)
        {
            if (!first) sb.Append(',');
            WriteString(sb, name);
            sb.Append(':');
            WriteString(sb, value ?? string.Empty);
        }

        /// <summary>Writes any value a game puts in an event parameter; an unknown type goes out as its <c>ToString</c>.</summary>
        public static void WriteValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    return;
                case string s:
                    WriteString(sb, s);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case char c:
                    WriteString(sb, c.ToString());
                    return;
                case float f:
                    WriteDouble(sb, f);
                    return;
                case double d:
                    WriteDouble(sb, d);
                    return;
                case decimal m:
                    sb.Append(m.ToString(CultureInfo.InvariantCulture));
                    return;
                case sbyte or byte or short or ushort or int or uint or long or ulong:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                case DateTime dt:
                    WriteString(sb, dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
                    return;
                case Enum e:
                    WriteString(sb, e.ToString());
                    return;
                case IDictionary dictionary:
                    WriteObject(sb, dictionary);
                    return;
                case IEnumerable list:
                    WriteArray(sb, list);
                    return;
                default:
                    WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
            }
        }

        private static void WriteObject(StringBuilder sb, IDictionary dictionary)
        {
            sb.Append('{');
            bool first = true;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key == null) continue;
                if (!first) sb.Append(',');
                first = false;
                WriteString(sb, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                sb.Append(':');
                WriteValue(sb, entry.Value);
            }
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IEnumerable list)
        {
            sb.Append('[');
            bool first = true;
            foreach (object item in list)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteValue(sb, item);
            }
            sb.Append(']');
        }

        // JSON has no NaN or Infinity; null is the only honest answer (and the server reads it as "missing").
        private static void WriteDouble(StringBuilder sb, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) sb.Append("null");
            else sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        public static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        // Control characters (and the line separators that break some JSON readers) go out escaped.
                        if (c < ' ' || c == LineSeparator || c == ParagraphSeparator)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>Only used by the tests and the log line; the body itself is written straight into a buffer.</summary>
        public static string ToJson(IDictionary<string, object> value)
        {
            var sb = new StringBuilder(128);
            WriteValue(sb, value);
            return sb.ToString();
        }
    }
}
