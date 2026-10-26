using System.Collections.Generic;
using System.Threading.Tasks;

namespace FlapGame.GameLib.AppsFlyer
{
    /// <summary>
    /// The AppsFlyer calls GameLib makes, behind an interface so the module logic is testable without the plugin. The real
    /// one lives in <c>FlapGame.GameLib.AppsFlyer.Sdk</c> (AppsFlyer Unity plugin 6.17.x or 7.x) and registers itself in
    /// <see cref="AppsFlyerBackend.Current"/> when the plugin is installed.
    /// </summary>
    public interface IAppsFlyerBackend
    {
        /// <summary>Plugin version, for the log.</summary>
        string Version { get; }

        /// <summary>Creates the SDK (no network yet). <paramref name="appId"/> is the numeric Apple id, null on Android.</summary>
        Task InitializeAsync(string devKey, string appId, bool debug);

        /// <summary>Sends the launch: install / session. Everything logged before it is held by the SDK.</summary>
        Task StartAsync();

        void SetCustomerUserId(string userId);

        void LogEvent(string name, Dictionary<string, string> values);

        void LogAdRevenue(string network, AdMediation mediation, string currency, double revenue, Dictionary<string, string> values);

        void SetConsent(AppsFlyerConsentData consent);

        /// <summary>The SDK reads the TCF v2.2 strings the CMP stored instead of <see cref="SetConsent"/>.</summary>
        void EnableTcfDataCollection(bool enabled);

        /// <summary>
        /// False when the Purchase Connector's native library is missing from the build (plugin 7.0.x on Android), so
        /// <see cref="StartPurchaseConnector"/> would do nothing and purchases must be logged as <c>af_purchase</c>.
        /// </summary>
        bool IsPurchaseConnectorAvailable { get; }

        /// <summary>Starts AppsFlyer's ROI360 Purchase Connector (needs an ROI360 subscription).</summary>
        void StartPurchaseConnector(bool sandbox, bool storeKit2);
    }

    public static class AppsFlyerBackend
    {
        /// <summary>Set before the first scene loads when the plugin is compiled in; null otherwise.</summary>
        public static IAppsFlyerBackend Current { get; set; }
    }

    /// <summary>Mediation platform for <c>logAdRevenue</c>.</summary>
    public enum AdMediation
    {
        Custom = 0,
        ApplovinMax = 1,
        GoogleAdMob = 2,
        IronSource = 3,
    }

    /// <summary>DMA consent as AppsFlyer takes it (null = not set).</summary>
    public readonly struct AppsFlyerConsentData
    {
        public AppsFlyerConsentData(bool isUserSubjectToGdpr, bool? hasConsentForDataUsage, bool? hasConsentForAdsPersonalization, bool? hasConsentForAdStorage)
        {
            IsUserSubjectToGdpr = isUserSubjectToGdpr;
            HasConsentForDataUsage = hasConsentForDataUsage;
            HasConsentForAdsPersonalization = hasConsentForAdsPersonalization;
            HasConsentForAdStorage = hasConsentForAdStorage;
        }

        public bool IsUserSubjectToGdpr { get; }

        public bool? HasConsentForDataUsage { get; }

        public bool? HasConsentForAdsPersonalization { get; }

        public bool? HasConsentForAdStorage { get; }

        /// <summary>Null while no CMP has answered. GDPR users: every flag follows their consent; others: not subject to GDPR.</summary>
        public static AppsFlyerConsentData? From(ConsentState state)
        {
            if (state == null || !state.IsResolved) return null;
            if (!state.GdprApplies) return new AppsFlyerConsentData(false, null, null, null);
            bool granted = state.HasUserConsent;
            return new AppsFlyerConsentData(true, granted, granted && !state.IsAgeRestrictedUser, granted);
        }

        public override string ToString() =>
            $"gdpr={IsUserSubjectToGdpr} dataUsage={HasConsentForDataUsage} adsPersonalization={HasConsentForAdsPersonalization} adStorage={HasConsentForAdStorage}";
    }
}
