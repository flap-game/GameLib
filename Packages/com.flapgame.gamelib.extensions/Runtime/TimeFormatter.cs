using System;

namespace FlapGame.GameLib.Extensions
{
    /// <summary>
    /// Durations for timers and cooldowns. Negative values read as 0.
    /// <para><c>roundUp</c> (the default) is for countdowns: 0.2 s left shows "00:01" until it really ends, never "00:00"
    /// a second early. Pass <c>roundUp: false</c> for time that counts up (time played, a stopwatch): 0.9 s shows "00:00".</para>
    /// </summary>
    public static class TimeFormatter
    {
        /// <summary>"MM:SS"; past an hour the minutes keep counting: 3725 s -> "62:05".</summary>
        public static string MinutesSeconds(double seconds, bool roundUp = true)
        {
            long total = WholeSeconds(seconds, roundUp);
            return $"{total / 60:00}:{total % 60:00}";
        }

        /// <summary>"HH:MM:SS": 3725 s -> "01:02:05".</summary>
        public static string HoursMinutesSeconds(double seconds, bool roundUp = true)
        {
            long total = WholeSeconds(seconds, roundUp);
            return $"{total / 3600:00}:{total % 3600 / 60:00}:{total % 60:00}";
        }

        /// <summary>"MM:SS" under an hour, "HH:MM:SS" from an hour.</summary>
        public static string Clock(double seconds, bool roundUp = true) =>
            WholeSeconds(seconds, roundUp) >= 3600 ? HoursMinutesSeconds(seconds, roundUp) : MinutesSeconds(seconds, roundUp);

        /// <summary>
        /// The two largest units: "45s", "5m 30s", "2h 15m", "3d 4h". With <paramref name="units"/> 1 only the largest ("3d"),
        /// zero parts are left out ("2h" not "2h 0m").
        /// </summary>
        public static string Short(double seconds, int units = 2, bool roundUp = true)
        {
            long total = WholeSeconds(seconds, roundUp);
            if (total == 0) return "0s";
            Span<long> values = stackalloc long[] { total / 86400, total % 86400 / 3600, total % 3600 / 60, total % 60 };
            ReadOnlySpan<char> suffixes = "dhms";

            int first = 0;
            while (values[first] == 0) first++;
            string result = string.Empty;
            for (int i = first; i < values.Length && i < first + Math.Max(1, units); i++)
            {
                if (values[i] == 0) continue;
                result += (result.Length == 0 ? string.Empty : " ") + values[i] + suffixes[i];
            }
            return result;
        }

        public static string MinutesSeconds(TimeSpan time, bool roundUp = true) => MinutesSeconds(time.TotalSeconds, roundUp);
        public static string HoursMinutesSeconds(TimeSpan time, bool roundUp = true) => HoursMinutesSeconds(time.TotalSeconds, roundUp);
        public static string Clock(TimeSpan time, bool roundUp = true) => Clock(time.TotalSeconds, roundUp);
        public static string Short(TimeSpan time, int units = 2, bool roundUp = true) => Short(time.TotalSeconds, units, roundUp);

        private static long WholeSeconds(double seconds, bool roundUp)
        {
            if (double.IsNaN(seconds) || seconds <= 0) return 0;
            if (seconds >= long.MaxValue) return long.MaxValue / 2;
            // A tiny tolerance for float noise: 59.0000001 rounds up to 59 (not 60), 59.9999999 rounds down to 60 (not 59).
            return roundUp ? (long)Math.Ceiling(seconds - 1e-4) : (long)Math.Floor(seconds + 1e-4);
        }
    }
}
