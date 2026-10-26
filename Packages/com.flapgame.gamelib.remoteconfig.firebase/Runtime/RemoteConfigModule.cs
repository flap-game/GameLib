using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlapGame.GameLib.Firebase;
using UnityEngine;

namespace FlapGame.GameLib.RemoteConfig
{
    /// <summary>
    /// Registers <see cref="IRemoteConfig"/> backed by Firebase Remote Config. Values are readable at once (defaults from
    /// this asset, overridden by the last session's fetch); the fetch runs in the background, so await
    /// <see cref="IRemoteConfig.WhenReadyAsync"/> where fresh values matter. Installs before Ads, which reads its plan here.
    /// </summary>
    [CreateAssetMenu(menuName = "GameLib/Modules/Remote Config (Firebase)", fileName = "RemoteConfigModule")]
    public sealed class RemoteConfigModule : GameLibModule
    {
        [Serializable]
        public sealed class Entry
        {
            public string Key;

            [Tooltip("Text as in the Firebase console: a number, true/false, or JSON for GetObject.")]
            [TextArea(1, 6)] public string Value;
        }

        [Tooltip("In-app defaults: used until the first fetch ever lands, and for keys the server does not set.")]
        public List<Entry> Defaults = new();

        [Tooltip("Seconds the first fetch (Firebase start + fetch) may take before WhenReadyAsync completes with cached/default " +
                 "values. A fetch that lands later is still applied and raises Activated. 0 = no timeout.")]
        [Min(0f)] public float FetchTimeoutSeconds = 5f;

        [Tooltip("Firebase minimum time between two real fetches (server throttling), in seconds. The SDK default is 12 h, " +
                 "which makes console changes take up to half a day to reach players.")]
        [Min(0f)] public float MinimumFetchIntervalSeconds = 3600f;

        [Tooltip("No fetch throttling in the Editor and development builds, so console changes show up on the next launch.")]
        public bool NoFetchThrottleInDevelopment = true;

        [Tooltip("Fetch in the background so bootstrap does not wait on the network (default). Off = bootstrap waits until " +
                 "the fetch finished or timed out.")]
        public bool FetchInBackground = true;

        public override int Order => GameLibModuleOrder.RemoteConfig;

        public override async ValueTask InstallAsync(IServiceRegistry services, CancellationToken cancellationToken = default)
        {
            IRemoteConfigProvider provider = null;
            Func<TimeSpan?, IRemoteConfigProvider> factory = FirebaseRemoteConfigBackend.Factory;
            if (factory != null)
                provider = factory(TimeSpan.FromSeconds(EffectiveMinimumFetchIntervalSeconds()));
            else
                Debug.LogWarning("[RemoteConfig] The Firebase Remote Config package is not installed - serving defaults and cache only.");

            var defaults = new List<KeyValuePair<string, string>>();
            foreach (Entry e in Defaults)
                if (e != null && !string.IsNullOrEmpty(e.Key))
                    defaults.Add(new KeyValuePair<string, string>(e.Key, e.Value));

            TimeSpan timeout = FetchTimeoutSeconds > 0f ? TimeSpan.FromSeconds(FetchTimeoutSeconds) : Timeout.InfiniteTimeSpan;
            var service = new RemoteConfigService(provider, defaults, RemoteConfigCache.Default, timeout);
            services.Register<IRemoteConfig>(service);

            if (FetchInBackground)
            {
                _ = FetchInBackgroundAsync(service, cancellationToken);
                return;
            }

            await service.FetchAndActivateAsync(cancellationToken);
        }

        public override void Validate(ICollection<GameLibIssue> issues)
        {
            FirebaseProjectChecks.Validate(issues, "Remote Config",
                "Firebase.RemoteConfig.FirebaseRemoteConfig, Firebase.RemoteConfig", "Firebase Remote Config (com.google.firebase.remote-config)");

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (Entry e in Defaults)
            {
                if (e == null) continue;
                if (string.IsNullOrWhiteSpace(e.Key)) issues.Add(GameLibIssue.Error("Remote Config: a default has no key."));
                else if (!keys.Add(e.Key)) issues.Add(GameLibIssue.Error($"Remote Config: default '{e.Key}' is listed twice."));
            }
        }

        private float EffectiveMinimumFetchIntervalSeconds()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (NoFetchThrottleInDevelopment) return 0f;
#endif
            return Mathf.Max(0f, MinimumFetchIntervalSeconds);
        }

        private static async Task FetchInBackgroundAsync(RemoteConfigService service, CancellationToken cancellationToken)
        {
            try
            {
                await service.FetchAndActivateAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Bootstrap torn down - nothing to report.
            }
            catch (Exception e)
            {
                Debug.LogError($"[RemoteConfig] Background fetch failed: {e.Message}");
            }
        }
    }
}
