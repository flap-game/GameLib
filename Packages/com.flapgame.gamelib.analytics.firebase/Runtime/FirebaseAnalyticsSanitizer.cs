using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FlapGame.GameLib.Analytics.Firebase
{
    /// <summary>
    /// Enforces Firebase / GA4 collection limits before an event reaches the SDK, which otherwise drops
    /// offending events or parameters silently (only visible in DebugView). Lives in the SDK-free assembly
    /// so it can be unit tested; only the Firebase sink uses it - the generic <c>IAnalytics</c>
    /// forwards events untouched to every sink.
    /// <list type="bullet">
    /// <item>Event / parameter / user-property names: <c>[A-Za-z0-9_]</c>, start with a letter, no reserved
    /// prefix (<c>firebase_</c>, <c>google_</c>, <c>ga_</c>). Invalid characters become <c>_</c>, a bad start
    /// or reserved prefix gets <see cref="RenamePrefix"/>, then the name is truncated.</item>
    /// <item>Event and parameter names ≤ 40 chars, user property names ≤ 24, user property values ≤ 36,
    /// string parameter values ≤ 100, user id ≤ 256, ≤ 25 parameters per event (extras dropped).</item>
    /// <item>Events Firebase reserves for itself (<c>first_open</c>, <c>session_start</c>,
    /// <c>in_app_purchase</c>, ...) are renamed with <see cref="RenamePrefix"/>.</item>
    /// </list>
    /// Every distinct problem is warned about once per instance, not once per event.
    /// </summary>
    public sealed class FirebaseAnalyticsSanitizer
    {
        public const int MaxEventNameLength = 40;
        public const int MaxParameterNameLength = 40;
        public const int MaxParameterStringValueLength = 100;
        public const int MaxParametersPerEvent = 25;
        public const int MaxUserPropertyNameLength = 24;
        public const int MaxUserPropertyValueLength = 36;
        public const int MaxUserIdLength = 256;
        public const string RenamePrefix = "x_";

        private static readonly string[] ReservedPrefixes = { "firebase_", "google_", "ga_" };

        private static readonly HashSet<string> ReservedEventNames = new(StringComparer.Ordinal)
        {
            "ad_activeview", "ad_click", "ad_exposure", "ad_query", "ad_reward", "adunit_exposure",
            "app_background", "app_clear_data", "app_exception", "app_remove", "app_store_refund",
            "app_store_subscription_cancel", "app_store_subscription_convert", "app_store_subscription_renew",
            "app_update", "app_upgrade", "dynamic_link_app_open", "dynamic_link_app_update",
            "dynamic_link_first_open", "error", "first_open", "first_visit", "in_app_purchase",
            "notification_dismiss", "notification_foreground", "notification_open", "notification_receive",
            "os_update", "session_start", "session_start_with_rollout", "user_engagement",
        };

        private static readonly HashSet<string> ReservedUserPropertyNames = new(StringComparer.Ordinal)
        {
            "first_open_after_install", "first_open_time", "first_visit_time", "last_deep_link_referrer",
            "user_id", "last_advertising_id_reset",
        };

        private const int MaxCachedEventNames = 256;

        private readonly HashSet<string> warned = new(StringComparer.Ordinal);
        // Event names that needed a rename (rare: a typo in game code), so the rename and its warning text are built once.
        private readonly Dictionary<string, string> renamedEvents = new(StringComparer.Ordinal);
        private readonly Action<string> warn;

        /// <param name="warn">Warning sink (defaults to <c>Debug.LogWarning</c>); each distinct message once.</param>
        public FirebaseAnalyticsSanitizer(Action<string> warn = null)
        {
            this.warn = warn ?? Debug.LogWarning;
        }

        /// <summary>
        /// Returns <paramref name="analyticsEvent"/> fitted to Firebase limits, or null when it cannot be sent (empty
        /// name). An event that already fits is returned as is, without a copy; otherwise the result is a copy. Values
        /// keep their type except strings, which are truncated, and unsupported objects, which become truncated strings.
        /// Item arrays (<c>IDictionary&lt;string, object&gt;</c> sequences, e.g. the <c>items</c> of <c>purchase</c>) pass through.
        /// </summary>
        public AnalyticsEvent? Sanitize(AnalyticsEvent analyticsEvent)
        {
            if (Fits(analyticsEvent))
                return analyticsEvent;

            string name = SanitizeEventName(analyticsEvent.Name);
            if (name == null)
            {
                WarnOnce("empty-event", "[FirebaseAnalytics] Dropped an event with an empty name.");
                return null;
            }

            IReadOnlyDictionary<string, object> source = analyticsEvent.Parameters;
            if (source == null || source.Count == 0)
                return new AnalyticsEvent(name, source);

            var result = new Dictionary<string, object>(Math.Min(source.Count, MaxParametersPerEvent), StringComparer.Ordinal);
            int dropped = 0;
            foreach (KeyValuePair<string, object> kv in source)
            {
                string key = SanitizeName(kv.Key, MaxParameterNameLength, null);
                if (key == null)
                {
                    WarnOnce($"empty-param:{name}", $"[FirebaseAnalytics] Event '{name}': dropped a parameter with an empty name.");
                    continue;
                }
                if (key != kv.Key)
                    WarnOnce($"param-name:{name}:{kv.Key}", $"[FirebaseAnalytics] Event '{name}': parameter '{kv.Key}' renamed to '{key}'.");
                if (result.ContainsKey(key))
                {
                    WarnOnce($"param-dup:{name}:{key}", $"[FirebaseAnalytics] Event '{name}': parameter '{kv.Key}' collides with '{key}' after renaming; dropped.");
                    continue;
                }
                if (result.Count >= MaxParametersPerEvent)
                {
                    dropped++;
                    continue;
                }

                object value = SanitizeValue(kv.Value, out bool truncated);
                if (truncated)
                    WarnOnce($"param-value:{name}:{key}", $"[FirebaseAnalytics] Event '{name}': value of '{key}' truncated to {MaxParameterStringValueLength} chars.");
                result[key] = value;
            }

            if (dropped > 0)
                WarnOnce($"param-count:{name}", $"[FirebaseAnalytics] Event '{name}' has more than {MaxParametersPerEvent} parameters; dropped {dropped}.");

            return new AnalyticsEvent(name, result);
        }

        // True when Sanitize would return the same name and the same values - checked without allocating.
        private static bool Fits(AnalyticsEvent analyticsEvent)
        {
            if (!IsValidName(analyticsEvent.Name, MaxEventNameLength, ReservedEventNames)) return false;
            IReadOnlyDictionary<string, object> parameters = analyticsEvent.Parameters;
            if (parameters == null) return true;
            if (parameters.Count > MaxParametersPerEvent) return false;
            foreach (KeyValuePair<string, object> kv in parameters)
            {
                // Valid keys map to themselves, so distinct source keys cannot collide.
                if (!IsValidName(kv.Key, MaxParameterNameLength, null) || !PassesThrough(kv.Value)) return false;
            }
            return true;
        }

        // SanitizeName for an event name, with its warning; renames are cached so a renamed event costs one lookup.
        private string SanitizeEventName(string original)
        {
            if (string.IsNullOrEmpty(original)) return null;
            lock (renamedEvents)
            {
                if (renamedEvents.TryGetValue(original, out string cached)) return cached;
            }

            string name = SanitizeName(original, MaxEventNameLength, ReservedEventNames);
            if (name != null && name != original)
                WarnOnce("event-name:" + original, $"[FirebaseAnalytics] Event '{original}' renamed to '{name}' (Firebase: ≤{MaxEventNameLength} chars [A-Za-z0-9_], starts with a letter, not reserved).");
            lock (renamedEvents)
            {
                if (renamedEvents.Count < MaxCachedEventNames) renamedEvents[original] = name;
            }
            return name;
        }

        /// <summary>Sanitized user property name, or null to skip the call.</summary>
        public string SanitizeUserPropertyName(string name)
        {
            string clean = SanitizeName(name, MaxUserPropertyNameLength, ReservedUserPropertyNames);
            if (clean == null)
                WarnOnce("empty-user-property", "[FirebaseAnalytics] Skipped a user property with an empty name.");
            else if (clean != name)
                WarnOnce("user-property:" + name, $"[FirebaseAnalytics] User property '{name}' renamed to '{clean}' (≤{MaxUserPropertyNameLength} chars [A-Za-z0-9_], starts with a letter, not reserved).");
            return clean;
        }

        /// <summary>User property value truncated to 36 chars (null clears and passes through).</summary>
        public string SanitizeUserPropertyValue(string name, string value)
        {
            if (value == null || value.Length <= MaxUserPropertyValueLength) return value;
            WarnOnce("user-property-value:" + name, $"[FirebaseAnalytics] User property '{name}' value truncated to {MaxUserPropertyValueLength} chars.");
            return value.Substring(0, MaxUserPropertyValueLength);
        }

        /// <summary>User id truncated to 256 chars (null clears and passes through).</summary>
        public string SanitizeUserId(string userId)
        {
            if (userId == null || userId.Length <= MaxUserIdLength) return userId;
            WarnOnce("user-id", $"[FirebaseAnalytics] User id truncated to {MaxUserIdLength} chars.");
            return userId.Substring(0, MaxUserIdLength);
        }

        // ── Pure helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Maps <paramref name="name"/> onto Firebase's name grammar; null when nothing usable is left.
        /// Invalid characters → <c>_</c>; non-letter start, reserved prefix or a name in
        /// <paramref name="reservedNames"/> → <see cref="RenamePrefix"/> + name; then truncation.
        /// </summary>
        public static string SanitizeName(string name, int maxLength, ICollection<string> reservedNames = null)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (IsValidName(name, maxLength, reservedNames)) return name;

            var sb = new StringBuilder(name.Length + RenamePrefix.Length);
            foreach (char c in name)
                sb.Append(IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
            string clean = sb.ToString();
            if (clean.Trim('_').Length == 0) return null;

            if (!IsAsciiLetter(clean[0]) || HasReservedPrefix(clean) || (reservedNames != null && reservedNames.Contains(clean)))
                clean = RenamePrefix + clean;

            return clean.Length > maxLength ? clean.Substring(0, maxLength) : clean;
        }

        /// <summary>True when <see cref="SanitizeName"/> returns <paramref name="name"/> itself: [A-Za-z0-9_] only, starts
        /// with a letter, no reserved prefix, not reserved, within <paramref name="maxLength"/>.</summary>
        public static bool IsValidName(string name, int maxLength, ICollection<string> reservedNames = null)
        {
            if (string.IsNullOrEmpty(name) || name.Length > maxLength || !IsAsciiLetter(name[0])) return false;
            foreach (char c in name)
                if (!IsAsciiLetterOrDigit(c) && c != '_') return false;
            return !HasReservedPrefix(name) && (reservedNames == null || !reservedNames.Contains(name));
        }

        // True when SanitizeValue returns the value itself, untruncated.
        private static bool PassesThrough(object value)
        {
            switch (value)
            {
                case string s:
                    return s.Length <= MaxParameterStringValueLength;
                case bool:
                case sbyte: case byte: case short: case ushort: case int: case uint: case long:
                case float: case double: case decimal:
                case IEnumerable<IDictionary<string, object>>:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Strings truncated to 100 chars; numbers / bools / item arrays unchanged; anything else stringified.</summary>
        public static object SanitizeValue(object value, out bool truncated)
        {
            truncated = false;
            switch (value)
            {
                case null:
                    return string.Empty;
                case string s:
                    return Truncate(s, out truncated);
                case bool:
                case sbyte: case byte: case short: case ushort: case int: case uint: case long:
                case float: case double: case decimal:
                    return value;
                case ulong ul:
                    return ul <= long.MaxValue ? (object)(long)ul : (double)ul;
                case IEnumerable<IDictionary<string, object>>:
                    return value;
                case IFormattable f:
                    return Truncate(f.ToString(null, CultureInfo.InvariantCulture), out truncated);
                default:
                    return Truncate(value.ToString() ?? string.Empty, out truncated);
            }
        }

        private static string Truncate(string s, out bool truncated)
        {
            truncated = s.Length > MaxParameterStringValueLength;
            return truncated ? s.Substring(0, MaxParameterStringValueLength) : s;
        }

        private static bool HasReservedPrefix(string name)
        {
            foreach (string prefix in ReservedPrefixes)
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        private static bool IsAsciiLetterOrDigit(char c) => IsAsciiLetter(c) || (c >= '0' && c <= '9');

        private void WarnOnce(string key, string message)
        {
            lock (warned)
            {
                if (!warned.Add(key)) return;
            }
            warn(message);
        }
    }
}
