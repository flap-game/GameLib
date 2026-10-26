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

        /// <summary>
        /// The active value the server sent; false while the provider is not started or the server does not set
        /// <paramref name="key"/> (defaults are the service's job, so a value from <see cref="SetDefaultsAsync"/> is not one).
        /// </summary>
        bool TryGetRemoteString(string key, out string value);

        /// <summary>
        /// Every active key and value the server sent - never defaults - saved to the disk cache after each activation, so
        /// a default changed in an app update and a key removed in the console fall back to the in-app default next launch.
        /// </summary>
        IReadOnlyDictionary<string, string> GetRemoteValues();
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
