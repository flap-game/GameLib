using System;
using System.Globalization;

namespace FlapGame.GameLib.Extensions
{
    /// <summary>Numbers for HUDs and shops. Culture-invariant, so "1.5K" never turns into "1,5K" on a German phone.</summary>
    public static class NumberFormatter
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>
        /// Compact, cut down to one decimal (never rounded up, so a player never sees more than they have):
        /// 950 -> "950", 1500 -> "1.5K", 1960 -> "1.9K", 999_999 -> "999.9K", 2_340_000 -> "2.3M", 1e9 -> "1B".
        /// </summary>
        public static string Compact(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "0";
            double abs = Math.Abs(value);
            string sign = value < 0 && abs >= 1 ? "-" : string.Empty;

            if (abs >= 1e12) return sign + Tenths(abs, 1e12) + "T";
            if (abs >= 1e9) return sign + Tenths(abs, 1e9) + "B";
            if (abs >= 1e6) return sign + Tenths(abs, 1e6) + "M";
            if (abs >= 1e3) return sign + Tenths(abs, 1e3) + "K";
            return sign + Math.Floor(abs).ToString(Invariant);
        }

        public static string Compact(long value) => Compact((double)value);

        /// <summary>Thousands separators: 1234567 -> "1,234,567".</summary>
        public static string WithSeparators(long value) => value.ToString("N0", Invariant);

        /// <summary>A 0..1 fraction as a percentage: 0.256 -> "26%" (or "25.6%" with 1 decimal).</summary>
        public static string Percent(float fraction, int decimals = 0) =>
            (fraction * 100f).ToString("F" + Math.Max(0, decimals), Invariant) + "%";

        /// <summary><paramref name="abs"/> / <paramref name="divisor"/> cut to one decimal, in whole numbers so 2.3 never reads as 2.29999.</summary>
        private static string Tenths(double abs, double divisor)
        {
            long tenths = abs < 9e18
                ? (long)Math.Floor(abs) / (long)(divisor / 10)
                : (long)Math.Floor(abs / divisor * 10);
            long whole = tenths / 10;
            long fraction = tenths % 10;
            return fraction == 0
                ? whole.ToString(Invariant)
                : whole.ToString(Invariant) + "." + fraction.ToString(Invariant);
        }
    }
}
