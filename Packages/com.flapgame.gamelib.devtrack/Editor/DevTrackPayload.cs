using System.Globalization;
using System.Text;

namespace FlapGame.GameLib.DevTrack
{
    /// <summary>
    /// The body of one dev-play event, in the format the internal tool reads:
    /// <code>{"event":"play_start","sessionId":"..","projectName":"..",...}</code>
    /// <c>event</c>, <c>sessionId</c> and <c>projectName</c> are always there (empty rather than missing); every other
    /// member is left out when it has no value, and <c>durationSeconds</c> only comes with a heartbeat and a play_stop.
    /// Written by hand so the package needs no JSON dependency and one event costs one string.
    /// </summary>
    public static class DevTrackPayload
    {
        public const string PlayStart = "play_start";
        public const string Heartbeat = "heartbeat";
        public const string PlayStop = "play_stop";

        public const string ContentType = "application/json";

        /// <summary>The <c>durationSeconds</c> value that leaves the member out (a play_start has no duration yet).</summary>
        public const int NoDuration = -1;

        public static string BuildBody(string eventName, string sessionId, DevTrackContext context, int durationSeconds, string clientVersion)
        {
            var sb = new StringBuilder(512);
            bool first = true;

            sb.Append('{');
            WriteRequired(sb, ref first, "event", eventName);
            WriteRequired(sb, ref first, "sessionId", sessionId);
            WriteRequired(sb, ref first, "projectName", context?.projectName);
            WriteOptional(sb, ref first, "projectPath", context?.projectPath);
            WriteOptional(sb, ref first, "bundleId", context?.bundleId);
            WriteOptional(sb, ref first, "unityVersion", context?.unityVersion);
            WriteOptional(sb, ref first, "buildTarget", context?.buildTarget);
            WriteOptional(sb, ref first, "gitUrl", context?.gitUrl);
            WriteOptional(sb, ref first, "gitBranch", context?.gitBranch);
            WriteOptional(sb, ref first, "gitCommit", context?.gitCommit);
            WriteOptional(sb, ref first, "gitUserName", context?.gitUserName);
            WriteOptional(sb, ref first, "gitUserEmail", context?.gitUserEmail);
            WriteOptional(sb, ref first, "pcUser", context?.pcUser);
            WriteOptional(sb, ref first, "pcName", context?.pcName);
            WriteOptional(sb, ref first, "localIp", context?.localIp);
            if (durationSeconds >= 0) WriteNumber(sb, ref first, "durationSeconds", durationSeconds);
            WriteOptional(sb, ref first, "clientVersion", clientVersion);
            sb.Append('}');

            return sb.ToString();
        }

        private static void WriteRequired(StringBuilder sb, ref bool first, string name, string value)
        {
            WriteName(sb, ref first, name);
            WriteString(sb, value ?? string.Empty);
        }

        private static void WriteOptional(StringBuilder sb, ref bool first, string name, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            WriteName(sb, ref first, name);
            WriteString(sb, value);
        }

        private static void WriteNumber(StringBuilder sb, ref bool first, string name, int value)
        {
            WriteName(sb, ref first, name);
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void WriteName(StringBuilder sb, ref bool first, string name)
        {
            if (!first) sb.Append(',');
            first = false;
            WriteString(sb, name);
            sb.Append(':');
        }

        /// <summary>A JSON string. Control characters go out escaped; a Windows path keeps its backslashes doubled.</summary>
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
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
