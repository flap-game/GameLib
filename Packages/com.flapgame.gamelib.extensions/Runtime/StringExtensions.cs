namespace FlapGame.GameLib.Extensions
{
    public static class StringExtensions
    {
        public static bool IsNullOrEmpty(this string s) => string.IsNullOrEmpty(s);

        public static bool IsNullOrWhiteSpace(this string s) => string.IsNullOrWhiteSpace(s);

        /// <summary>Cuts the string to <paramref name="maxLength"/> characters, the ellipsis included: "Hello world" (8) -> "Hello w…".</summary>
        public static string Truncate(this string s, int maxLength, string ellipsis = "…")
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLength) return s;
            if (maxLength <= 0) return string.Empty;
            ellipsis ??= string.Empty;
            if (maxLength <= ellipsis.Length) return ellipsis.Substring(0, maxLength);
            return s.Substring(0, maxLength - ellipsis.Length) + ellipsis;
        }
    }
}
