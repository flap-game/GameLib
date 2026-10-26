using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.AppsFlyer
{
    /// <summary>
    /// Adds the AppsFlyer sink to <see cref="IAnalytics"/> at once and starts AppsFlyer in the background in the order it
    /// needs (<see cref="AppsFlyerStartup"/>): CMP answer → consent → App Tracking Transparency answer (the ATT module
    /// shows the prompt) → customer user id → start. Everything tracked meanwhile is buffered and sent after the start.
    /// </summary>
    [CreateAssetMenu(menuName = "GameLib/Modules/AppsFlyer", fileName = "AppsFlyerModule")]
    public sealed class AppsFlyerModule : GameLibModule
    {
        public const string LogTag = "[AppsFlyer]";

        [Header("App")]
        [Tooltip("AppsFlyer dashboard > App settings > Dev key.")]
        public string DevKey;

        [Tooltip("Numeric Apple app id (App Store Connect > App information), without 'id'. iOS only.")]
        public string IosAppId;

        [Tooltip("Verbose AppsFlyer logs in the Editor and development builds.")]
        public bool DebugLogs;

        [Header("Consent")]
        [Tooltip("Start only after the CMP (installed with the Ads module) answered, so the install carries the user's choice.")]
        public bool WaitForConsent = true;

        [Tooltip("Longest wait for the CMP answer, in seconds.")]
        [Min(0f)] public float ConsentWaitSeconds = 60f;

        [Tooltip("ConsentState: GameLib sends the CMP answer as AppsFlyer DMA consent. Tcf: the SDK reads the TCF strings " +
                 "of a TCF v2.2 CMP (Google UMP, MAX's flow) itself.")]
        public AppsFlyerConsentSource ConsentSource = AppsFlyerConsentSource.ConsentState;

        [Header("iOS App Tracking Transparency")]
        [Tooltip("Start only after the ATT prompt was answered (shown by the ATT module, or by another SDK such as MAX's consent " +
                 "flow), so the install carries the IDFA. Off: start at once - the first session then never has the IDFA.")]
        public bool WaitForAtt = true;

        [Tooltip("Longest wait for the ATT answer, in seconds. Keep it above the ATT module's delay.")]
        [Min(0f)] public float AttWaitSeconds = 60f;

        [Header("Purchases")]
        [Tooltip("Manual: GameLib logs af_purchase for every purchase. PurchaseConnector: AppsFlyer's ROI360 connector validates " +
                 "and logs store purchases itself (paid plan - do not combine with Manual). None: no purchase revenue.")]
        public AppsFlyerPurchaseMode PurchaseMode = AppsFlyerPurchaseMode.Manual;

        [Tooltip("Purchase Connector: validate against the store sandbox. Development builds only.")]
        public bool PurchaseConnectorSandbox;

        [Tooltip("Purchase Connector on iOS: StoreKit 2 (iOS 15+). Off = StoreKit 1.")]
        public bool UseStoreKit2;

        [Header("Events")]
        [Tooltip("Send every IAnalytics event to AppsFlyer. Off = only the events listed below (plus ad revenue and purchases).")]
        public bool ForwardAllEvents;

        [Tooltip("Game events sent to AppsFlyer, e.g. level_complete, tutorial_complete.")]
        public List<string> ForwardedEvents = new();

        private static readonly TimeSpan BootstrapWait = TimeSpan.FromSeconds(30);

        public override int Order => GameLibModuleOrder.Analytics;

        public override ValueTask InstallAsync(IServiceRegistry services, CancellationToken cancellationToken = default)
        {
            IAppsFlyerBackend backend = AppsFlyerBackend.Current;
            if (backend == null)
            {
                Debug.LogWarning($"{LogTag} The AppsFlyer plugin is not installed - no attribution.");
                return default;
            }
            if (string.IsNullOrWhiteSpace(DevKey))
            {
                Debug.LogError($"{LogTag} Dev Key is empty on the AppsFlyer module - AppsFlyer does not start.");
                return default;
            }

            AppsFlyerPurchaseMode purchaseMode = PurchaseMode;
            if (purchaseMode == AppsFlyerPurchaseMode.PurchaseConnector && !backend.IsPurchaseConnectorAvailable)
            {
                // Plugin 7.0.x leaves the Android connector out of its dependencies; its calls would only log a warning.
                Debug.LogWarning($"{LogTag} The Purchase Connector is not in this build - logging af_purchase for purchases instead.");
                purchaseMode = AppsFlyerPurchaseMode.Manual;
            }

            var sink = new AppsFlyerSink(backend, purchaseMode, ForwardAllEvents, ForwardedEvents);
            // Only buffer the events AppsFlyer sends, so dropped game events never crowd out ad revenue and purchases.
            var buffer = new BufferedAnalyticsSink<AppsFlyerSink>(sink, LogTag, bufferEvent: sink.Forwards);
            services.Resolve<IAnalyticsSinkRegistry>().AddSink(buffer);

            services.TryResolve(out IConsentState consent);
            if (consent != null && ConsentSource == AppsFlyerConsentSource.ConsentState)
            {
                // Later changes (privacy options form); the first answer is sent by the startup before the start.
                void Apply(ConsentState state) => buffer.RunLatest("consent", s => s.ApplyConsent(state));
                consent.Changed += Apply;
                services.RegisterDisposable(Disposable.Create(() => consent.Changed -= Apply));
            }

            services.TryResolve(out IAttService att);
            AppsFlyerStartup.Options options = BuildOptions();
            options.PurchaseMode = purchaseMode;
            var startup = new AppsFlyerStartup(backend, consent, att, options, WaitForBootstrapAsync,
                                               () => services.IsRegistered<IConsentService>(),
                                               () => buffer.TryGetPendingUserId(out string userId) ? userId : null);
            // Past this the buffer warns once and keeps waiting: a later start still sends everything buffered.
            TimeSpan timeout = BootstrapWait + startup.MaxWait + TimeSpan.FromSeconds(30);
            Debug.Log($"{LogTag} Plugin {backend.Version}; starting after consent and ATT.");
            _ = buffer.InitializeAsync(startup.RunAsync, timeout, expectedFailure: Application.isEditor, cancellationToken);
            return default;
        }

        public AppsFlyerStartup.Options BuildOptions() => new()
        {
            DevKey = DevKey?.Trim(),
            AppId = IosAppId?.Trim(),
            Debug = DebugLogs && Debug.isDebugBuild,
            WaitForConsent = WaitForConsent,
            ConsentWait = TimeSpan.FromSeconds(ConsentWaitSeconds),
            ConsentSource = ConsentSource,
            WaitForAtt = WaitForAtt,
            AttWait = TimeSpan.FromSeconds(AttWaitSeconds),
            PurchaseMode = PurchaseMode,
            PurchaseConnectorSandbox = PurchaseConnectorSandbox && Debug.isDebugBuild,
            StoreKit2 = UseStoreKit2,
        };

        public override void Validate(ICollection<GameLibIssue> issues)
        {
            if (Type.GetType("AppsFlyerSDK.AppsFlyer, AppsFlyer") == null)
                issues.Add(GameLibIssue.Error("AppsFlyer: the AppsFlyer Unity plugin (appsflyer-unity-plugin) is not installed - install it from GameLib > Setup."));
            if (string.IsNullOrWhiteSpace(DevKey))
                issues.Add(GameLibIssue.Error("AppsFlyer: Dev Key is empty."));
            if (string.IsNullOrWhiteSpace(IosAppId))
                issues.Add(GameLibIssue.Warning("AppsFlyer: iOS App Id is empty - required for iOS builds."));
            else if (!long.TryParse(IosAppId.Trim(), out _))
                issues.Add(GameLibIssue.Error("AppsFlyer: iOS App Id must be the numeric Apple id, without 'id'."));
            if (PurchaseMode == AppsFlyerPurchaseMode.Manual && ForwardAllEvents)
                issues.Add(GameLibIssue.Warning("AppsFlyer: Forward All Events also sends iap_* events; af_purchase already carries the revenue."));
        }

        private static async Task WaitForBootstrapAsync()
        {
            try
            {
                await GameLibBootstrap.WhenReadyAsync(BootstrapWait);
            }
            catch (Exception)
            {
                // A failed or stuck bootstrap still deserves the launch report.
            }
        }
    }
}
