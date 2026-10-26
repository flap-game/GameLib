using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.AppsFlyer
{
    /// <summary>
    /// Adds the AppsFlyer sink to <see cref="IAnalytics"/> at once and starts AppsFlyer in the background in the order it
    /// needs (<see cref="AppsFlyerStartup"/>): CMP answer → consent → App Tracking Transparency → start. Everything tracked
    /// meanwhile is buffered and sent after the start.
    /// </summary>
    [CreateAssetMenu(menuName = "GameLib/Modules/AppsFlyer", fileName = "AppsFlyerModule")]
    public sealed class AppsFlyerModule : GameLibModule
    {
        public const string LogTag = "[AppsFlyer]";
        public const string DefaultAttUsageDescription = "Your data will be used to deliver personalized ads to you.";

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
        [Tooltip("Request: GameLib shows the prompt after the CMP answer. WaitForOther: another SDK shows it (MAX's consent flow). " +
                 "DoNotWait: start at once (the first session then never has the IDFA).")]
        public AttMode Att = AttMode.Request;

        [Tooltip("Longest wait for the prompt answer, in seconds.")]
        [Min(0f)] public float AttWaitSeconds = 60f;

        [Tooltip("Info.plist NSUserTrackingUsageDescription: the sentence the prompt shows. Added at build time when Att = Request.")]
        [TextArea(2, 4)] public string AttUsageDescription = DefaultAttUsageDescription;

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

            var sink = new AppsFlyerSink(backend, PurchaseMode, ForwardAllEvents, ForwardedEvents);
            var buffer = new BufferedAnalyticsSink<AppsFlyerSink>(sink, LogTag);
            services.Resolve<IAnalyticsSinkRegistry>().AddSink(buffer);

            services.TryResolve(out IConsentState consent);
            if (consent != null && ConsentSource == AppsFlyerConsentSource.ConsentState)
            {
                // Later changes (privacy options form); the first answer is sent by the startup before the start.
                void Apply(ConsentState state) => buffer.Run(s => s.ApplyConsent(state));
                consent.Changed += Apply;
                services.RegisterDisposable(Disposable.Create(() => consent.Changed -= Apply));
            }

            var startup = new AppsFlyerStartup(backend, consent, new AttPrompt(), BuildOptions(), WaitForBootstrapAsync,
                                               () => services.IsRegistered<IConsentService>());
            TimeSpan timeout = startup.MaxWait + TimeSpan.FromSeconds(30);
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
            Att = Att,
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
            if (Att == AttMode.Request && string.IsNullOrWhiteSpace(AttUsageDescription))
                issues.Add(GameLibIssue.Error("AppsFlyer: ATT Usage Description is empty - iOS kills an app that shows the prompt without it."));
            if (PurchaseMode == AppsFlyerPurchaseMode.Manual && ForwardAllEvents)
                issues.Add(GameLibIssue.Warning("AppsFlyer: Forward All Events also sends iap_* events; af_purchase already carries the revenue."));
        }

        private static async Task WaitForBootstrapAsync()
        {
            try
            {
                await GameLibBootstrap.WhenReadyAsync(TimeSpan.FromSeconds(30));
            }
            catch (Exception)
            {
                // A failed or stuck bootstrap still deserves the launch report.
            }
        }
    }
}
