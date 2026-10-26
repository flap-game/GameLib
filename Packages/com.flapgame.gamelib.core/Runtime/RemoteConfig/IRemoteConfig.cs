using System;
using System.Threading;
using System.Threading.Tasks;

namespace FlapGame.GameLib
{
    /// <summary>
    /// Remote values (Firebase Remote Config, ...), registered by a remote config module. Values are readable right after
    /// registration: defaults from the module asset, overridden by the values cached from the last successful fetch, until
    /// a fresh fetch activates (<see cref="Activated"/>). Other modules use it too - the Ads module reads its plan from it.
    /// Resolve it with <c>TryResolve</c>: it is absent when no remote config module is installed.
    /// </summary>
    public interface IRemoteConfig
    {
        /// <summary>True once the first fetch finished, failed or timed out (values may still be cached/defaults).</summary>
        bool IsReady { get; }

        /// <summary>Raised on the main thread every time fetched values become active.</summary>
        event Action Activated;

        /// <summary>Completes when <see cref="IsReady"/> becomes true. Never faults.</summary>
        Task WhenReadyAsync();

        /// <summary>Fetches and activates now (concurrent calls share one round trip).</summary>
        ValueTask FetchAndActivateAsync(CancellationToken cancellationToken = default);

        string GetString(string key, string defaultValue = "");

        bool GetBool(string key, bool defaultValue = false);

        long GetLong(string key, long defaultValue = 0L);

        double GetDouble(string key, double defaultValue = 0.0);

        /// <summary>The value parsed as JSON (enums by name, unknown fields ignored); <paramref name="defaultValue"/> when absent or malformed.</summary>
        T GetObject<T>(string key, T defaultValue = default);
    }
}
