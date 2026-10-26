using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.RemoteConfig
{
    /// <summary>
    /// <see cref="IRemoteConfig"/> over a provider, with defaults, a disk cache of the last activation, a fetch timeout and
    /// readiness. Reads never block and never throw: the server value the provider has active, otherwise the cached
    /// server value from the last session, otherwise the default from the module asset, otherwise the caller's default -
    /// so values (the Ads plan, ...) are usable right at install. The cache holds server values only, so a changed default
    /// reaches existing users and a key removed in the console falls back to its default on the next launch.
    /// <para><see cref="WhenReadyAsync"/> completes after the first fetch whatever happens (success, failure, timeout, no
    /// provider). A fetch that lands after the timeout is still cached and raises <see cref="Activated"/>; until it ends,
    /// further fetch calls join it instead of starting another.</para>
    /// </summary>
    public sealed class RemoteConfigService : IRemoteConfig
    {
        private readonly IRemoteConfigProvider provider;
        private readonly RemoteConfigCache cache;
        private readonly TimeSpan fetchTimeout;
        private readonly Dictionary<string, string> fallback;
        private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // The real round trip (SetDefaults + fetch + activation) until it truly ends, past the caller's timeout too.
        private Task inFlight;
        private bool inFlightTimedOut;
        private bool defaultsApplied;

        /// <param name="provider">Null = defaults and cache only (no SDK); ready after the first fetch call.</param>
        /// <param name="fetchTimeout">≤ 0 or infinite = no timeout.</param>
        public RemoteConfigService(IRemoteConfigProvider provider, IEnumerable<KeyValuePair<string, string>> defaults, RemoteConfigCache cache, TimeSpan fetchTimeout)
        {
            this.provider = provider;
            this.cache = cache;
            this.fetchTimeout = fetchTimeout;

            // Defaults < cache: the last session's server values win.
            fallback = new Dictionary<string, string>(StringComparer.Ordinal);
            if (defaults != null)
                foreach (KeyValuePair<string, string> kv in defaults)
                    if (!string.IsNullOrEmpty(kv.Key))
                        fallback[kv.Key] = kv.Value ?? string.Empty;
            if (cache != null)
                foreach (KeyValuePair<string, string> kv in cache.Load())
                    fallback[kv.Key] = kv.Value;
        }

        public bool IsReady => ready.Task.IsCompleted;

        public event Action Activated;

        public Task WhenReadyAsync() => ready.Task;

        public ValueTask FetchAndActivateAsync(CancellationToken cancellationToken = default)
        {
            // Coalesce concurrent callers (module background fetch + a gameplay refresh) into one round trip, including a
            // fetch still running after its timeout.
            if (inFlight == null || inFlight.IsCompleted)
            {
                inFlightTimedOut = false;
                inFlight = provider == null ? Task.CompletedTask : FetchInternalAsync(cancellationToken);
            }
            return new ValueTask(WaitAsync(inFlight, cancellationToken));
        }

        // The caller's view of the round trip: done at the timeout, while the work itself goes on.
        private async Task WaitAsync(Task work, CancellationToken cancellationToken)
        {
            try
            {
                if (!work.IsCompleted && fetchTimeout > TimeSpan.Zero && fetchTimeout != Timeout.InfiniteTimeSpan)
                {
                    using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    Task finished = await Task.WhenAny(work, Task.Delay(fetchTimeout, delayCts.Token));
                    delayCts.Cancel();
                    if (finished != work)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        inFlightTimedOut = true;
                        Debug.LogWarning($"[RemoteConfig] Fetch timed out after {fetchTimeout.TotalSeconds:0.#}s - using cached/default values; a late result is still applied.");
                        return;
                    }
                }
                await work;
            }
            finally
            {
                ready.TrySetResult(true);
            }
        }

        // Never faults: a failure is logged here. Cancelled only through the token.
        private async Task FetchInternalAsync(CancellationToken cancellationToken)
        {
            try
            {
                await FetchCoreAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                Debug.LogWarning(inFlightTimedOut
                    ? $"[RemoteConfig] Late fetch failed - keeping cached/default values. {Describe(e)}"
                    : $"[RemoteConfig] Fetch failed - using cached/default values. {Describe(e)}");
                return;
            }

            if (inFlightTimedOut) Debug.Log("[RemoteConfig] Late fetch activated.");
            OnActivated();
        }

        private async Task FetchCoreAsync(CancellationToken cancellationToken)
        {
            if (!defaultsApplied)
            {
                // The provider serves the same defaults + cache (last server values) as the service reads.
                await provider.SetDefaultsAsync(new Dictionary<string, string>(fallback, StringComparer.Ordinal), cancellationToken);
                defaultsApplied = true;
            }
            await provider.FetchAndActivateAsync(cancellationToken);
        }

        private void OnActivated()
        {
            try
            {
                // Server values only: a cached default would beat the module defaults on every later launch. Saved even
                // when empty, so keys removed in the console fall back to the defaults next launch.
                IReadOnlyDictionary<string, string> remote = provider.GetRemoteValues();
                if (remote != null) cache?.Save(remote);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoteConfig] Could not read the activated values: {e.Message}");
            }

            Action handlers = Activated;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try { ((Action)handler)(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        // ── Reads ────────────────────────────────────────────────────────

        /// <summary>The raw value: provider's server value, then cache, then defaults. False when no source has the key.</summary>
        public bool TryGetString(string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(key)) return false;
            try
            {
                if (provider != null && provider.TryGetRemoteString(key, out value)) return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoteConfig] Reading '{key}' failed: {e.Message}");
            }
            return fallback.TryGetValue(key, out value);
        }

        public string GetString(string key, string defaultValue = "") =>
            TryGetString(key, out string value) ? value : defaultValue;

        public bool GetBool(string key, bool defaultValue = false) =>
            TryGetString(key, out string value) && TryParseBool(value, out bool result) ? result : defaultValue;

        public long GetLong(string key, long defaultValue = 0L) =>
            TryGetString(key, out string value) && long.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long result)
                ? result
                : defaultValue;

        public double GetDouble(string key, double defaultValue = 0.0) =>
            TryGetString(key, out string value) && double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
                ? result
                : defaultValue;

        public T GetObject<T>(string key, T defaultValue = default) =>
            TryGetString(key, out string value) ? RemoteConfigJson.Deserialize(value, defaultValue) : defaultValue;

        /// <summary>Firebase's boolean rules: 1/true/t/yes/y/on and 0/false/f/no/n/off, any case.</summary>
        public static bool TryParseBool(string value, out bool result)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "1": case "true": case "t": case "yes": case "y": case "on":
                    result = true;
                    return true;
                case "0": case "false": case "f": case "no": case "n": case "off":
                    result = false;
                    return true;
                default:
                    result = false;
                    return false;
            }
        }

        private static string Describe(Exception e)
        {
            while ((e is AggregateException || e is TypeInitializationException) && e.InnerException != null)
                e = e.InnerException;
            return e.Message;
        }
    }
}
