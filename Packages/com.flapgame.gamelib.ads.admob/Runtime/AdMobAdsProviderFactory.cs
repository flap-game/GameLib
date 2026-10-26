using UnityEngine;
using UnityEngine.Scripting;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Registers Google AdMob in <see cref="AdsProviderCatalog"/>. This assembly compiles only when the Google Mobile
    /// Ads package is installed (asmdef <c>versionDefines</c> -> <c>GAMELIB_ADMOB</c>), so installing the SDK is all a game
    /// does to make the <c>admob</c> provider id available.
    /// <para>
    /// The AdMob APP id is not a runtime value: it lives in the Google Mobile Ads settings asset
    /// (Assets &gt; Google Mobile Ads &gt; Settings) and is written into the Android manifest / iOS Info.plist at build
    /// time. <see cref="AdsProviderContext.AppKey"/> is therefore ignored by this provider; only the per-platform ad
    /// unit ids of the routes are used.
    /// </para>
    /// </summary>
    [Preserve]
    public sealed class AdMobAdsProviderFactory : IAdsProviderFactory
    {
        /// <inheritdoc />
        public string ProviderId => AdProviderIds.AdMob;

        /// <inheritdoc />
        public string DisplayName => "Google AdMob";

        /// <summary>Google UMP is the preferred CMP: it writes the IAB TCF string every other SDK reads.</summary>
        public int ConsentPriority => 100;

        /// <inheritdoc />
        public IAdsProviderAdapter CreateAdapter() => new AdMobAdsAdapter();

        /// <summary>Google UMP (<see cref="AdMobConsentService"/>).</summary>
        public IConsentService CreateConsentService(AdsModule settings) => new AdMobConsentService(settings);

        /// <summary>Adds this factory to <see cref="AdsProviderCatalog"/> before any scene loads.</summary>
        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Register() => AdsProviderCatalog.Register(new AdMobAdsProviderFactory());
    }
}
