using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AppsFlyerSDK;
using UnityEngine;
using UnityEngine.Scripting;
using AF = AppsFlyerSDK.AppsFlyer;

namespace FlapGame.GameLib.AppsFlyer
{
    /// <summary>
    /// The real <see cref="IAppsFlyerBackend"/>, for the AppsFlyer Unity plugin 6.17.x (synchronous API) and 7.x (async
    /// <c>Awaitable</c> API, <c>GAMELIB_APPSFLYER_7</c>). Compiled only when the plugin is installed.
    /// </summary>
    public sealed class AppsFlyerSdkBackend : IAppsFlyerBackend
    {
        private MonoBehaviour host;

        public string Version => AF.kAppsFlyerPluginVersion;

#if GAMELIB_APPSFLYER_7
        public async Task InitializeAsync(string devKey, string appId, bool debug)
        {
            await AF.init(devKey, appId);
            if (debug) await AF.enableDebug(true);
        }

        public async Task StartAsync() => await AF.start();

        public void SetCustomerUserId(string userId) => Run(() => AF.setCustomerUserId(userId));

        public void LogEvent(string name, Dictionary<string, string> values) => Run(() => AF.logEvent(name, values));

        public void LogAdRevenue(string network, AdMediation mediation, string currency, double revenue, Dictionary<string, string> values) =>
            Run(() => AF.logAdRevenue(new AFAdRevenueData(network, Map(mediation), currency, revenue), values));

        public void SetConsent(AppsFlyerConsentData consent) => Run(() => AF.setConsentData(ToSdk(consent)));

        public void EnableTcfDataCollection(bool enabled) => Run(() => AF.enableTCFDataCollection(enabled));

        // Awaitables report failures only to whoever awaits them.
        private static async void Run(Func<Awaitable> call)
        {
            try { await call(); }
            catch (Exception e) { Debug.LogWarning($"[AppsFlyer] {e.Message}"); }
        }
#else
        public Task InitializeAsync(string devKey, string appId, bool debug)
        {
            AF.setIsDebug(debug);
            AF.initSDK(devKey, appId);
            return Task.CompletedTask;
        }

        public Task StartAsync()
        {
            AF.startSDK();
            return Task.CompletedTask;
        }

        public void SetCustomerUserId(string userId) => AF.setCustomerUserId(userId);

        public void LogEvent(string name, Dictionary<string, string> values) => AF.sendEvent(name, values);

        public void LogAdRevenue(string network, AdMediation mediation, string currency, double revenue, Dictionary<string, string> values) =>
            AF.logAdRevenue(new AFAdRevenueData(network, Map(mediation), currency, revenue), values);

        public void SetConsent(AppsFlyerConsentData consent) => AF.setConsentData(ToSdk(consent));

        public void EnableTcfDataCollection(bool enabled) => AF.enableTCFDataCollection(enabled);
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool? purchaseConnectorAvailable;

        public bool IsPurchaseConnectorAvailable => purchaseConnectorAvailable ??= HasAndroidPurchaseClient();

        // Plugin 7.0.x comments com.appsflyer:purchase-connector out of AppsFlyerDependencies.xml (AGP 8 namespace clash);
        // its C# calls then only log a warning. The library's PurchaseClient class tells whether it made it into the build.
        private static bool HasAndroidPurchaseClient()
        {
            try
            {
                using var purchaseClient = new AndroidJavaClass("com.appsflyer.api.PurchaseClient");
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
#else
        public bool IsPurchaseConnectorAvailable => true;
#endif

        public void StartPurchaseConnector(bool sandbox, bool storeKit2)
        {
            // iOS routes the connector's callbacks to a GameObject by name.
            if (host == null)
            {
                var go = new GameObject("GameLibAppsFlyerPurchaseConnector");
                UnityEngine.Object.DontDestroyOnLoad(go);
                host = go.AddComponent<PurchaseConnectorHost>();
            }

            AppsFlyerPurchaseConnector.init(host, Store.GOOGLE);
            AppsFlyerPurchaseConnector.setIsSandbox(sandbox);
            AppsFlyerPurchaseConnector.setStoreKitVersion(storeKit2 ? StoreKitVersion.SK2 : StoreKitVersion.SK1);
            AppsFlyerPurchaseConnector.setAutoLogPurchaseRevenue(
                AppsFlyerAutoLogPurchaseRevenueOptions.AppsFlyerAutoLogPurchaseRevenueOptionsAutoRenewableSubscriptions,
                AppsFlyerAutoLogPurchaseRevenueOptions.AppsFlyerAutoLogPurchaseRevenueOptionsInAppPurchases);
            AppsFlyerPurchaseConnector.build();
            AppsFlyerPurchaseConnector.startObservingTransactions();
        }

        private static MediationNetwork Map(AdMediation mediation) => mediation switch
        {
            AdMediation.ApplovinMax => MediationNetwork.ApplovinMax,
            AdMediation.GoogleAdMob => MediationNetwork.GoogleAdMob,
            AdMediation.IronSource => MediationNetwork.IronSource,
            _ => MediationNetwork.Custom,
        };

        private static AppsFlyerConsent ToSdk(AppsFlyerConsentData consent) =>
            new(consent.IsUserSubjectToGdpr, consent.HasConsentForDataUsage, consent.HasConsentForAdsPersonalization, consent.HasConsentForAdStorage);

        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register() => AppsFlyerBackend.Current = new AppsFlyerSdkBackend();

        /// <summary>Callback target of the Purchase Connector.</summary>
        private sealed class PurchaseConnectorHost : MonoBehaviour
        {
        }
    }
}
