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

        public static string Format(AnalyticsEvent analyticsEvent)
        {
            var line = new StringBuilder(analyticsEvent.Name);
            IReadOnlyDictionary<string, object> parameters = analyticsEvent.Parameters;
            if (parameters != null && parameters.Count > 0)
            {
                var keys = new List<string>(parameters.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys)
                {
                    line.Append(' ').Append(key).Append('=').Append(FormatValue(parameters[key]));
                    if (line.Length >= MaxLength) break;
                }
            }

            return line.Length > MaxLength ? line.ToString(0, MaxLength - 1) + "…" : line.ToString();
        }

        private static string FormatValue(object value)
        {
            switch (value)
            {
                case null: return "null";
                case string s: return s;
                case bool b: return b ? "true" : "false";
                case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
                case System.Collections.IEnumerable: return "[…]";
                default: return value.ToString();
            }
        }
    }
}
