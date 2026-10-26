namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Well-known ad provider ids. A provider id is a lowercase string so a new SDK can be added as its own assembly
    /// without editing this package (register an <c>IAdsProviderFactory</c> with a new id). These constants only
    /// name the providers that ship with GameLib.
    /// </summary>
    public static class AdProviderIds
    {
        /// <summary>The Null/mock adapter: fake ads in the Editor and development builds, nothing in release.</summary>
        public const string None      = "none";
        public const string AdMob     = "admob";
        public const string Max       = "max";
        public const string LevelPlay = "levelplay";

        /// <summary>Lowercases and trims <paramref name="id"/>; null/blank becomes <see cref="None"/>.</summary>
        public static string Normalize(string id) =>
            string.IsNullOrWhiteSpace(id) ? None : id.Trim().ToLowerInvariant();

        /// <summary>Maps the pre-0.6 enum to its id.</summary>
        public static string FromLegacy(AdProviderType type) => type switch
        {
            AdProviderType.AdMob => AdMob,
            AdProviderType.Max   => Max,
            _                    => None,
        };

        /// <summary>Maps an id back to the pre-0.6 enum (None for any provider the enum never knew).</summary>
        public static AdProviderType ToLegacy(string id) => Normalize(id) switch
        {
            AdMob => AdProviderType.AdMob,
            Max   => AdProviderType.Max,
            _     => AdProviderType.None,
        };
    }
}
