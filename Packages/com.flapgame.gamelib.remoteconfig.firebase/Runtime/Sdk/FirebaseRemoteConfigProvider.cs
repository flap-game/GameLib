using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlapGame.GameLib.Firebase;
using UnityEngine;
using UnityEngine.Scripting;
using FRC = global::Firebase.RemoteConfig;

namespace FlapGame.GameLib.RemoteConfig
{
    /// <summary>
    /// Firebase Remote Config provider. Starts Firebase (shared start) and applies the fetch interval on the first
    /// SetDefaults / Fetch, retrying a failed start on the next call. Reads never touch Firebase before it started.
    /// Compiled only with the Firebase Remote Config package.
    /// </summary>
    public sealed class FirebaseRemoteConfigProvider : IRemoteConfigProvider
    {
        private readonly TimeSpan? minimumFetchInterval;
        private Task initialization;
        private volatile bool available;

        public FirebaseRemoteConfigProvider(TimeSpan? minimumFetchInterval) => this.minimumFetchInterval = minimumFetchInterval;

        private static FRC.FirebaseRemoteConfig Config => FRC.FirebaseRemoteConfig.DefaultInstance;

        public async Task SetDefaultsAsync(IReadOnlyDictionary<string, string> defaults, CancellationToken cancellationToken)
        {
            await EnsureInitializedAsync(cancellationToken);
            var values = new Dictionary<string, object>(defaults.Count);
            foreach (KeyValuePair<string, string> kv in defaults) values[kv.Key] = kv.Value;
            await Config.SetDefaultsAsync(values);
        }

        public async Task FetchAndActivateAsync(CancellationToken cancellationToken)
        {
            await EnsureInitializedAsync(cancellationToken);

            // The SDK takes no CancellationToken: race the fetch with the token.
            Task<bool> fetch = Config.FetchAndActivateAsync();
            if (cancellationToken.CanBeCanceled)
            {
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task finished = await Task.WhenAny(fetch, Task.Delay(Timeout.Infinite, delayCts.Token));
                delayCts.Cancel();
                if (finished != fetch) cancellationToken.ThrowIfCancellationRequested();
            }
            await fetch;
        }

        public bool TryGetRemoteString(string key, out string value)
        {
            value = null;
            if (!available || string.IsNullOrEmpty(key)) return false;
            FRC.ConfigValue v = Config.GetValue(key);
            if (v.Source != FRC.ValueSource.RemoteValue) return false;
            value = v.StringValue;
            return true;
        }

        public IReadOnlyDictionary<string, string> GetRemoteValues()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!available) return result;
            // DefaultValue is what the service pushed (module defaults + cache): caching it would pin today's defaults.
            foreach (KeyValuePair<string, FRC.ConfigValue> kv in Config.AllValues)
                if (kv.Value.Source == FRC.ValueSource.RemoteValue)
                    result[kv.Key] = kv.Value.StringValue;
            return result;
        }

        private Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            Task current = initialization;
            if (current == null || current.IsFaulted || current.IsCanceled)
                initialization = current = InitializeAsync(cancellationToken);
            return current;
        }

        private async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await FirebaseStartup.EnsureReadyAsync(cancellationToken);

            if (minimumFetchInterval.HasValue)
            {
                try
                {
                    FRC.ConfigSettings settings = Config.ConfigSettings;
                    settings.MinimumFetchIntervalInMilliseconds = (ulong)Math.Max(0, minimumFetchInterval.Value.TotalMilliseconds);
                    await Config.SetConfigSettingsAsync(settings);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RemoteConfig] Could not apply the fetch interval: {e.Message}");
                }
            }

            available = true;
        }

        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register() => FirebaseRemoteConfigBackend.Factory = interval => new FirebaseRemoteConfigProvider(interval);
    }
}
