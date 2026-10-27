using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// AppLovin MAX provider (<see cref="AdProviderIds.Max"/>). Created by <see cref="MaxAdsProviderFactory"/>; the
    /// composite ads service owns every rule (routes, cooldowns, caps, no-ads) and only asks this adapter to load,
    /// report readiness and show.
    /// <para>
    /// MAX loads and reports per ad unit, so several placements may share one unit: they share its loaded ad (showing
    /// one consumes it for all), the unit keeps loading while at least one of its placements is active, and the show
    /// passes the placement id to MAX as its "placement" reporting field. A unit has exactly one format; a placement
    /// whose format differs from the unit's first placement is rejected with an error log.
    /// </para>
    /// <para>
    /// Fullscreen ads load only after <see cref="SetPlacementActive"/>(id, true) and are reloaded after every show and
    /// every failure (exponential backoff 2, 4 ... capped at 60 s). MAX cannot release a fullscreen ad it already
    /// loaded: deactivating a placement stops reloading and reports it not ready; the cached ad is reused on
    /// reactivation. Banners/MRECs are created on their first show; MAX refreshes and retries them internally.
    /// </para>
    /// <para>
    /// SDK events are delivered on the Unity main thread (<c>MaxSdkBase.InvokeEventsOnUnityMainThread</c> is forced
    /// to true unless the game already chose a value). Once a show started, the caller's token is ignored and the
    /// real outcome is awaited.
    /// </para>
    /// </summary>
    [Preserve]
    public sealed class MaxAdsAdapter : IAdsProviderAdapter, IBannerMetrics
    {
        private const string LogPrefix = "[Ads/MAX]";
        private const double MaxRetryDelaySeconds = 60d;
        // MAX documents impression-level revenue in USD; MaxSdkBase.AdInfo carries no currency field.
        private const string RevenueCurrency = "USD";

        // Placement id -> assignment (placement + MAX unit id for this platform).
        private readonly Dictionary<string, AdUnitAssignment> assigned = new();
        // MAX ad unit id -> placement ids sharing it (SDK callbacks only carry the unit id).
        private readonly Dictionary<string, List<string>> unitPlacements = new();
        private readonly Dictionary<string, AdFormat> unitFormats = new();

        private readonly HashSet<string> activePlacements   = new();
        private readonly HashSet<string> loadingUnits       = new(); // fullscreen load in flight
        private readonly HashSet<string> loadedUnits        = new(); // fullscreen ad ready in MAX
        private readonly HashSet<string> retryingUnits      = new(); // backoff timer running
        private readonly HashSet<string> createdBannerUnits = new();
        private readonly HashSet<string> loadedBannerUnits  = new();
        private readonly HashSet<string> visibleBannerUnits = new(); // shown and not hidden since
        private readonly HashSet<string> refreshPaused      = new(); // auto-refresh stopped while hidden
        private readonly Dictionary<string, float> bannerHeightPixels = new(); // per unit, computed once
        private float pixelsPerDp;

        private readonly Dictionary<string, TaskCompletionSource<AdShowResult>> pendingShows     = new(); // per unit
        private readonly Dictionary<string, TaskCompletionSource<bool>>         rewardSignals    = new(); // per rewarded unit
        private readonly Dictionary<string, string>                             showingPlacement = new(); // unit -> placement on screen
        private readonly Dictionary<string, int>                                retryAttempts    = new(); // per unit

        private readonly CancellationTokenSource lifetime = new();
        private ConsentState consent = new();
        private bool initialized;
        private bool disposed;

        /// <summary>Creates the adapter and subscribes to the static MAX callbacks (released by <see cref="Dispose"/>).</summary>
        [Preserve]
        public MaxAdsAdapter()
        {
            // Our handlers touch plain dictionaries: never let MAX raise them on a background thread.
            if (MaxSdkBase.InvokeEventsOnUnityMainThread == null)
                MaxSdkBase.InvokeEventsOnUnityMainThread = true;

            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent        += OnInterstitialLoaded;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent    += OnInterstitialLoadFailed;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent        += OnInterstitialHidden;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += OnInterstitialDisplayFailed;
            MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent   += OnInterstitialRevenue;

            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent            += OnRewardedLoaded;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent        += OnRewardedLoadFailed;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent    += OnRewardedReward;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent            += OnRewardedHidden;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent     += OnRewardedDisplayFailed;
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent       += OnRewardedRevenue;

            MaxSdkCallbacks.AppOpen.OnAdLoadedEvent             += OnAppOpenLoaded;
            MaxSdkCallbacks.AppOpen.OnAdLoadFailedEvent         += OnAppOpenLoadFailed;
            MaxSdkCallbacks.AppOpen.OnAdHiddenEvent             += OnAppOpenHidden;
            MaxSdkCallbacks.AppOpen.OnAdDisplayFailedEvent      += OnAppOpenDisplayFailed;
            MaxSdkCallbacks.AppOpen.OnAdRevenuePaidEvent        += OnAppOpenRevenue;

            MaxSdkCallbacks.Banner.OnAdLoadedEvent              += OnBannerLoaded;
            MaxSdkCallbacks.Banner.OnAdLoadFailedEvent          += OnBannerLoadFailed;
            MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent         += OnBannerRevenue;

            MaxSdkCallbacks.MRec.OnAdLoadedEvent                += OnMRecLoaded;
            MaxSdkCallbacks.MRec.OnAdLoadFailedEvent            += OnMRecLoadFailed;
            MaxSdkCallbacks.MRec.OnAdRevenuePaidEvent           += OnMRecRevenue;
        }

        /// <inheritdoc />
        public string ProviderId => AdProviderIds.Max;

        /// <inheritdoc />
        public bool IsInitialized => initialized;

        /// <inheritdoc />
        public event Action<string> PlacementReadyChanged;

        /// <inheritdoc />
        public event Action<string, string> PlacementLoadFailed;

        /// <inheritdoc />
        public event Action<AdImpression> ImpressionPaid;

        /// <summary>Grace to wait for a reward callback that arrives after the hidden callback.</summary>
        public TimeSpan LateRewardGrace { get; set; } = TimeSpan.FromSeconds(0.5);

        /// <inheritdoc />
        public async ValueTask InitializeAsync(AdsProviderContext context, CancellationToken cancellationToken = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (disposed) throw new ObjectDisposedException(nameof(MaxAdsAdapter));
            if (initialized) return;
            cancellationToken.ThrowIfCancellationRequested();

            MapUnits(context.Units);
            ApplyConsent(context.Consent);

            // MAX 8.6.6 still exposes SetSdkKey, marked [Obsolete] (MaxSdkAndroid.cs:984, MaxSdkiOS.cs:1236,
            // MaxSdkUnityEditor.cs:1238). Without an app key the SDK key comes from the AppLovin Integration Manager.
            string appKey = context.AppKey;
            if (!string.IsNullOrWhiteSpace(appKey) && !MaxSdk.IsInitialized())
            {
#pragma warning disable CS0618
                MaxSdk.SetSdkKey(appKey.Trim());
#pragma warning restore CS0618
            }

            if (context.TestMode && context.Settings?.MaxTestDeviceIds?.Count > 0)
                MaxSdk.SetTestDeviceAdvertisingIdentifiers(context.Settings.MaxTestDeviceIds.ToArray());

            if (!MaxSdk.IsInitialized())
            {
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                // A failed init still raises this event: report it, so the composite drops MAX instead of routing to
                // an SDK whose every load fails.
                void OnSdkInit(MaxSdkBase.SdkConfiguration config)
                {
                    if (config != null && config.IsSuccessfullyInitialized) tcs.TrySetResult(true);
                    else tcs.TrySetException(new InvalidOperationException("MaxSdk.InitializeSdk reported IsSuccessfullyInitialized = false"));
                }
                MaxSdkCallbacks.OnSdkInitializedEvent += OnSdkInit;
                try
                {
                    MaxSdk.InitializeSdk();
                    using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
                        await tcs.Task;
                }
                finally
                {
                    MaxSdkCallbacks.OnSdkInitializedEvent -= OnSdkInit;
                }
            }

            if (disposed) throw new ObjectDisposedException(nameof(MaxAdsAdapter));
            initialized = true;
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Initialized with {assigned.Count} placement(s) on {unitFormats.Count} unit(s).");

            // Placements activated before init (the composite should not, but be tolerant) start loading now.
            foreach (string unit in new List<string>(unitFormats.Keys))
                if (IsUnitActive(unit)) LoadUnit(unit);
            foreach (string id in new List<string>(activePlacements))
                if (assigned.TryGetValue(id, out AdUnitAssignment a) && a.Placement.IsBannerLike) PreloadBanner(a);
        }

        /// <inheritdoc />
        public void SetPlacementActive(string placementId, bool active)
        {
            if (disposed || placementId == null || !assigned.TryGetValue(placementId, out AdUnitAssignment a)) return;
            if (active ? !activePlacements.Add(placementId) : !activePlacements.Remove(placementId)) return;

            string unit = a.AdUnitId;
            bool banner = a.Placement.IsBannerLike;
            if (active)
            {
                if (banner && initialized) PreloadBanner(a);
                else if (!banner && initialized)
                {
                    if (loadedUnits.Contains(unit)) PlacementReadyChanged?.Invoke(placementId); // cached ad reused
                    else LoadUnit(unit);
                }
                return;
            }

            if (banner)
            {
                if (!IsUnitActive(unit)) DestroyBannerUnit(unit);
            }
            else if (!IsUnitActive(unit))
            {
                retryAttempts.Remove(unit);
            }
            PlacementReadyChanged?.Invoke(placementId);
        }

        /// <inheritdoc />
        public bool IsPlacementReady(string placementId)
        {
            if (!initialized || placementId == null || !assigned.TryGetValue(placementId, out AdUnitAssignment a)) return false;
            if (!activePlacements.Contains(placementId)) return false;
            return a.Placement.IsBannerLike
                ? createdBannerUnits.Contains(a.AdUnitId) && loadedBannerUnits.Contains(a.AdUnitId)
                : loadedUnits.Contains(a.AdUnitId) && !pendingShows.ContainsKey(a.AdUnitId);
        }

        /// <inheritdoc />
        public async ValueTask<AdShowResult> ShowAsync(string placementId, CancellationToken cancellationToken = default)
        {
            if (disposed || placementId == null || !assigned.TryGetValue(placementId, out AdUnitAssignment a))
                return AdShowResult.Failed;
            switch (a.Placement.Format)
            {
                case AdFormat.Interstitial:
                case AdFormat.Rewarded:
                case AdFormat.AppOpen:
                    return await ShowFullscreenAsync(a, cancellationToken);
                default:
                    return ShowBanner(a);
            }
        }

        /// <inheritdoc />
        public void HideBanner(string placementId)
        {
            if (!TryGetBanner(placementId, out AdUnitAssignment a) || !createdBannerUnits.Contains(a.AdUnitId)) return;
            string unit = a.AdUnitId;
            if (IsMrecUnit(unit)) MaxSdk.HideMRec(unit);
            else MaxSdk.HideBanner(unit);
            visibleBannerUnits.Remove(unit);
            PauseRefresh(unit);
        }

        /// <inheritdoc />
        public void DestroyBanner(string placementId)
        {
            if (!TryGetBanner(placementId, out AdUnitAssignment a)) return;
            DestroyBannerUnit(a.AdUnitId);
        }

        /// <summary>
        /// Maps the consent state onto MAX's privacy API: <c>SetHasUserConsent</c> where GDPR applies, <c>SetDoNotSell</c> when
        /// <see cref="ConsentState.DoNotSell"/> has a value. MAX 8.6.6 has no age-restricted API (removed in MAX 13 /
        /// plugin 7.x); an age-restricted user is only logged - mark the app as child-directed in the AppLovin dashboard.
        /// MAX also reads the IAB TCF string a CMP (UMP) writes. Valid before and after <c>InitializeSdk</c>.
        /// </summary>
        public void ApplyConsent(ConsentState state)
        {
            if (disposed || state == null) return;
            consent = state.Clone();
            // Only meaningful where GDPR applies; elsewhere MAX's own default stands.
            if (consent.GdprApplies) MaxSdk.SetHasUserConsent(consent.HasUserConsent);
            if (consent.DoNotSell.HasValue) MaxSdk.SetDoNotSell(consent.DoNotSell.Value);
            if (consent.IsAgeRestrictedUser)
                Debug.LogWarning($"{LogPrefix} Age-restricted user: MAX 8.6.6 has no runtime age-restricted API; configure child-directed treatment in the AppLovin dashboard.");
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Consent applied: {consent}.");
        }

        /// <summary>Stops every reload, resolves pending shows as Failed, destroys banners and unsubscribes from MAX.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();

            // Resolve pending shows so callers don't hang after teardown.
            foreach (var tcs in new List<TaskCompletionSource<AdShowResult>>(pendingShows.Values)) tcs.TrySetResult(AdShowResult.Failed);
            pendingShows.Clear();
            rewardSignals.Clear();

            // Release native banner views - they would otherwise outlive the service (bootstrap reset / domain reload).
            foreach (string unit in new List<string>(createdBannerUnits)) DestroyBannerUnit(unit);

            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent        -= OnInterstitialLoaded;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent    -= OnInterstitialLoadFailed;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent        -= OnInterstitialHidden;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent -= OnInterstitialDisplayFailed;
            MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent   -= OnInterstitialRevenue;

            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent            -= OnRewardedLoaded;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent        -= OnRewardedLoadFailed;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent    -= OnRewardedReward;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent            -= OnRewardedHidden;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent     -= OnRewardedDisplayFailed;
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent       -= OnRewardedRevenue;

            MaxSdkCallbacks.AppOpen.OnAdLoadedEvent             -= OnAppOpenLoaded;
            MaxSdkCallbacks.AppOpen.OnAdLoadFailedEvent         -= OnAppOpenLoadFailed;
            MaxSdkCallbacks.AppOpen.OnAdHiddenEvent             -= OnAppOpenHidden;
            MaxSdkCallbacks.AppOpen.OnAdDisplayFailedEvent      -= OnAppOpenDisplayFailed;
            MaxSdkCallbacks.AppOpen.OnAdRevenuePaidEvent        -= OnAppOpenRevenue;

            MaxSdkCallbacks.Banner.OnAdLoadedEvent              -= OnBannerLoaded;
            MaxSdkCallbacks.Banner.OnAdLoadFailedEvent          -= OnBannerLoadFailed;
            MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent         -= OnBannerRevenue;

            MaxSdkCallbacks.MRec.OnAdLoadedEvent                -= OnMRecLoaded;
            MaxSdkCallbacks.MRec.OnAdLoadFailedEvent            -= OnMRecLoadFailed;
            MaxSdkCallbacks.MRec.OnAdRevenuePaidEvent           -= OnMRecRevenue;

            lifetime.Dispose();
        }

        // --- Mapping ---

        private void MapUnits(IReadOnlyList<AdUnitAssignment> units)
        {
            // Idempotent: a faulted/cancelled InitializeAsync may be retried with the same context.
            assigned.Clear();
            unitPlacements.Clear();
            unitFormats.Clear();
            if (units == null) return;

            foreach (AdUnitAssignment u in units)
            {
                string id = u.PlacementId;
                string unit = u.AdUnitId?.Trim();
                if (string.IsNullOrEmpty(id)) continue;
                if (string.IsNullOrEmpty(unit))
                {
                    Debug.LogError($"{LogPrefix} Placement '{id}' has no MAX ad unit id for this platform - ignored. Check AdsModule.");
                    continue;
                }

                if (unitFormats.TryGetValue(unit, out AdFormat existing))
                {
                    // Sharing a unit is fine, but one MAX unit has exactly one format.
                    if (existing != u.Format)
                    {
                        Debug.LogError($"{LogPrefix} Ad unit {unit} is used by placements of different formats ({existing} and {u.Format} for '{id}') - '{id}' ignored. Check AdsModule.");
                        continue;
                    }
                    if (u.Placement.IsBannerLike)
                        Debug.LogWarning($"{LogPrefix} Banner unit {unit} is shared by '{unitPlacements[unit][0]}' and '{id}': one native view, created with the first placement's size and position.");
                    unitPlacements[unit].Add(id);
                }
                else
                {
                    unitFormats[unit]    = u.Format;
                    unitPlacements[unit] = new List<string> { id };
                }
                assigned[id] = new AdUnitAssignment(u.Placement, unit);
            }
        }

        private bool IsUnitActive(string unit)
        {
            if (!unitPlacements.TryGetValue(unit, out List<string> ids)) return false;
            foreach (string id in ids)
                if (activePlacements.Contains(id)) return true;
            return false;
        }

        // --- Loading ---

        private void LoadUnit(string unit)
        {
            if (disposed || !initialized || !IsUnitActive(unit)) return;
            if (loadingUnits.Contains(unit) || loadedUnits.Contains(unit) || pendingShows.ContainsKey(unit)) return;
            if (!unitFormats.TryGetValue(unit, out AdFormat format)) return;

            switch (format)
            {
                case AdFormat.Interstitial: MaxSdk.LoadInterstitial(unit); break;
                case AdFormat.Rewarded:     MaxSdk.LoadRewardedAd(unit);   break;
                case AdFormat.AppOpen:      MaxSdk.LoadAppOpenAd(unit);    break;
                default: return; // banners load through their view
            }
            loadingUnits.Add(unit);
        }

        // Exponential backoff (2, 4, 8 ... capped at 60 s) with unlimited attempts while the unit is active.
        private async Task RetryAfterBackoffAsync(string unit)
        {
            if (!retryingUnits.Add(unit)) return;
            try
            {
                int attempt = retryAttempts.TryGetValue(unit, out int previous) ? previous + 1 : 1;
                retryAttempts[unit] = attempt;
                double seconds = Math.Min(MaxRetryDelaySeconds, Math.Pow(2, Math.Min(attempt, 10)));
                if (AdsLog.Verbose) Debug.Log($"{LogPrefix} {Describe(unit)} retry #{attempt} in {seconds:0}s.");
                // Awaited on the Unity main thread: the continuation resumes on UnitySynchronizationContext.
                await Task.Delay(TimeSpan.FromSeconds(seconds), lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                return; // adapter disposed
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            finally
            {
                retryingUnits.Remove(unit);
            }
            LoadUnit(unit);
        }

        // --- Showing ---

        private async ValueTask<AdShowResult> ShowFullscreenAsync(AdUnitAssignment a, CancellationToken cancellationToken)
        {
            AdPlacement p = a.Placement;
            string unit = a.AdUnitId;
            string adType = AdTypeOf(p.Format);
            if (!initialized || !activePlacements.Contains(p.Id) || !loadedUnits.Contains(unit))
            {
                Debug.LogWarning($"{LogPrefix} {adType} {p.Id} not shown: not ready.");
                return AdShowResult.Failed;
            }
            if (pendingShows.ContainsKey(unit))
            {
                Debug.LogWarning($"{LogPrefix} {adType} {p.Id} not shown: unit {unit} is already on screen.");
                return AdShowResult.Failed;
            }
            if (cancellationToken.IsCancellationRequested) return AdShowResult.Failed;
            if (!SdkHasAd(unit, p.Format))
            {
                // Expired (or dropped by MAX) since it loaded: showing it might never call back and hold the show open.
                Debug.LogWarning($"{LogPrefix} {adType} {p.Id} not shown: MAX no longer has the ad (expired) - reloading.");
                loadedUnits.Remove(unit);
                NotifyUnit(unit);
                LoadUnit(unit);
                return AdShowResult.Failed;
            }

            var tcs = new TaskCompletionSource<AdShowResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> reward = null;
            pendingShows[unit] = tcs;
            if (p.Format == AdFormat.Rewarded)
            {
                reward = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                rewardSignals[unit] = reward;
            }
            loadedUnits.Remove(unit);
            showingPlacement[unit] = p.Id;
            NotifyUnit(unit); // the shared ad is consumed for every placement of the unit
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Showing {adType} {p.Id}.");

            AdShowResult result;
            try
            {
                // The placement id is MAX's own "placement" reporting field - distinguishes placements sharing a unit.
                switch (p.Format)
                {
                    case AdFormat.Interstitial: MaxSdk.ShowInterstitial(unit, p.Id); break;
                    case AdFormat.Rewarded:     MaxSdk.ShowRewardedAd(unit, p.Id);   break;
                    default:                    MaxSdk.ShowAppOpenAd(unit, p.Id);    break;
                }

                // The ad is on screen: the caller's token no longer applies (see class remarks).
                AdShowResult closed = await tcs.Task;
                result = reward != null ? await ResolveRewardAsync(closed, reward.Task) : closed;
            }
            finally
            {
                if (pendingShows.TryGetValue(unit, out var current) && ReferenceEquals(current, tcs)) pendingShows.Remove(unit);
                rewardSignals.Remove(unit);
                showingPlacement.Remove(unit);
            }

            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} {adType} {p.Id} result: {result}.");
            LoadUnit(unit);
            return result;
        }

        // Asked only at show time (one native call): readiness checks stay on the adapter's own state.
        private static bool SdkHasAd(string unit, AdFormat format)
        {
            try
            {
                return format switch
                {
                    AdFormat.Interstitial => MaxSdk.IsInterstitialReady(unit),
                    AdFormat.Rewarded     => MaxSdk.IsRewardedAdReady(unit),
                    _                     => MaxSdk.IsAppOpenAdReady(unit),
                };
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Ready check for {unit} threw: {e.Message} - showing anyway.");
                return true;
            }
        }

        // Hidden arrives as Skipped; upgrade to Completed if the reward callback landed or lands within the grace.
        private async Task<AdShowResult> ResolveRewardAsync(AdShowResult closed, Task<bool> rewardEarned)
        {
            if (closed != AdShowResult.Skipped) return closed;
            if (rewardEarned.IsCompleted) return AdShowResult.Completed;
            try
            {
                Task winner = await Task.WhenAny(rewardEarned, Task.Delay(LateRewardGrace, lifetime.Token));
                return winner == rewardEarned && rewardEarned.IsCompleted ? AdShowResult.Completed : AdShowResult.Skipped;
            }
            catch (OperationCanceledException)
            {
                return rewardEarned.IsCompleted ? AdShowResult.Completed : AdShowResult.Skipped;
            }
            catch (ObjectDisposedException)
            {
                return rewardEarned.IsCompleted ? AdShowResult.Completed : AdShowResult.Skipped;
            }
        }

        // Creates the view on first show; MAX keeps the show request and displays the banner once it loads.
        private AdShowResult ShowBanner(AdUnitAssignment a)
        {
            AdPlacement p = a.Placement;
            string unit = a.AdUnitId;
            if (!initialized)
            {
                Debug.LogWarning($"{LogPrefix} Banner {p.Id} not shown: SDK not initialized.");
                return AdShowResult.Failed;
            }
            if (!activePlacements.Contains(p.Id))
            {
                if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Banner {p.Id} not shown: placement not active.");
                return AdShowResult.Failed;
            }

            try
            {
                bool mrec = IsMrecUnit(unit);
                CreateBannerView(a, mrec);
                if (mrec)
                {
                    MaxSdk.SetMRecPlacement(unit, p.Id);
                    MaxSdk.ShowMRec(unit);
                }
                else
                {
                    MaxSdk.SetBannerPlacement(unit, p.Id);
                    MaxSdk.ShowBanner(unit);
                }
                visibleBannerUnits.Add(unit);
                if (refreshPaused.Remove(unit))
                {
                    if (mrec) MaxSdk.StartMRecAutoRefresh(unit);
                    else MaxSdk.StartBannerAutoRefresh(unit);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} Banner {p.Id} could not be created: {e.Message}");
                return AdShowResult.Failed;
            }

            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Showing banner {p.Id}{(loadedBannerUnits.Contains(unit) ? "" : " (displays once loaded)")}.");
            return AdShowResult.Completed;
        }

        // MAX creates the view hidden and starts loading it at once.
        private void CreateBannerView(AdUnitAssignment a, bool mrec)
        {
            AdPlacement p = a.Placement;
            string unit = a.AdUnitId;
            if (createdBannerUnits.Contains(unit)) return;
            MaxSdkBase.AdViewConfiguration config;
            if (p.Position == BannerPosition.Custom)
            {
                Vector2 at = BannerLayout.CustomTopLeftDp(p.CustomCenter, SizeDp(p, mrec), PixelsPerDp());
                config = new MaxSdkBase.AdViewConfiguration(at.x, at.y);
            }
            else
            {
                config = new MaxSdkBase.AdViewConfiguration(ToMaxPosition(p.Position));
            }
            if (mrec)
            {
                MaxSdk.CreateMRec(unit, config);
            }
            else
            {
                // MAX 8.x: adaptive is a creation option (AdViewConfiguration.IsAdaptive, default true).
                // Collapsible has no MAX equivalent and maps to adaptive; Standard stays 320x50.
                config.IsAdaptive = p.Size == BannerSize.Adaptive || p.Size == BannerSize.Collapsible;
                MaxSdk.CreateBanner(unit, config);
            }
            createdBannerUnits.Add(unit);
        }

        // AdPlacement.PreloadBanner: the view is created hidden when the placement is allowed, so the first show has an ad.
        private void PreloadBanner(AdUnitAssignment a)
        {
            if (disposed || !initialized || !a.Placement.PreloadBanner || a.Placement.Size == BannerSize.Collapsible) return;
            if (!activePlacements.Contains(a.Placement.Id) || createdBannerUnits.Contains(a.AdUnitId)) return;
            try
            {
                CreateBannerView(a, IsMrecUnit(a.AdUnitId));
                if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Banner {a.Placement.Id} preloading (hidden).");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Banner {a.Placement.Id} could not be preloaded: {e.Message}");
            }
        }

        // A hidden view keeps refreshing in MAX: requests without impressions. Stopped only after the first load, so a
        // preloaded view still gets its ad; restarted by the next show.
        private void PauseRefresh(string unit)
        {
            if (visibleBannerUnits.Contains(unit) || !loadedBannerUnits.Contains(unit) || !refreshPaused.Add(unit)) return;
            try
            {
                if (IsMrecUnit(unit)) MaxSdk.StopMRecAutoRefresh(unit);
                else MaxSdk.StopBannerAutoRefresh(unit);
            }
            catch (Exception e)
            {
                refreshPaused.Remove(unit);
                Debug.LogWarning($"{LogPrefix} Banner {Describe(unit)} refresh not stopped: {e.Message}");
            }
        }

        private void DestroyBannerUnit(string unit)
        {
            if (!createdBannerUnits.Remove(unit)) return;
            if (IsMrecUnit(unit)) MaxSdk.DestroyMRec(unit);
            else MaxSdk.DestroyBanner(unit);
            bool wasLoaded = loadedBannerUnits.Remove(unit);
            visibleBannerUnits.Remove(unit);
            refreshPaused.Remove(unit);
            bannerHeightPixels.Remove(unit); // recreated later, maybe in another orientation
            retryAttempts.Remove(unit);
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Banner {Describe(unit)} destroyed.");
            if (wasLoaded && !disposed) NotifyUnit(unit);
        }

        // --- Shared callback bodies ---

        private void OnUnitLoaded(string unit)
        {
            if (!unitPlacements.ContainsKey(unit)) return;
            loadingUnits.Remove(unit);
            retryAttempts.Remove(unit);
            loadedUnits.Add(unit);
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} {Describe(unit)} loaded.");
            NotifyUnit(unit);
        }

        private void OnUnitLoadFailed(string unit, MaxSdkBase.ErrorInfo error)
        {
            if (!unitPlacements.ContainsKey(unit)) return;
            loadingUnits.Remove(unit);
            string message = ErrorText(error);
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} {Describe(unit)} load failed: {message}");
            RaiseLoadFailed(unit, message);
            if (!disposed && IsUnitActive(unit)) _ = RetryAfterBackoffAsync(unit);
        }

        private void ResolvePending(string unit, AdShowResult result)
        {
            if (pendingShows.TryGetValue(unit, out var tcs)) tcs.TrySetResult(result);
        }

        private void OnDisplayFailed(string unit, MaxSdkBase.ErrorInfo error)
        {
            if (!unitPlacements.ContainsKey(unit)) return;
            Debug.LogWarning($"{LogPrefix} {Describe(unit)} display failed: {ErrorText(error)}");
            ResolvePending(unit, AdShowResult.Failed);
        }

        private void OnRevenue(string unit, MaxSdkBase.AdInfo info)
        {
            if (info == null || !unitPlacements.TryGetValue(unit, out List<string> ids) || ids.Count == 0) return;
            if (info.Revenue < 0)
            {
                Debug.LogWarning($"{LogPrefix} {Describe(unit)} paid impression without revenue ({info.Revenue}) - not reported.");
                return;
            }

            // The placement MAX echoes back (we pass ours to Show/Set*Placement), else the one on screen, else the first active.
            string placementId = null;
            if (!string.IsNullOrEmpty(info.Placement) && ids.Contains(info.Placement)) placementId = info.Placement;
            else if (showingPlacement.TryGetValue(unit, out string showing)) placementId = showing;
            else
            {
                foreach (string id in ids)
                    if (activePlacements.Contains(id)) { placementId = id; break; }
                placementId ??= ids[0];
            }

            AdFormat format = assigned.TryGetValue(placementId, out AdUnitAssignment a) ? a.Format : unitFormats[unit];
            ImpressionPaid?.Invoke(new AdImpression(placementId, AdProviderIds.Max, format, unit, info.NetworkName,
                                                    info.Revenue, RevenueCurrency, info.RevenuePrecision));
        }

        private void OnAdViewLoaded(string unit)
        {
            if (!unitPlacements.ContainsKey(unit)) return;
            retryAttempts.Remove(unit);
            if (!loadedBannerUnits.Add(unit)) return; // auto-refresh
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} {Describe(unit)} loaded.");
            PauseRefresh(unit); // preloaded or hidden before it loaded
            NotifyUnit(unit);
        }

        private void OnAdViewLoadFailed(string unit, MaxSdkBase.ErrorInfo error)
        {
            if (!unitPlacements.ContainsKey(unit)) return;
            string message = ErrorText(error);
            // MAX retries ad view loads internally; report only.
            Debug.LogWarning($"{LogPrefix} {Describe(unit)} load failed: {message}");
            RaiseLoadFailed(unit, message);
        }

        private void NotifyUnit(string unit)
        {
            if (!unitPlacements.TryGetValue(unit, out List<string> ids)) return;
            foreach (string id in ids.ToArray()) PlacementReadyChanged?.Invoke(id);
        }

        private void RaiseLoadFailed(string unit, string message)
        {
            if (!unitPlacements.TryGetValue(unit, out List<string> ids)) return;
            foreach (string id in ids.ToArray())
                if (activePlacements.Contains(id)) PlacementLoadFailed?.Invoke(id, message);
        }

        // --- Interstitial callbacks ---

        private void OnInterstitialLoaded(string unit, MaxSdkBase.AdInfo _) => OnUnitLoaded(unit);
        private void OnInterstitialLoadFailed(string unit, MaxSdkBase.ErrorInfo error) => OnUnitLoadFailed(unit, error);
        private void OnInterstitialHidden(string unit, MaxSdkBase.AdInfo _) => ResolvePending(unit, AdShowResult.Completed);
        private void OnInterstitialDisplayFailed(string unit, MaxSdkBase.ErrorInfo error, MaxSdkBase.AdInfo _) => OnDisplayFailed(unit, error);
        private void OnInterstitialRevenue(string unit, MaxSdkBase.AdInfo info) => OnRevenue(unit, info);

        // --- Rewarded callbacks ---

        private void OnRewardedLoaded(string unit, MaxSdkBase.AdInfo _) => OnUnitLoaded(unit);
        private void OnRewardedLoadFailed(string unit, MaxSdkBase.ErrorInfo error) => OnUnitLoadFailed(unit, error);

        private void OnRewardedReward(string unit, MaxSdkBase.Reward _, MaxSdkBase.AdInfo __)
        {
            if (rewardSignals.TryGetValue(unit, out var signal)) signal.TrySetResult(true);
        }

        // Hidden = Skipped for now; ResolveRewardAsync upgrades it to Completed if the reward callback landed.
        private void OnRewardedHidden(string unit, MaxSdkBase.AdInfo _) => ResolvePending(unit, AdShowResult.Skipped);
        private void OnRewardedDisplayFailed(string unit, MaxSdkBase.ErrorInfo error, MaxSdkBase.AdInfo _) => OnDisplayFailed(unit, error);
        private void OnRewardedRevenue(string unit, MaxSdkBase.AdInfo info) => OnRevenue(unit, info);

        // --- App open callbacks ---

        private void OnAppOpenLoaded(string unit, MaxSdkBase.AdInfo _) => OnUnitLoaded(unit);
        private void OnAppOpenLoadFailed(string unit, MaxSdkBase.ErrorInfo error) => OnUnitLoadFailed(unit, error);
        private void OnAppOpenHidden(string unit, MaxSdkBase.AdInfo _) => ResolvePending(unit, AdShowResult.Completed);
        private void OnAppOpenDisplayFailed(string unit, MaxSdkBase.ErrorInfo error, MaxSdkBase.AdInfo _) => OnDisplayFailed(unit, error);
        private void OnAppOpenRevenue(string unit, MaxSdkBase.AdInfo info) => OnRevenue(unit, info);

        // --- Banner / MRec callbacks ---

        private void OnBannerLoaded(string unit, MaxSdkBase.AdInfo _) => OnAdViewLoaded(unit);
        private void OnBannerLoadFailed(string unit, MaxSdkBase.ErrorInfo error) => OnAdViewLoadFailed(unit, error);
        private void OnBannerRevenue(string unit, MaxSdkBase.AdInfo info) => OnRevenue(unit, info);

        private void OnMRecLoaded(string unit, MaxSdkBase.AdInfo _) => OnAdViewLoaded(unit);
        private void OnMRecLoadFailed(string unit, MaxSdkBase.ErrorInfo error) => OnAdViewLoadFailed(unit, error);
        private void OnMRecRevenue(string unit, MaxSdkBase.AdInfo info) => OnRevenue(unit, info);

        // --- Banner metrics ---

        /// <inheritdoc />
        public float GetBannerHeightPixels(string placementId) =>
            TryGetBanner(placementId, out AdUnitAssignment a) && loadedBannerUnits.Contains(a.AdUnitId) ? HeightPixels(a) : 0f;

        /// <inheritdoc />
        public float GetExpectedBannerHeightPixels(string placementId) =>
            TryGetBanner(placementId, out AdUnitAssignment a) ? HeightPixels(a) : 0f;

        // MAX reports no view size: the height follows from the format (MREC 250 dp, adaptive banner from MAX, else
        // 50 dp / 90 dp on tablets). Computed once per unit - each MaxSdkUtils call is a native call.
        private float HeightPixels(AdUnitAssignment a)
        {
            if (bannerHeightPixels.TryGetValue(a.AdUnitId, out float px)) return px;
            try
            {
                px = SizeDp(a.Placement, IsMrecUnit(a.AdUnitId)).y * PixelsPerDp();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Banner {a.Placement.Id} height unknown: {e.Message}");
                return 0f;
            }
            bannerHeightPixels[a.AdUnitId] = px;
            return px;
        }

        private float PixelsPerDp()
        {
            if (pixelsPerDp > 0f) return pixelsPerDp;
            float density = MaxSdkUtils.GetScreenDensity();
            pixelsPerDp = density > 0f ? density : 1f;
            return pixelsPerDp;
        }

        private Vector2 SizeDp(AdPlacement p, bool mrec)
        {
            if (mrec) return new Vector2(300f, 250f);
            if (p.Size == BannerSize.Adaptive || p.Size == BannerSize.Collapsible)
            {
                // Adaptive banners span the width, and their height is the one for the full width.
                float height = MaxSdkUtils.GetAdaptiveBannerHeight();
                return new Vector2(Screen.safeArea.width / PixelsPerDp(), height > 0f ? height : 50f);
            }
            return MaxSdkUtils.IsTablet() ? new Vector2(728f, 90f) : new Vector2(320f, 50f);
        }

        // --- Helpers ---

        private static MaxSdkBase.AdViewPosition ToMaxPosition(BannerPosition pos) => pos switch
        {
            BannerPosition.Top         => MaxSdkBase.AdViewPosition.TopCenter,
            BannerPosition.Bottom      => MaxSdkBase.AdViewPosition.BottomCenter,
            BannerPosition.TopLeft     => MaxSdkBase.AdViewPosition.TopLeft,
            BannerPosition.TopRight    => MaxSdkBase.AdViewPosition.TopRight,
            BannerPosition.BottomLeft  => MaxSdkBase.AdViewPosition.BottomLeft,
            BannerPosition.BottomRight => MaxSdkBase.AdViewPosition.BottomRight,
            BannerPosition.Center      => MaxSdkBase.AdViewPosition.Centered,
            _                          => MaxSdkBase.AdViewPosition.BottomCenter,
        };

        private static string AdTypeOf(AdFormat format) => format switch
        {
            AdFormat.Interstitial => "interstitial",
            AdFormat.Rewarded     => "rewarded",
            AdFormat.AppOpen      => "app_open",
            AdFormat.Mrec         => "mrec",
            _                     => "banner",
        };

        private static string ErrorText(MaxSdkBase.ErrorInfo error) =>
            error == null ? "unknown error" : $"{error.Code}: {error.Message}";

        // A unit is an MREC view when its format is Mrec or its first placement asks for the medium rectangle size.
        private bool IsMrecUnit(string unit)
        {
            if (unitFormats.TryGetValue(unit, out AdFormat format) && format == AdFormat.Mrec) return true;
            return unitPlacements.TryGetValue(unit, out List<string> ids) && ids.Count > 0
                && assigned.TryGetValue(ids[0], out AdUnitAssignment a) && a.Placement.Size == BannerSize.MediumRectangle;
        }

        private bool TryGetBanner(string placementId, out AdUnitAssignment a)
        {
            if (placementId != null && assigned.TryGetValue(placementId, out a) && a.Placement.IsBannerLike) return true;
            a = default;
            return false;
        }

        private string Describe(string unit)
        {
            string type = unitFormats.TryGetValue(unit, out AdFormat f) ? AdTypeOf(f) : "ad";
            string ids = unitPlacements.TryGetValue(unit, out List<string> list) ? string.Join(",", list) : unit;
            return $"{type} {ids}";
        }
    }
}
