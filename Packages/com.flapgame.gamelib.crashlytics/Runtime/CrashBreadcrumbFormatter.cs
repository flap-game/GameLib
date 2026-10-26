using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FlapGame.GameLib.Crashlytics
{
    /// <summary>
    /// Turns an analytics event into one crash-log line: <c>level_complete level=p1_l03 stars=3</c>. Parameters are
    /// sorted by name so the same event always reads the same; the line is cut at <see cref="MaxLength"/> characters.
    /// </summary>
    public static class CrashBreadcrumbFormatter
    {
        public const int MaxLength = 256;

        // Reused per thread: one line per tracked event, so no builder / key list per call.
        [ThreadStatic] private static StringBuilder cachedLine;
        [ThreadStatic] private static List<string> cachedKeys;

        public static string Format(AnalyticsEvent analyticsEvent)
        {
            StringBuilder line = cachedLine ??= new StringBuilder(MaxLength + 32);
            line.Clear().Append(analyticsEvent.Name);
            IReadOnlyDictionary<string, object> parameters = analyticsEvent.Parameters;
            if (parameters != null && parameters.Count == 1)
            {
                foreach (KeyValuePair<string, object> kv in parameters)
                    AppendParameter(line, kv.Key, kv.Value);
            }
            else if (parameters != null && parameters.Count > 1)
            {
                List<string> keys = cachedKeys ??= new List<string>();
                keys.Clear();
                foreach (KeyValuePair<string, object> kv in parameters)
                    keys.Add(kv.Key);
                keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys)
                {
                    AppendParameter(line, key, parameters[key]);
                    if (line.Length >= MaxLength) break;
                }
                keys.Clear();
            }

            string result = line.Length > MaxLength ? line.ToString(0, MaxLength - 1) + "…" : line.ToString();
            if (line.Capacity > 4 * MaxLength) cachedLine = null;   // one huge value must not stay pinned
            return result;
        }

        private static void AppendParameter(StringBuilder line, string key, object value)
        {
            line.Append(' ').Append(key).Append('=');
            AppendValue(line, value);
        }

        private static void AppendValue(StringBuilder line, object value)
        {
            Span<char> digits = stackalloc char[32];
            int written;
            switch (value)
            {
                case null: line.Append("null"); return;
                case string s: line.Append(s); return;
                case bool b: line.Append(b ? "true" : "false"); return;
                // The common numbers are formatted into a stack buffer: same text as ToString(null, Invariant), no string.
                case int i when i.TryFormat(digits, out written, default, CultureInfo.InvariantCulture):
                    line.Append(digits.Slice(0, written));
                    return;
                case long l when l.TryFormat(digits, out written, default, CultureInfo.InvariantCulture):
                    line.Append(digits.Slice(0, written));
                    return;
                case IFormattable f: line.Append(f.ToString(null, CultureInfo.InvariantCulture)); return;
                case System.Collections.IEnumerable: line.Append("[…]"); return;
                default: line.Append(value.ToString()); return;
            }
        }
    }
}
