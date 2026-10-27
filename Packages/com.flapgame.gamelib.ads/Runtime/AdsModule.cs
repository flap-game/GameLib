using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// The Ads module: placement table, providers, policy - and the installer that builds the ads stack from them.
    /// <para>
    /// Providers are plugins: every provider package (ads.max, ads.admob) registers an <see cref="IAdsProviderFactory"/>
    /// in <see cref="AdsProviderCatalog"/>. The selected providers are catalog ∩ enabled <see cref="Providers"/> rows ∩ the
    /// plan's <see cref="AdsPlan.Providers"/> (when an <see cref="IAdsPlanSource"/> is registered and lists any). The mock
    /// provider <c>none</c> stands in for every placement when nothing else is selected (fake ads only in the Editor and
    /// development builds, nothing in release).
    /// </para>
    /// <para>
    /// Order at startup: plan (no network) → provider selection → consent platform of the highest-priority selected
    /// factory → every adapter initialized in parallel. No-ads comes from <see cref="Entitlements.NoAds"/>, revenue goes to
    /// <see cref="IAnalytics.TrackAdRevenue"/>, consent is published to <see cref="IConsentState"/>.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "GameLib/Modules/Ads", fileName = "AdsModule")]
    public sealed class AdsModule : GameLibModule
    {
        [Serializable]
        public sealed class ConsentDebugConfig
        {
            [Tooltip("Enable to force a consent geography for testing. Disable in production builds.")]
            public bool Enabled;
            [Tooltip("Simulated geography. EEA triggers the consent form.")]
            public ConsentDebugGeography Geography = ConsentDebugGeography.EEA;
            [Tooltip("Hashed test-device IDs. Find yours in logcat: 'Use RequestConfiguration.Builder.setTestDeviceIds'.")]
            public List<string> TestDeviceIds = new();
        }

        [Header("Providers")]
        [Tooltip("Every ad SDK this game may use, with its switch and keys. A provider that is not listed here is still " +
                 "used when a route names it and its package is installed (enabled, no app key).")]
        public List<AdsProviderConfig> Providers = new();

        [Tooltip("Primary = only the first route of a placement loads; the next route loads after it fails (default). " +
                 "Parallel = every route keeps an ad loaded.")]
        public AdLoadStrategy LoadStrategy = AdLoadStrategy.Primary;

        [Header("Policy")]
        public AdsPolicy Policy = new();

        [Header("App Open")]
        [Tooltip("App open placement shown at launch (not on the first launch after install) and on every return from the " +
                 "background - never on the way back from an ad, ad click, purchase or link. Empty = no automatic app open ad.")]
        public string AppOpenPlacementId;

        [Tooltip("How long a launch waits for the app open ad to load; after that the launch shows none.")]
        [Min(0f)] public float AppOpenColdStartWaitSeconds = 10f;

        [Tooltip("First launch (counted since install) that shows the launch app open ad: 1 = every launch, 2 = all but the " +
                 "first (shared rule), 5 = from the 5th launch. Returns from the background are not affected.")]
        [Min(1)] public int AppOpenShowAtLaunchFrom = 2;

        [Tooltip("On: the launch ad shows by itself as soon as it loads. Off: the game shows it from its loading screen with " +
                 "AppOpenLauncher.ShowLaunchAdAsync() and continues when that returns (ad closed, or none loaded in time).")]
        public bool AppOpenAutoShowAtLaunch = true;

        [Tooltip("Minimum seconds in the background before a return shows an app open ad. 0 = every return.")]
        [Min(0f)] public float AppOpenMinBackgroundSeconds;

        [Tooltip("PlayerPrefs key of the launch counter. Empty = GameLib's own key. A game moving to GameLib sets its old key " +
                 "(e.g. GameAds_LaunchCount) so its players keep their count.")]
        public string AppOpenLaunchCountKey;

        [Header("While A Fullscreen Ad Is Up")]
        [Tooltip("Time.timeScale = 0 while an interstitial, rewarded or app open ad is on screen; put back when it closes. " +
                 "Leave off when the game already pauses on FullscreenShowingChanged.")]
        public bool PauseGameDuringFullscreenAds;

        [Tooltip("AudioListener.pause while a fullscreen ad is on screen, so game music does not play under the ad's sound.")]
        public bool MuteAudioDuringFullscreenAds;

        [Header("Startup")]
        [Tooltip("Run consent + SDK initialization in the background so bootstrap is not blocked by network round-trips.")]
        public bool InitializeInBackground = true;

        [Tooltip("Seconds to wait for the consent answer before moving on. Never applies while the consent form is on screen. " +
                 "After the timeout ads start only if consent from a previous session allows it; otherwise they start as soon as " +
                 "the consent flow finishes. 0 = wait forever.")]
        [Min(0f)] public float ConsentTimeoutSeconds = 20f;

        [Tooltip("Remote config key holding the JSON ads plan (providers, routes, policy overrides). Read when a remote " +
                 "config module is installed and the game registered no IAdsPlanSource of its own. Empty = no plan.")]
        public string PlanRemoteConfigKey = "ads_plan";

        [Tooltip("Seconds the mock adapter pretends to show an ad for. Editor / development builds only.")]
        [Min(0f)] public float NullAdapterFakeDelaySeconds = 0.5f;

        [Header("Consent Debug")]
        public ConsentDebugConfig ConsentDebug = new();

        [Header("Test Mode")]
        [Tooltip("Enable to show test ads. Add device IDs below (printed in logcat on first run).")]
        public bool TestMode;
        [Tooltip("Info logs for every ad load, show and retry also in release builds (always on in the Editor and development builds). Warnings and errors are always logged.")]
        public bool VerboseLogs;
        [Tooltip("AdMob test device IDs.")]
        public List<string> AdMobTestDeviceIds = new();
        [Tooltip("MAX test device advertising IDs. Only applied when TestMode is enabled.")]
        public List<string> MaxTestDeviceIds = new();

        [Header("Placements")]
        public List<AdPlacement> Placements = new();

        public override int Order => GameLibModuleOrder.Ads;

        /// <summary>The config row for <paramref name="providerId"/>, or null when the provider is not listed.</summary>
        public AdsProviderConfig FindProvider(string providerId)
        {
            if (Providers == null) return null;
            string id = AdProviderIds.Normalize(providerId);
            foreach (AdsProviderConfig c in Providers)
                if (c != null && AdProviderIds.Normalize(c.ProviderId) == id) return c;
            return null;
        }

        public bool TryGet(string id, out AdPlacement placement)
        {
            if (!string.IsNullOrEmpty(id) && Placements != null)
            {
                foreach (AdPlacement p in Placements)
                {
                    if (p != null && p.Id == id)
                    {
                        placement = p;
                        return true;
                    }
                }
            }
            placement = null;
            return false;
        }

        public override void Validate(ICollection<GameLibIssue> issues)
        {
            var ids = new HashSet<string>();
            foreach (AdPlacement p in Placements)
            {
                if (p == null) continue;
                if (string.IsNullOrWhiteSpace(p.Id))
                {
                    issues.Add(GameLibIssue.Error("A placement has no id."));
                    continue;
                }
                if (!ids.Add(p.Id))
                    issues.Add(GameLibIssue.Error($"Placement '{p.Id}' is listed twice."));
                if (p.Routes == null || p.Routes.Count == 0)
                    issues.Add(GameLibIssue.Warning($"Placement '{p.Id}' has no route, so only the mock adapter serves it."));
                else
                    foreach (AdRoute r in p.Routes)
                    {
                        if (r == null || r.EffectiveProviderId == AdProviderIds.None) continue;
                        if (string.IsNullOrEmpty(r.AndroidAdUnitId) && string.IsNullOrEmpty(r.AdUnitId))
                            issues.Add(GameLibIssue.Warning($"Placement '{p.Id}' route '{r.EffectiveProviderId}' has no Android ad unit id."));
                        if (string.IsNullOrEmpty(r.IosAdUnitId) && string.IsNullOrEmpty(r.AdUnitId))
                            issues.Add(GameLibIssue.Warning($"Placement '{p.Id}' route '{r.EffectiveProviderId}' has no iOS ad unit id."));
                        if (p.Format == AdFormat.Banner && p.Size is BannerSize.SmallFullWidth or BannerSize.Collapsible
                            && r.EffectiveProviderId == AdProviderIds.Max)
                            issues.Add(GameLibIssue.Warning($"Placement '{p.Id}': MAX has no {p.Size} banner - its route '{r.EffectiveProviderId}' shows a Standard 320x50 one."));
                        if (p.Format == AdFormat.RewardedInterstitial && r.EffectiveProviderId == AdProviderIds.Max)
                            issues.Add(GameLibIssue.Error($"Placement '{p.Id}': MAX has no rewarded interstitial - route it to AdMob, or use an Interstitial / Rewarded placement on MAX."));
                    }
            }

            if (!string.IsNullOrEmpty(AppOpenPlacementId))
            {
                if (!TryGet(AppOpenPlacementId, out AdPlacement appOpen))
                    issues.Add(GameLibIssue.Error($"App Open Placement Id '{AppOpenPlacementId}' is not in Placements."));
                else if (appOpen.Format != AdFormat.AppOpen)
                    issues.Add(GameLibIssue.Error($"App Open Placement '{AppOpenPlacementId}' has format {appOpen.Format}, expected AppOpen."));
            }

            if (TestMode)
                issues.Add(GameLibIssue.Warning("Ads Test Mode is on - turn it off for release builds."));
            if (ConsentDebug != null && ConsentDebug.Enabled)
                issues.Add(GameLibIssue.Warning("Consent Debug is on - turn it off for release builds."));
        }

        public override async ValueTask InstallAsync(IServiceRegistry services, CancellationToken cancellationToken = default)
        {
            AdsLog.Verbose = VerboseLogs || Debug.isDebugBuild;
            IAnalytics analytics = services.Resolve<IAnalytics>();
            IEntitlements entitlements = services.Resolve<IEntitlements>();
            IConsentState consentState = services.Resolve<IConsentState>();
            services.TryResolve(out IAppLifecycle lifecycle);

            // 1. Plan known without network: the game's own source, else the remote config value (cache / default).
            if (!services.TryResolve(out IAdsPlanSource planSource) && !string.IsNullOrEmpty(PlanRemoteConfigKey) &&
                services.TryResolve(out IRemoteConfig remoteConfig))
            {
                var remoteSource = new RemoteConfigAdsPlanSource(PlanRemoteConfigKey, remoteConfig);
                services.RegisterDisposable(remoteSource);
                planSource = remoteSource;
            }
            AdsPlan plan = null;
            try
            {
                if (planSource != null && !planSource.TryGetCachedPlan(out plan)) plan = null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Ads] Plan source threw: {e.Message} - no plan.");
                plan = null;
            }

            // 2. Providers.
            List<IAdsProviderFactory> selected = SelectProviders(AdsProviderCatalog.All, this, plan);
            var adapters = new List<IAdsProviderAdapter>();
            foreach (IAdsProviderFactory factory in selected)
            {
                try
                {
                    IAdsProviderAdapter adapter = factory.CreateAdapter();
                    if (adapter != null) adapters.Add(adapter);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Ads] Provider '{factory.ProviderId}' could not create its adapter: {e.Message}");
                }
            }
            // No SDK selected (Editor, or no provider package): mock every placement so gameplay flows still run - in the
            // Editor / development builds only. With an SDK present the mock only serves routes that say 'none'.
            bool anySdk = adapters.Count > 0;
            if (!anySdk || (RoutesName(this, AdProviderIds.None) && (plan?.Providers == null || plan.Providers.Count == 0 || PlanLists(plan, AdProviderIds.None))))
                adapters.Add(new NullAdsAdapter(this, NullAdapterFakeDelaySeconds, serveAllPlacements: !anySdk));
            Debug.Log($"[Ads] Providers: {DescribeAdapters(adapters)}{(plan != null ? $" (plan '{plan.Name}')" : string.Empty)}.");

            var service = new CompositeAdsService(adapters, this, plan, () => entitlements.Has(Entitlements.NoAds));
            service.ShowResult     += (id, provider, result) => TrackResult(analytics, id, provider, result);
            service.ImpressionPaid += impression => analytics.TrackAdRevenue(ToAdRevenue(impression));
            services.Register<IAdsService>(service);
            services.RegisterDisposable(service);
            if (plan != null) SetPlanProperty(analytics, plan);

            services.RegisterDisposable(new Wiring(service, entitlements, lifecycle, planSource, analytics));
            if (PauseGameDuringFullscreenAds || MuteAudioDuringFullscreenAds)
                services.RegisterDisposable(new FullscreenAdPause(service, PauseGameDuringFullscreenAds, MuteAudioDuringFullscreenAds));

            // 3. Consent platform of the highest-priority selected provider, wrapped so every change reaches the SDKs
            //    and the shared IConsentState (Firebase, AppsFlyer).
            IConsentService innerConsent = SelectConsent(selected, this);
            var forwarder = innerConsent != null ? new ConsentForwarder(innerConsent, service, consentState) : null;
            if (forwarder != null)
                services.Register<IConsentService>(forwarder);
            ConsentState initial = MapConsent(forwarder);
            initial.Att = consentState.Current.Att; // ATT belongs to whoever asks for it, not to the CMP
            service.ApplyConsent(initial);
            if (forwarder == null)
                consentState.Update(initial); // no CMP at all: publish "not required" so trackers do not wait
            else
                // A provider that timed out at init and joins later (MAX's CMP inside its init) answers only then.
                service.ProviderJoinedLate += _ => forwarder.Push();

            // 4. App open rule. Its cold-start deadline starts once the SDKs are up (AppOpenLauncher.AdsStarted).
            if (!string.IsNullOrEmpty(AppOpenPlacementId) && lifecycle != null)
            {
                var launcher = new AppOpenLauncher(service, lifecycle, AppOpenPlacementId)
                {
                    ColdStartWaitSeconds = AppOpenColdStartWaitSeconds,
                    ShowAtLaunchFrom     = Math.Max(1, AppOpenShowAtLaunchFrom),
                    AutoShowAtLaunch     = AppOpenAutoShowAtLaunch,
                    MinBackgroundSeconds = AppOpenMinBackgroundSeconds,
                    LaunchCountPrefsKey  = string.IsNullOrWhiteSpace(AppOpenLaunchCountKey) ? AppOpenLauncher.LaunchCountKey : AppOpenLaunchCountKey.Trim(),
                };
                services.Register(launcher);
                launcher.Start();
            }

            if (InitializeInBackground)
                _ = RunInBackgroundAsync(forwarder, service, lifecycle, cancellationToken);
            else
                await InitializeAdsAsync(forwarder, service, lifecycle, cancellationToken);
        }

        /// <summary>
        /// catalog ∩ enabled settings rows ∩ plan providers. A provider with no settings row is selected when a route
        /// names it (see <see cref="Providers"/>). The mock provider <c>none</c> is never returned here.
        /// </summary>
        public static List<IAdsProviderFactory> SelectProviders(IReadOnlyList<IAdsProviderFactory> catalog, AdsModule settings, AdsPlan plan)
        {
            var result = new List<IAdsProviderFactory>();
            if (catalog == null) return result;
            bool planFilters = plan?.Providers != null && plan.Providers.Count > 0;
            foreach (IAdsProviderFactory factory in catalog)
            {
                if (factory == null) continue;
                string id = AdProviderIds.Normalize(factory.ProviderId);
                if (id == AdProviderIds.None) continue;
                AdsProviderConfig row = settings != null ? settings.FindProvider(id) : null;
                bool enabled = row != null ? row.Enabled : RoutesName(settings, id);
                if (!enabled) continue;
                if (planFilters && !PlanLists(plan, id)) continue;
                result.Add(factory);
            }
            result.Sort((a, b) => string.CompareOrdinal(AdProviderIds.Normalize(a.ProviderId), AdProviderIds.Normalize(b.ProviderId)));
            return result;
        }

        /// <summary>The consent platform of the selected factory with the highest <see cref="IAdsProviderFactory.ConsentPriority"/> that has one.</summary>
        public static IConsentService SelectConsent(IReadOnlyList<IAdsProviderFactory> selected, AdsModule settings)
        {
            IConsentService best = null;
            int bestPriority = int.MinValue;
            if (selected == null) return null;
            foreach (IAdsProviderFactory factory in selected)
            {
                if (factory == null || factory.ConsentPriority <= bestPriority) continue;
                IConsentService candidate;
                try { candidate = factory.CreateConsentService(settings); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Consent] {factory.ProviderId} could not create its consent service: {e.Message}");
                    continue;
                }
                if (candidate == null) continue;
                best = candidate;
                bestPriority = factory.ConsentPriority;
            }
            return best;
        }

        /// <summary>
        /// Maps a consent platform's answer onto the state handed to every adapter: consent comes from the IAB TCF
        /// purposes the user accepted, never from the CMP status alone. Null platform = not required.
        /// </summary>
        /// <param name="store">Where the IAB strings are read. Null = the platform store. Tests inject a fake.</param>
        public static ConsentState MapConsent(IConsentService consent, IIabPrivacyStore store = null)
        {
            if (consent == null) return IabPrivacy.Map(ConsentStatus.NotRequired, true, store);
            return IabPrivacy.Map(consent.Status, consent.CanRequestAds, store);
        }

        /// <summary>The <see cref="AdRevenue"/> of one paid impression.</summary>
        public static AdRevenue ToAdRevenue(AdImpression impression) => new()
        {
            Platform  = PlatformName(impression.ProviderId),
            Network   = impression.AdSource,
            Format    = impression.Format switch
            {
                AdFormat.Interstitial => AdFormatKind.Interstitial,
                AdFormat.Rewarded     => AdFormatKind.Rewarded,
                AdFormat.Banner       => AdFormatKind.Banner,
                AdFormat.Mrec         => AdFormatKind.Mrec,
                AdFormat.AppOpen      => AdFormatKind.AppOpen,
                AdFormat.RewardedInterstitial => AdFormatKind.RewardedInterstitial,
                _                     => AdFormatKind.Unknown,
            },
            AdUnitId  = impression.AdUnitId,
            Placement = impression.PlacementId,
            Value     = impression.Value,
            Currency  = impression.Currency,
            Precision = impression.Precision,
            CountryCode = impression.CountryCode,
        };

        // Firebase's documented ad_platform values.
        private static string PlatformName(string providerId) => AdProviderIds.Normalize(providerId) switch
        {
            AdProviderIds.Max       => "AppLovin",
            AdProviderIds.AdMob     => "AdMob",
            AdProviderIds.LevelPlay => "ironSource",
            var other               => other,
        };

        private static bool RoutesName(AdsModule settings, string providerId)
        {
            if (settings == null || settings.Placements == null) return false;
            foreach (AdPlacement p in settings.Placements)
            {
                if (p?.Routes == null) continue;
                foreach (AdRoute r in p.Routes)
                    if (r != null && r.EffectiveProviderId == providerId) return true;
            }
            return false;
        }

        private static bool PlanLists(AdsPlan plan, string providerId)
        {
            foreach (string p in plan.Providers)
                if (!string.IsNullOrWhiteSpace(p) && AdProviderIds.Normalize(p) == providerId) return true;
            return false;
        }

        private static string DescribeAdapters(List<IAdsProviderAdapter> adapters)
        {
            var ids = new List<string>(adapters.Count);
            foreach (IAdsProviderAdapter a in adapters) ids.Add(a.ProviderId);
            return string.Join(", ", ids);
        }

        private static void TrackResult(IAnalytics analytics, string placementId, string provider, AdShowResult result)
        {
            analytics.Track(new AnalyticsEvent("ads_show_result", new Dictionary<string, object>
            {
                { "placement_id", placementId },
                { "provider",     provider },
                { "result",       result.ToString() },
            }));
        }

        private static void SetPlanProperty(IAnalytics analytics, AdsPlan plan)
        {
            if (plan == null || string.IsNullOrEmpty(plan.Name)) return;
            try { analytics.SetUserProperty("ads_plan", plan.Name); }
            catch (Exception e) { Debug.LogWarning($"[Ads] ads_plan user property failed: {e.Message}"); }
        }

        private async Task RunInBackgroundAsync(IConsentService consentService, CompositeAdsService service, IAppLifecycle lifecycle,
                                                CancellationToken cancellationToken)
        {
            try
            {
                await InitializeAdsAsync(consentService, service, lifecycle, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Bootstrap torn down (play mode stopped) - nothing to report.
            }
            catch (Exception e)
            {
                Debug.LogError($"[Ads] Background initialization failed: {e.Message}");
            }
        }

        // The consent platform runs BEFORE any SDK initializes or loads (required by Google UMP), and nothing starts
        // until it says CanRequestAds.
        private async Task InitializeAdsAsync(IConsentService consentService, CompositeAdsService service, IAppLifecycle lifecycle,
                                              CancellationToken cancellationToken)
        {
            if (consentService != null)
            {
                Task request = RequestConsentSafeAsync(consentService, cancellationToken);
                bool answered = await WaitForConsentAsync(request, consentService, cancellationToken);
                if (!consentService.CanRequestAds)
                {
                    if (answered)
                    {
                        NoConsentYet(consentService, service, lifecycle, cancellationToken);
                        return;
                    }
                    // No answer yet and nothing stored from a previous session: start ads the moment the flow finishes.
                    Debug.LogWarning($"[Consent] No consent answer after {ConsentTimeoutSeconds:0}s - ads start once the consent flow completes.");
                    _ = InitializeAfterConsentAsync(request, consentService, service, lifecycle, cancellationToken);
                    return;
                }
                if (!answered)
                    Debug.LogWarning($"[Consent] No consent answer after {ConsentTimeoutSeconds:0}s - continuing with the consent stored from a previous session.");
            }
            await service.InitializeAsync(cancellationToken);
            PushConsentAfterInit(consentService);
        }

        // A CMP that runs inside an SDK's init (MAX's consent flow) only answers after InitializeAsync: re-read it.
        private static void PushConsentAfterInit(IConsentService consentService)
        {
            if (consentService is ConsentForwarder forwarder)
                forwarder.Push();
        }

        private static async Task RequestConsentSafeAsync(IConsentService consentService, CancellationToken cancellationToken)
        {
            try
            {
                await consentService.RequestAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                Debug.LogWarning($"[Consent] Request failed: {e.Message}");
            }
        }

        // True when the request finished. Times out only while no consent form is on screen: a user reading the
        // form is never cut off.
        private async Task<bool> WaitForConsentAsync(Task request, IConsentService consentService, CancellationToken cancellationToken)
        {
            if (ConsentTimeoutSeconds <= 0f)
            {
                await request;
                return true;
            }
            float deadline = Time.realtimeSinceStartup + ConsentTimeoutSeconds;
            while (!request.IsCompleted)
            {
                if (!IsFormShowing(consentService) && Time.realtimeSinceStartup >= deadline) return false;
                await Task.WhenAny(request, Task.Delay(250, cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
            }
            await request; // surface cancellation
            return true;
        }

        // Consent services are provider plugins this package cannot reference: an optional public 'bool IsFormShowing'
        // property (the UMP consent service has one) is read by name.
        private static bool IsFormShowing(IConsentService consentService)
        {
            object target = consentService is ConsentForwarder f ? f.Inner : consentService;
            if (target == null) return false;
            try
            {
                PropertyInfo prop = target.GetType().GetProperty("IsFormShowing", BindingFlags.Public | BindingFlags.Instance);
                return prop != null && prop.PropertyType == typeof(bool) && (bool)prop.GetValue(target);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task InitializeAfterConsentAsync(Task request, IConsentService consentService, CompositeAdsService service,
                                                       IAppLifecycle lifecycle, CancellationToken cancellationToken)
        {
            try
            {
                await request;
                if (consentService.CanRequestAds)
                {
                    await service.InitializeAsync(cancellationToken);
                    PushConsentAfterInit(consentService);
                }
                else NoConsentYet(consentService, service, lifecycle, cancellationToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.LogError($"[Ads] Initialization after consent failed: {e.Message}");
            }
        }

        /// <summary>How many returns to the app re-run a consent flow that ended without permission to request ads.</summary>
        public const int ConsentRetriesOnResume = 3;

        // The consent flow ended without permission to request ads - typically UMP's update failing offline on the first
        // launch. Retried on the next returns to the app (bounded); ads start as soon as a retry allows them.
        private void NoConsentYet(IConsentService consentService, CompositeAdsService service, IAppLifecycle lifecycle,
                                         CancellationToken cancellationToken)
        {
            if (lifecycle == null)
            {
                Debug.LogWarning("[Consent] CanRequestAds is false after the consent flow - ad SDKs stay uninitialized. " +
                                 "Call IConsentService.RequestAsync then IAdsService.InitializeAsync to retry.");
                return;
            }
            Debug.LogWarning($"[Consent] CanRequestAds is false after the consent flow - retried on the next {ConsentRetriesOnResume} returns to the app.");

            int attempts = 0;
            bool running = false;
            void OnResumed(AppResumeInfo info)
            {
                if (running) return;
                if (cancellationToken.IsCancellationRequested)
                {
                    lifecycle.Resumed -= OnResumed;
                    return;
                }
                running = true;
                attempts++;
                _ = RetryAsync();
            }
            async Task RetryAsync()
            {
                try
                {
                    // Bounded like the first request: a retry that never answers must not block the next resumes.
                    await WaitForConsentAsync(RequestConsentSafeAsync(consentService, cancellationToken), consentService, cancellationToken);
                    if (consentService.CanRequestAds)
                    {
                        lifecycle.Resumed -= OnResumed;
                        Debug.Log("[Consent] Consent retry allows ads - starting the ad SDKs.");
                        await service.InitializeAsync(cancellationToken);
                        PushConsentAfterInit(consentService);
                    }
                    else if (attempts >= ConsentRetriesOnResume)
                    {
                        lifecycle.Resumed -= OnResumed;
                        Debug.LogWarning("[Consent] CanRequestAds still false - ad SDKs stay uninitialized for this session.");
                    }
                }
                catch (OperationCanceledException)
                {
                    lifecycle.Resumed -= OnResumed;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Ads] Initialization after a consent retry failed: {e.Message}");
                }
                finally
                {
                    running = false;
                }
            }
            lifecycle.Resumed += OnResumed;
        }

        /// <summary>
        /// The registered <see cref="IConsentService"/>: the selected consent platform, plus the resulting
        /// <see cref="ConsentState"/> pushed to every adapter and to <see cref="IConsentState"/> after the request and
        /// after the privacy options form.
        /// </summary>
        public sealed class ConsentForwarder : IConsentService
        {
            private readonly CompositeAdsService ads;
            private readonly IConsentState state;

            public ConsentForwarder(IConsentService inner, CompositeAdsService ads, IConsentState state)
            {
                Inner      = inner ?? throw new ArgumentNullException(nameof(inner));
                this.ads   = ads;
                this.state = state;
            }

            /// <summary>The provider's consent service.</summary>
            public IConsentService Inner { get; }

            public ConsentStatus Status => Inner.Status;

            public bool CanRequestAds => Inner.CanRequestAds;

            public bool IsPrivacyOptionsRequired => Inner.IsPrivacyOptionsRequired;

            public async ValueTask RequestAsync(CancellationToken cancellationToken = default)
            {
                try { await Inner.RequestAsync(cancellationToken); }
                finally { Push(); }
            }

            public async ValueTask ShowPrivacyOptionsAsync(CancellationToken cancellationToken = default)
            {
                try { await Inner.ShowPrivacyOptionsAsync(cancellationToken); }
                finally { Push(); }
            }

            internal void Push()
            {
                try
                {
                    ConsentState mapped = MapConsent(Inner);
                    ads?.ApplyConsent(mapped);
                    if (state != null)
                    {
                        mapped.Att = state.Current.Att; // ATT belongs to whoever asks for it, not to the CMP
                        state.Update(mapped);
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[Consent] Forwarding consent failed: {e.Message}"); }
            }
        }

        /// <summary>Subscriptions to other services, released with the registry.</summary>
        private sealed class Wiring : IDisposable
        {
            private readonly CompositeAdsService ads;
            private readonly IEntitlements entitlements;
            private readonly IAppLifecycle lifecycle;
            private readonly IAdsPlanSource planSource;
            private readonly IAnalytics analytics;
            private IDisposable fullscreenScope;

            public Wiring(CompositeAdsService ads, IEntitlements entitlements, IAppLifecycle lifecycle, IAdsPlanSource planSource, IAnalytics analytics)
            {
                this.ads          = ads;
                this.entitlements = entitlements;
                this.lifecycle    = lifecycle;
                this.planSource   = planSource;
                this.analytics    = analytics;

                entitlements.Changed += OnEntitlementChanged;
                if (planSource != null) planSource.PlanChanged += OnPlanChanged;
                if (lifecycle != null)
                {
                    ads.FullscreenShowingChanged += OnFullscreenShowingChanged;
                    ads.AdClicked += OnAdClicked;
                    ads.DebuggerOpening += OnDebuggerOpening;
                }
            }

            private void OnEntitlementChanged(string key, bool owned)
            {
                if (key == Entitlements.NoAds) ads.RefreshNoAds();
            }

            private void OnPlanChanged(AdsPlan plan)
            {
                ads.ApplyPlan(plan);
                SetPlanProperty(analytics, plan);
            }

            // Leaving the app while a fullscreen ad is up, or for an ad click, is not the player returning.
            private void OnFullscreenShowingChanged(bool showing)
            {
                if (showing)
                {
                    fullscreenScope ??= lifecycle.BeginExternalActivity("ad");
                }
                else
                {
                    fullscreenScope?.Dispose();
                    fullscreenScope = null;
                }
            }

            private void OnAdClicked(string placementId) => lifecycle.MarkExternalActivity("ad_click");

            private void OnDebuggerOpening(string providerId) => lifecycle.MarkExternalActivity("ad_debugger");

            public void Dispose()
            {
                entitlements.Changed -= OnEntitlementChanged;
                if (planSource != null) planSource.PlanChanged -= OnPlanChanged;
                ads.FullscreenShowingChanged -= OnFullscreenShowingChanged;
                ads.AdClicked -= OnAdClicked;
                ads.DebuggerOpening -= OnDebuggerOpening;
                fullscreenScope?.Dispose();
                fullscreenScope = null;
            }
        }
    }
}
