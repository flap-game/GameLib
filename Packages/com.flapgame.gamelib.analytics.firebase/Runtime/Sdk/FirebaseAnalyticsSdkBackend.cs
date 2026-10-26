using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlapGame.GameLib.Firebase;
using UnityEngine;
using UnityEngine.Scripting;
using FA = global::Firebase.Analytics;

namespace FlapGame.GameLib.Analytics.Firebase
{
    /// <summary>The real <see cref="IFirebaseAnalyticsBackend"/>. Compiled only with the Firebase Analytics package.</summary>
    public sealed class FirebaseAnalyticsSdkBackend : IFirebaseAnalyticsBackend
    {
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await FirebaseStartup.EnsureReadyAsync(cancellationToken);
            FA.FirebaseAnalytics.SetAnalyticsCollectionEnabled(true);
        }

        public void LogEvent(string name, IReadOnlyDictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                FA.FirebaseAnalytics.LogEvent(name);
                return;
            }

            var array = new FA.Parameter[parameters.Count];
            int i = 0;
            foreach (KeyValuePair<string, object> kv in parameters)
                array[i++] = ToParameter(kv.Key, kv.Value);
            FA.FirebaseAnalytics.LogEvent(name, array);
        }

        public void SetUserProperty(string name, string value) => FA.FirebaseAnalytics.SetUserProperty(name, value);

        public void SetUserId(string userId) => FA.FirebaseAnalytics.SetUserId(userId);

        public void SetConsent(FirebaseConsent consent)
        {
            FA.FirebaseAnalytics.SetConsent(new Dictionary<FA.ConsentType, FA.ConsentStatus>
            {
                [FA.ConsentType.AnalyticsStorage] = Status(consent.AnalyticsStorage),
                [FA.ConsentType.AdStorage] = Status(consent.AdStorage),
                [FA.ConsentType.AdUserData] = Status(consent.AdUserData),
                [FA.ConsentType.AdPersonalization] = Status(consent.AdPersonalization),
            });
        }

        private static FA.ConsentStatus Status(bool granted) => granted ? FA.ConsentStatus.Granted : FA.ConsentStatus.Denied;

        // Values were already normalised by FirebaseAnalyticsSanitizer.SanitizeValue.
        private static FA.Parameter ToParameter(string key, object value)
        {
            switch (value)
            {
                case string s: return new FA.Parameter(key, s);
                case bool b: return new FA.Parameter(key, b ? 1L : 0L);
                case float f: return new FA.Parameter(key, (double)f);
                case double d: return new FA.Parameter(key, d);
                case decimal m: return new FA.Parameter(key, (double)m);
                case sbyte or byte or short or ushort or int or uint or long:
                    return new FA.Parameter(key, Convert.ToInt64(value));
                case IEnumerable<IDictionary<string, object>> items:
                    return new FA.Parameter(key, items);
                default:
                    return new FA.Parameter(key, value?.ToString() ?? string.Empty);
            }
        }

        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register() => FirebaseAnalyticsBackend.Current = new FirebaseAnalyticsSdkBackend();
    }
}
