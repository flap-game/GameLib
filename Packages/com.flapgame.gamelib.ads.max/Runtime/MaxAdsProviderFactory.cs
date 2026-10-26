using UnityEngine;
using UnityEngine.Scripting;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Entry point of the AppLovin MAX provider. Registers itself into <see cref="AdsProviderCatalog"/> at
    /// <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/>, so installing the MAX package (which compiles this
    /// assembly through the asmdef <c>versionDefines</c> -> <c>GAMELIB_MAX</c>) is all a game does.
    /// <para>
    /// Consent: MAX's CMP (<see cref="MaxConsentService"/>, priority 50) runs only when no higher-priority CMP is
    /// installed. MAX's own consent flow ("MAX Terms and Privacy Policy Flow" / Google UMP in the AppLovin Integration
    /// Manager) is a BUILD-TIME setting (<c>AppLovinInternalSettings.ConsentFlowEnabled</c>, written into the Android
    /// manifest / iOS plist by the AppLovin post-processors) and runs inside <c>MaxSdk.InitializeSdk()</c>; MAX 8.6.6 has
    /// no runtime switch for it. When AdMob is installed too, AdMob's UMP (priority 100) runs the one consent flow, so
    /// the game MUST disable the MAX consent flow in the Integration Manager, otherwise the user sees two flows.
    /// </para>
    /// </summary>
    [Preserve]
    public sealed class MaxAdsProviderFactory : IAdsProviderFactory
    {
        /// <inheritdoc />
        public string ProviderId => AdProviderIds.Max;

        /// <inheritdoc />
        public string DisplayName => "AppLovin MAX";

        /// <inheritdoc />
        public int ConsentPriority => 50;

        /// <inheritdoc />
        public IAdsProviderAdapter CreateAdapter() => new MaxAdsAdapter();

        /// <inheritdoc />
        public IConsentService CreateConsentService(AdsModule settings) => new MaxConsentService();

        /// <summary>Registers this factory before any scene loads (also after a domain reload with Enter Play Mode options).</summary>
        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Register() => AdsProviderCatalog.Register(new MaxAdsProviderFactory());
    }
}
