using UnityEngine;

namespace FlapGame.GameLib.Extensions
{
    public static class ColorExtensions
    {
        public static Color WithAlpha(this Color color, float alpha) => new(color.r, color.g, color.b, alpha);

        /// <summary>"RRGGBB", no '#'.</summary>
        public static string ToHex(this Color color) => ColorUtility.ToHtmlStringRGB(color);

        /// <summary>"RRGGBBAA", no '#'.</summary>
        public static string ToHexRgba(this Color color) => ColorUtility.ToHtmlStringRGBA(color);

        /// <summary>
        /// Parses "#RGB", "#RRGGBB", "#RRGGBBAA" (the '#' is optional) or a color name such as "red".
        /// </summary>
        public static bool TryParseHex(string hex, out Color color)
        {
            if (string.IsNullOrEmpty(hex))
            {
                color = default;
                return false;
            }
            if (ColorUtility.TryParseHtmlString(hex, out color)) return true;
            return hex[0] != '#' && ColorUtility.TryParseHtmlString("#" + hex, out color);
        }

        /// <summary>Like <see cref="TryParseHex"/>, <paramref name="fallback"/> when the text is not a color.</summary>
        public static Color ParseHex(string hex, Color fallback = default) => TryParseHex(hex, out Color color) ? color : fallback;
    }
}
