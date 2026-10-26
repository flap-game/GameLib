using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlapGame.GameLib.RemoteConfig
{
    /// <summary>
    /// A remote config backend (Firebase). <see cref="RemoteConfigService"/> wraps it with defaults, the disk cache, the
    /// fetch timeout and readiness; game code only sees <see cref="IRemoteConfig"/>. Providers start themselves on the
    /// first <see cref="SetDefaultsAsync"/> / <see cref="FetchAndActivateAsync"/>, so a hanging start stays inside the
    /// service's timeout.
    /// </summary>
    public interface IRemoteConfigProvider
    {
        Task SetDefaultsAsync(IReadOnlyDictionary<string, string> defaults, CancellationToken cancellationToken);

        Task FetchAndActivateAsync(CancellationToken cancellationToken);

        /// <summary>The active value; false while the provider is not started or has no value for <paramref name="key"/>.</summary>
        bool TryGetString(string key, out string value);

        /// <summary>Every active key and value (fetched and defaults), saved to the disk cache after each activation.</summary>
        IReadOnlyDictionary<string, string> GetAll();
    }

    public static class FirebaseRemoteConfigBackend
    {
        /// <summary>
        /// Creates the Firebase provider with a minimum fetch interval (null = SDK default, 12 h). Set before the first
        /// scene loads when the Firebase Remote Config package is compiled in; null otherwise.
        /// </summary>
        public static Func<TimeSpan?, IRemoteConfigProvider> Factory { get; set; }
    }
}
