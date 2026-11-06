using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#if GAMELIB_ADMOB
using GoogleMobileAds.Api;
using AdFormat = FlapGame.GameLib.Ads.AdFormat;
#endif

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Google AdMob behind <see cref="IAdsProviderAdapter"/>. It only loads, reports and shows: routes, cooldowns, caps
    /// and no-ads belong to the composite. A fullscreen placement is loaded only while it is active
    /// (<see cref="SetPlacementActive"/>) and is reloaded after every show or failure (exponential backoff, capped at
    /// 60 s). Banners are created on their first <see cref="ShowAsync"/> and really released by
    /// <see cref="DestroyBanner"/> or deactivation.
    /// <para>
    /// Fullscreen placements that share a format and an ad unit id share one <see cref="Slot"/>: a single ad is loaded
    /// for all of them and whichever shows first consumes it. Without this, five interstitial placements on one unit
    /// id held five loaded ads, four of which expired unshown (AdMob request count ×5, show rate ÷5).
    /// </para>
    /// <para>
    /// Threading: since plugin 10.7.0 the SDK raises its callbacks off the Unity main thread and
    /// <c>MobileAds.RaiseAdEventsOnUnityMainThread</c> is obsolete, so every SDK callback is marshalled here to the
    /// main thread (captured <see cref="SynchronizationContext"/>, else <c>MobileAdsEventExecutor.ExecuteInUpdate</c>)
    /// before it touches state or raises an event.
    /// </para>
    /// <para>
    /// Once <c>Show()</c> was called the caller's cancellation token is ignored: the ad is on screen and its real
    /// outcome is awaited, so the composite never opens a second ad on top of it.
    /// </para>
    /// <para>
    /// Native ads (0.9.0) are drawn by GameLib's own native views (<see cref="AdMobNativeBridge"/>), one native ad per
    /// key: a banner-like native (NativeBanner, NativeCollapsible) has one key per placement and behaves like a banner
    /// (loaded on its first show, or on activation with <see cref="AdPlacement.PreloadBanner"/>; a shown ad is spent
    /// once hidden and a fresh one loads while the placement stays active). NativeInterstitial shares a
    /// <see cref="Slot"/> per ad unit like the other fullscreen formats.
    /// </para>
    /// </summary>
    public sealed class AdMobAdsAdapter : IAdsProviderAdapter, IBannerMetrics, IAdClickSource, IAdsDebugger, IAdsAudio, IRewardVerification, IBannerExpansion, INativeCollapseSource, INativeTimingAdapter
    {
        private const string LogPrefix = "[Ads/AdMob]";

        /// <summary>Upper bound of the reload backoff, in seconds.</summary>
        private const double MaxRetryDelaySeconds = 60;

        private readonly Dictionary<string, Unit> units = new();
        // Fullscreen ad state per (format, ad unit id), shared by every placement routed to that unit.
        private readonly Dictionary<string, Slot> slots = new();
        // Placements activated before the SDK finished initializing; loaded once it has.
        private readonly HashSet<string> activeBeforeInit = new();
        private readonly CancellationTokenSource lifetime = new();
        // Shows awaiting their close callback; failed on Dispose so no caller hangs after teardown.
        private readonly HashSet<TaskCompletionSource<AdShowResult>> pendingShows = new();
        // Native bridge keys -> banner-like native placement / NativeInterstitial slot.
        private readonly Dictionary<string, Unit> nativeUnits = new();
        private readonly Dictionary<string, Slot> nativeSlots = new();
#if GAMELIB_ADMOB
        private AdMobNativeBridge nativeBridge; // created on the first native call
#endif

        private SynchronizationContext mainContext;
        private int mainThreadId;
        private AdsModule settings;
        private ConsentState consent = new();
        private bool initialized;
        private bool disposed;
        private string rewardUserId, rewardCustomData; // SSV of the next rewarded show (SetRewardVerification)

        /// <summary>Creates the adapter. Call on the main thread: it captures the main-thread context for SDK callbacks.</summary>
        public AdMobAdsAdapter()
        {
            CaptureMainThread();
        }

        /// <inheritdoc />
        public string ProviderId => AdProviderIds.AdMob;

        /// <inheritdoc />
        public bool IsInitialized => initialized;

        /// <inheritdoc />
        public event Action<string> PlacementReadyChanged;

        /// <inheritdoc />
        public event Action<string, string> PlacementLoadFailed;

        /// <inheritdoc />
        public event Action<AdImpression> ImpressionPaid;

        /// <inheritdoc />
        public event Action<string> AdClicked;

        /// <inheritdoc />
        public event Action<string, bool> BannerExpandedChanged;

        // (0.9.0) Fullscreen content that opens within this many seconds of a banner tap was opened by the tap.
        private const float BannerTapToOpenSeconds = 3f;

        /// <inheritdoc />
        public event Action<string, bool> NativeExpandedChanged;

        /// <summary>Grace to wait for a reward callback that arrives after the close callback.</summary>
        public TimeSpan LateRewardGrace { get; set; } = RewardGrace.DefaultGrace;

        /// <inheritdoc />
        public async ValueTask InitializeAsync(AdsProviderContext context, CancellationToken cancellationToken = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (disposed) throw new ObjectDisposedException(nameof(AdMobAdsAdapter));
            if (initialized) return;

            CaptureMainThread();
            settings = context.Settings;
            RegisterUnits(context);
            consent = context.Consent?.Clone() ?? new ConsentState();

#if GAMELIB_ADMOB
            // Request configuration (test devices, child-directed tags) must be set before MobileAds.Initialize.
            PushRequestConfiguration();
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Consent at init: {consent}.");

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            MobileAds.Initialize(_ => tcs.TrySetResult(true));
            using (cancellationToken.Register(() => tcs.TrySetCanceled()))
                await tcs.Task;
            if (disposed) return;

            initialized = true;
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Initialized with {units.Count} placement(s).");

            // Initialization never loads by itself: only placements the composite already activated start loading.
            foreach (string id in new List<string>(activeBeforeInit))
                if (units.TryGetValue(id, out Unit unit) && unit.Active) EnsureLoading(unit);
            activeBeforeInit.Clear();
#else
            await Task.CompletedTask;
            initialized = true;
#endif
        }

        /// <inheritdoc />
        // (0.9.0) Effective native timings pushed by the composite (plan / remote overrides); absent = the local asset's values.
        private readonly Dictionary<string, (int close, int collapse)> nativeTimings = new();

        /// <inheritdoc />
        public void SetNativeTiming(string placementId, int closeDelaySeconds, int collapseButtonDelaySeconds)
        {
            if (placementId != null) nativeTimings[placementId] = (Math.Max(0, closeDelaySeconds), Math.Max(0, collapseButtonDelaySeconds));
        }

        private int NativeCloseDelay(AdPlacement p) =>
            nativeTimings.TryGetValue(p.Id, out var t) ? t.close : Math.Max(0, p.NativeCloseDelaySeconds);

        private int NativeCollapseDelay(AdPlacement p) =>
            nativeTimings.TryGetValue(p.Id, out var t) ? t.collapse : Math.Max(0, p.NativeCollapseButtonDelaySeconds);

        public void SetPlacementActive(string placementId, bool active)
        {
            if (disposed || placementId == null || !units.TryGetValue(placementId, out Unit unit)) return;

            if (active)
            {
                unit.Active = true;
                if (!initialized) { activeBeforeInit.Add(placementId); return; }
#if GAMELIB_ADMOB
                EnsureLoading(unit);
#endif
                return;
            }

            activeBeforeInit.Remove(placementId);
            if (!unit.Active) return;
#if GAMELIB_ADMOB
            bool wasReady = IsPlacementReady(placementId);
#endif
            unit.Active = false;
            unit.ResetLoad(); // invalidates in-flight loads and pending retries
#if GAMELIB_ADMOB
            // A shared fullscreen ad stays loaded while another placement on the same unit still wants it.
            if (unit.Slot != null && !unit.Slot.IsActive)
            {
                unit.Slot.ResetLoad();
                ReleaseFullscreen(unit.Slot);
                ReleaseNative(unit.Slot);
            }
            bool wasExpanded = unit.BannerExpansionReported;
            ReleaseBanner(unit);
            ReleaseNative(unit);
            if (wasExpanded) BannerExpandedChanged?.Invoke(placementId, false);
            if (wasReady) RaiseReadyChanged(placementId);
#endif
        }

        /// <inheritdoc />
        public bool IsPlacementReady(string placementId)
        {
#if GAMELIB_ADMOB
            if (!initialized || disposed || placementId == null || !units.TryGetValue(placementId, out Unit unit)) return false;
            switch (unit.Format)
            {
                // An inactive placement is never ready, even when its shared slot holds an ad for another placement.
                case AdFormat.Interstitial: return unit.Active && unit.Slot.Interstitial != null;
                case AdFormat.Rewarded:     return unit.Active && unit.Slot.Rewarded != null;
                case AdFormat.RewardedInterstitial: return unit.Active && unit.Slot.RewardedInterstitial != null;
                case AdFormat.AppOpen:      return unit.Active && IsAppOpenReady(unit.Slot);
                case AdFormat.NativeInterstitial: return unit.Active && unit.Slot.NativeReady;
                default:
                    return unit.NativeKey != null
                        ? unit.Active && unit.BannerLoaded
                        : unit.Banner != null && unit.BannerLoaded;
            }
#else
            return false;
#endif
        }

        /// <inheritdoc />
        public async ValueTask<AdShowResult> ShowAsync(string placementId, CancellationToken cancellationToken = default)
        {
#if GAMELIB_ADMOB
            if (disposed || placementId == null || !units.TryGetValue(placementId, out Unit unit))
                return AdShowResult.Failed;
            switch (unit.Format)
            {
                case AdFormat.Interstitial: return await ShowInterstitialAsync(unit, cancellationToken);
                case AdFormat.Rewarded:     return await ShowRewardedAsync(unit, cancellationToken);
                case AdFormat.RewardedInterstitial: return await ShowRewardedInterstitialAsync(unit, cancellationToken);
                case AdFormat.AppOpen:      return await ShowAppOpenAsync(unit, cancellationToken);
                case AdFormat.NativeInterstitial: return await ShowNativeInterstitialAsync(unit, cancellationToken);
                default:                    return unit.NativeKey != null ? ShowNativeBanner(unit) : ShowBanner(unit);
            }
#else
            await Task.CompletedTask;
            return AdShowResult.Failed;
#endif
        }

        /// <inheritdoc />
        public float GetBannerHeightPixels(string placementId)
        {
#if GAMELIB_ADMOB
            if (disposed || placementId == null || !units.TryGetValue(placementId, out Unit unit)) return 0f;
            // A native bar reports its height only while it is on screen (NativeCollapsible: the collapsed bar).
            if (unit.NativeKey != null) return unit.NativeOnScreen ? NativeHeightPixels(unit) : 0f;
            if (unit.Banner == null || !unit.BannerLoaded) return 0f;
            // Measured once per loaded view: a layout script may ask every frame, and each measure is a native call.
            if (unit.LoadedHeightPixels > 0f) return unit.LoadedHeightPixels;
            try { return unit.LoadedHeightPixels = unit.Banner.GetHeightInPixels(); }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Banner {placementId} height unknown: {e.Message}");
                return 0f;
            }
#else
            return 0f;
#endif
        }

        /// <inheritdoc />
        public float GetExpectedBannerHeightPixels(string placementId)
        {
#if GAMELIB_ADMOB
            if (disposed || !initialized || placementId == null || !units.TryGetValue(placementId, out Unit unit) || !unit.Active)
                return 0f;
            if (unit.NativeKey != null) return NativeHeightPixels(unit); // the bar height is fixed by the placement
            float loaded = GetBannerHeightPixels(placementId);
            if (loaded > 0f) return loaded;
            if (unit.ExpectedHeightPixels > 0f) return unit.ExpectedHeightPixels;
            try
            {
                // The height this device's banner had last time (exact), else an estimate of the requested size.
                float remembered = PlayerPrefs.GetFloat(BannerHeightKey(placementId), 0f);
                unit.ExpectedHeightPixels = remembered > 0f ? remembered : EstimateBannerHeightPixels(GetAdSize(unit.Placement));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Banner {placementId} expected height unknown: {e.Message}");
            }
            return unit.ExpectedHeightPixels;
#else
            return 0f;
#endif
        }

        /// <inheritdoc />
        public void HideBanner(string placementId)
        {
#if GAMELIB_ADMOB
            if (placementId == null || !units.TryGetValue(placementId, out Unit unit)) return;
            unit.BannerVisible = false;
            if (unit.NativeKey != null) { HideNative(unit); return; }
            if (unit.Banner != null && unit.BannerExpanded)
            {
                // (0.9.0) BannerView.Hide() leaves the SDK's expanded overlay of a collapsible banner on screen, and the
                // SDK has no collapse call: drop the view (which takes the overlay with it); the next show loads a new one.
                DropExpandedBanner(unit);
                if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Banner {placementId} was expanded: destroyed instead of hidden.");
                return;
            }
            unit.Banner?.Hide();
#endif
        }

        /// <inheritdoc />
        public void DestroyBanner(string placementId)
        {
#if GAMELIB_ADMOB
            if (placementId == null || !units.TryGetValue(placementId, out Unit unit)) return;
            if (unit.NativeKey != null) { DestroyNative(unit); return; }
            if (unit.Banner == null) return;
            bool wasLoaded = unit.BannerLoaded;
            bool wasExpanded = unit.BannerExpansionReported;
            unit.Generation++; // a pending banner retry belongs to the destroyed view
            unit.RetryPending = false;
            unit.RetryAttempt = 0;
            ReleaseBanner(unit);
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Banner {placementId} destroyed.");
            if (wasExpanded) BannerExpandedChanged?.Invoke(placementId, false); // the destroyed view never reports it closed
            if (wasLoaded) RaiseReadyChanged(placementId);
#endif
        }

        /// <summary>
        /// AdMob reads the IAB TCF string that Google UMP writes, so only the age flag is applied here:
        /// <see cref="ConsentState.IsAgeRestrictedUser"/> sets <c>RequestConfiguration.AgeRestrictedTreatment</c> to
        /// <c>Child</c> (the 11.x replacement of the deprecated <c>TagForChildDirectedTreatment</c> /
        /// <c>TagForUnderAgeOfConsent</c>); otherwise it is left unspecified. Everything else is logged.
        /// </summary>
        public void ApplyConsent(ConsentState state)
        {
            if (state == null) return;
            consent = state.Clone();
#if GAMELIB_ADMOB
            PushRequestConfiguration();
#endif
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Consent applied: {consent} (personalisation follows the TCF string written by UMP).");
        }

        /// <summary>Google's SSV options for the next rewarded / rewarded interstitial show (AdMob console: the ad unit's SSV callback URL).</summary>
        public void SetRewardVerification(string userId, string customData)
        {
            rewardUserId     = userId;
            rewardCustomData = customData;
        }

#if GAMELIB_ADMOB
        // One show's: taken (and cleared) by the show that uses it. Null = nothing to verify.
        private ServerSideVerificationOptions TakeRewardVerification()
        {
            string userId = rewardUserId, customData = rewardCustomData;
            rewardUserId = rewardCustomData = null;
            if (string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(customData)) return null;
            return new ServerSideVerificationOptions { UserId = userId, CustomData = customData };
        }
#endif

        /// <summary>Google's app volume and mute: video ads start at this volume (Google: only for an app with its own volume control).</summary>
        public void ApplyAdAudio(float volume, bool muted)
        {
            if (disposed) return;
#if GAMELIB_ADMOB
            MobileAds.SetApplicationVolume(volume);
            MobileAds.SetApplicationMuted(muted);
#endif
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} App volume {volume:0.##}, muted {muted}.");
        }

        /// <summary>
        /// Google's Ad Inspector. It opens on test devices only (added in the AdMob console > Settings > Test devices, or
        /// in AdsModule > AdMob Test Device Ids with Test Mode on); elsewhere it closes at once with an error.
        /// </summary>
        public Task<bool> OpenDebuggerAsync()
        {
            if (disposed || !initialized) return Task.FromResult(false);
#if GAMELIB_ADMOB
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            MobileAds.OpenAdInspector(error => RunOnMain(() =>
            {
                if (error == null)
                {
                    tcs.TrySetResult(true);
                    return;
                }
                Debug.LogWarning($"{LogPrefix} Ad Inspector: {error.GetMessage()} - it opens on AdMob test devices only.");
                tcs.TrySetResult(false);
            }));
            return tcs.Task;
#else
            return Task.FromResult(false);
#endif
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
#if GAMELIB_ADMOB
            foreach (TaskCompletionSource<AdShowResult> pending in new List<TaskCompletionSource<AdShowResult>>(pendingShows))
                pending.TrySetResult(AdShowResult.Failed);
            pendingShows.Clear();
            foreach (Unit unit in units.Values)
            {
                unit.Generation++;
                unit.Active = false;
                ReleaseBanner(unit);
                ReleaseNative(unit);
            }
            foreach (Slot slot in slots.Values)
            {
                slot.Generation++;
                ReleaseFullscreen(slot);
                slot.NativeShowing = null; // teardown: a native interstitial on screen goes too
                ReleaseNative(slot);
            }
            nativeBridge?.Dispose();
            nativeBridge = null;
#endif
            nativeUnits.Clear();
            nativeSlots.Clear();
            activeBeforeInit.Clear();
            lifetime.Dispose();
        }

        // ---------------------------------------------------------------- setup

        private void CaptureMainThread()
        {
            mainContext  = SynchronizationContext.Current;
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        private void RegisterUnits(AdsProviderContext context)
        {
            // A retried init (after a faulted / cancelled run) rebuilds both: stale slots would keep the old run's units.
            units.Clear();
            slots.Clear();
            nativeUnits.Clear();
            nativeSlots.Clear();
            foreach (AdUnitAssignment assignment in context.Units)
            {
                AdPlacement placement = assignment.Placement;
                if (placement == null || string.IsNullOrEmpty(placement.Id)) continue;

                string unitId = assignment.AdUnitId;
                if (string.IsNullOrEmpty(unitId) && context.TestMode)
                {
                    unitId = GoogleTestUnitId(placement.Format);
                    if (AdsLog.Verbose) Debug.Log($"{LogPrefix} {placement.Id}: no unit id, test mode uses Google's test unit {unitId}.");
                }
                if (string.IsNullOrEmpty(unitId))
                {
                    Debug.LogWarning($"{LogPrefix} {placement.Id}: no AdMob unit id for this platform - placement ignored.");
                    continue;
                }
                var unit = new Unit(placement, unitId);
                units[placement.Id] = unit;
                if (IsFullscreen(placement.Format))
                {
                    string key = $"{placement.Format}|{unitId}";
                    if (!slots.TryGetValue(key, out Slot slot))
                    {
                        slots[key] = slot = new Slot(placement.Format, unitId);
                        if (placement.Format == AdFormat.NativeInterstitial)
                            nativeSlots[slot.NativeKey = "slot|" + key] = slot;
                    }
                    slot.Members.Add(unit);
                    unit.Slot = slot;
                }
                else if (placement.IsNative)
                {
                    nativeUnits[unit.NativeKey = "placement|" + placement.Id] = unit;
                }
            }
            foreach (Slot slot in slots.Values)
                if (slot.Members.Count > 1)
                    if (AdsLog.Verbose) Debug.Log($"{LogPrefix} {slot.Format} unit {slot.AdUnitId} is shared by {slot.Name}: one ad loaded for all.");
        }

        private static bool IsFullscreen(AdFormat format) =>
            format is AdFormat.Interstitial or AdFormat.Rewarded or AdFormat.RewardedInterstitial or AdFormat.AppOpen
                   or AdFormat.NativeInterstitial;

        /// <summary>Google's public demo unit ids (always fill, never pay). Used in test mode when a route has no id.</summary>
        private static string GoogleTestUnitId(AdFormat format)
        {
#if UNITY_IOS
            return format switch
            {
                AdFormat.Interstitial => "ca-app-pub-3940256099942544/4411468910",
                AdFormat.Rewarded     => "ca-app-pub-3940256099942544/1712485313",
                AdFormat.RewardedInterstitial => "ca-app-pub-3940256099942544/6978759866",
                AdFormat.AppOpen      => "ca-app-pub-3940256099942544/5662855259",
                AdFormat.NativeBanner or AdFormat.NativeCollapsible or AdFormat.NativeInterstitial
                                      => "ca-app-pub-3940256099942544/3986624511",
                _                     => "ca-app-pub-3940256099942544/2934735716",
            };
#else
            return format switch
            {
                AdFormat.Interstitial => "ca-app-pub-3940256099942544/1033173712",
                AdFormat.Rewarded     => "ca-app-pub-3940256099942544/5224354917",
                AdFormat.RewardedInterstitial => "ca-app-pub-3940256099942544/5354046379",
                AdFormat.AppOpen      => "ca-app-pub-3940256099942544/9257395921",
                AdFormat.NativeBanner or AdFormat.NativeCollapsible or AdFormat.NativeInterstitial
                                      => "ca-app-pub-3940256099942544/2247696110",
                _                     => "ca-app-pub-3940256099942544/6300978111",
            };
#endif
        }

        /// <summary>Runs <paramref name="action"/> on the Unity main thread (inline when already there).</summary>
        private void RunOnMain(Action action)
        {
            if (Thread.CurrentThread.ManagedThreadId == mainThreadId) { Invoke(action); return; }
            if (mainContext != null) { mainContext.Post(_ => Invoke(action), null); return; }
#if GAMELIB_ADMOB
            global::GoogleMobileAds.Common.MobileAdsEventExecutor.ExecuteInUpdate(() => Invoke(action));
#else
            Invoke(action);
#endif
        }

        private static void Invoke(Action action)
        {
            try { action(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void RaiseReadyChanged(string placementId) => PlacementReadyChanged?.Invoke(placementId);

        // Every active placement on the slot sees the shared ad appear or go.
        private void RaiseReadyChanged(Slot slot)
        {
            foreach (Unit unit in slot.Members)
                if (unit.Active) RaiseReadyChanged(unit.Id);
        }

        private void RaiseClicked(string placementId) => AdClicked?.Invoke(placementId);

#if GAMELIB_ADMOB
        private void PushRequestConfiguration()
        {
            var config = new RequestConfiguration();
            if (settings != null && settings.TestMode && settings.AdMobTestDeviceIds is { Count: > 0 })
                config.TestDeviceIds = new List<string>(settings.AdMobTestDeviceIds);
            // Plugin 11.x deprecates TagForChildDirectedTreatment/TagForUnderAgeOfConsent (CS0618) in favour of
            // AgeRestrictedTreatment; Child is the strictest value and covers both old tags (COPPA + under age of consent).
            if (consent.IsAgeRestrictedUser)
                config.AgeRestrictedTreatment = AgeRestrictedTreatment.Child;
            config.MaxAdContentRating = (settings?.MaxAdContentRating ?? AdContentRating.Unspecified) switch
            {
                AdContentRating.G  => MaxAdContentRating.G,
                AdContentRating.PG => MaxAdContentRating.PG,
                AdContentRating.T  => MaxAdContentRating.T,
                AdContentRating.MA => MaxAdContentRating.MA,
                _                  => MaxAdContentRating.Unspecified,
            };
            MobileAds.SetRequestConfiguration(config);
        }

        // ---------------------------------------------------------------- loading

        // Banners/MRECs are created on their first ShowAsync: a never-shown banner costs no requests.
        private void EnsureLoading(Unit unit)
        {
            if (unit.Slot != null) EnsureLoading(unit.Slot);
            else if (unit.NativeKey != null) PreloadNative(unit);
            else PreloadBanner(unit);
        }

        // AdPlacement.PreloadBanner: the view is created hidden when the placement is allowed, so the first show has an
        // ad. Not for collapsible banners: their expanded state belongs to the first display.
        private void PreloadBanner(Unit unit)
        {
            AdPlacement p = unit.Placement;
            if (disposed || !initialized || !unit.Active || unit.Banner != null || !p.IsBannerLike || !p.PreloadBanner
                || p.Size == BannerSize.Collapsible)
                return;
            try
            {
                CreateBanner(unit);
                if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Banner {unit.Id} preloading (hidden).");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Banner {unit.Id} could not be preloaded: {e.Message}");
                ReleaseBanner(unit);
            }
        }

        private void EnsureLoading(Slot slot)
        {
            if (disposed || !initialized || !slot.IsActive || slot.Loading || slot.RetryPending) return;
            switch (slot.Format)
            {
                case AdFormat.Interstitial: if (slot.Interstitial == null) LoadInterstitial(slot); break;
                case AdFormat.Rewarded:     if (slot.Rewarded == null)     LoadRewarded(slot);     break;
                case AdFormat.RewardedInterstitial: if (slot.RewardedInterstitial == null) LoadRewardedInterstitial(slot); break;
                case AdFormat.AppOpen:      if (slot.AppOpen == null)      LoadAppOpen(slot);      break;
                // Not while its ad is on screen: the events of one key could not tell the two ads apart.
                case AdFormat.NativeInterstitial:
                    if (!slot.NativeReady && slot.NativeShowing == null) LoadNative(slot);
                    break;
            }
        }

        // Paid/click handlers are attached when the ad is shown, not loaded: only then is the placement known.
        private void LoadInterstitial(Slot slot)
        {
            int generation = BeginLoad(slot);
            InterstitialAd.Load(slot.AdUnitId, new AdRequest(), (ad, error) => RunOnMain(() =>
            {
                if (!AcceptLoad(slot, generation, ad == null, error?.GetMessage(), "Interstitial")) { ad?.Destroy(); return; }
                slot.Interstitial?.Destroy();
                slot.Interstitial = ad;
                if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Interstitial {slot.Name} loaded.");
                RaiseReadyChanged(slot);
            }));
        }

        private void LoadRewarded(Slot slot)
        {
            int generation = BeginLoad(slot);
            RewardedAd.Load(slot.AdUnitId, new AdRequest(), (ad, error) => RunOnMain(() =>
            {
                if (!AcceptLoad(slot, generation, ad == null, error?.GetMessage(), "Rewarded")) { ad?.Destroy(); return; }
                slot.Rewarded?.Destroy();
                slot.Rewarded = ad;
                if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Rewarded {slot.Name} loaded.");
                RaiseReadyChanged(slot);
            }));
        }

        private void LoadRewardedInterstitial(Slot slot)
        {
            int generation = BeginLoad(slot);
            RewardedInterstitialAd.Load(slot.AdUnitId, new AdRequest(), (ad, error) => RunOnMain(() =>
            {
                if (!AcceptLoad(slot, generation, ad == null, error?.GetMessage(), "Rewarded interstitial")) { ad?.Destroy(); return; }
                slot.RewardedInterstitial?.Destroy();
                slot.RewardedInterstitial = ad;
                if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Rewarded interstitial {slot.Name} loaded.");
                RaiseReadyChanged(slot);
            }));
        }

        private void LoadAppOpen(Slot slot)
        {
            int generation = BeginLoad(slot);
            AppOpenAd.Load(slot.AdUnitId, new AdRequest(), (ad, error) => RunOnMain(() =>
            {
                if (!AcceptLoad(slot, generation, ad == null, error?.GetMessage(), "App open")) { ad?.Destroy(); return; }
                slot.AppOpen?.Destroy();
                slot.AppOpen         = ad;
                slot.AppOpenLoadedAt = DateTime.UtcNow;
                if (AdsLog.Verbose) Debug.Log($"{LogPrefix} App open {slot.Name} loaded.");
                RaiseReadyChanged(slot);
            }));
        }

        private static int BeginLoad(Slot slot)
        {
            slot.Loading = true;
            return slot.Generation;
        }

        /// <summary>
        /// Common load-callback gate (main thread). False = discard the ad: stale (every placement deactivated or
        /// disposed since the request) or failed - a failure raises <see cref="PlacementLoadFailed"/> for each active
        /// placement on the slot and schedules a retry.
        /// </summary>
        private bool AcceptLoad(Slot slot, int generation, bool adMissing, string errorMessage, string label)
        {
            if (disposed || generation != slot.Generation || !slot.IsActive) return false;
            slot.Loading = false;
            if (errorMessage != null || adMissing)
            {
                string msg = errorMessage ?? "null ad";
                foreach (Unit unit in slot.Members)
                    if (unit.Active) PlacementLoadFailed?.Invoke(unit.Id, msg);
                ScheduleRetry(slot, label, msg, () => EnsureLoading(slot));
                return false;
            }
            slot.RetryAttempt = 0;
            return true;
        }

        // Exponential backoff (2, 4, 8 ... capped at 60 s) with unlimited attempts, as recommended by Google.
        // Without it a placement that failed twice (e.g. offline at launch) would stay dead for the session.
        private void ScheduleRetry(LoadState state, string label, string error, Action load)
        {
            state.RetryAttempt++;
            state.RetryPending = true;
            double seconds = Math.Min(MaxRetryDelaySeconds, Math.Pow(2, Math.Min(state.RetryAttempt, 10)));
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} {label} {state.Name} load failed (retry #{state.RetryAttempt} in {seconds:0}s): {error}");
            _ = RetryAfterAsync(state, state.Generation, seconds, load);
        }

        private async Task RetryAfterAsync(LoadState state, int generation, double seconds, Action load)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                return; // adapter disposed
            }
            // Task.Delay may resume on a pool thread when no context was captured; hop back before touching state.
            RunOnMain(() =>
            {
                if (disposed || generation != state.Generation || !state.IsActive) return;
                state.RetryPending = false;
                load();
            });
        }

        // App open ads go stale 4 hours after load; treat an expired ad as not ready and reload it.
        private bool IsAppOpenReady(Slot slot)
        {
            if (slot.AppOpen == null) return false;
            if (DateTime.UtcNow - slot.AppOpenLoadedAt < Slot.AppOpenExpiry) return true;

            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} App open {slot.Name} expired; discarding and reloading.");
            slot.AppOpen.Destroy();
            slot.AppOpen = null;
            EnsureLoading(slot);
            return false;
        }

        private static void ReleaseFullscreen(Slot slot)
        {
            slot.Interstitial?.Destroy();
            slot.Interstitial = null;
            slot.Rewarded?.Destroy();
            slot.Rewarded = null;
            slot.RewardedInterstitial?.Destroy();
            slot.RewardedInterstitial = null;
            slot.AppOpen?.Destroy();
            slot.AppOpen = null;
        }

        // (0.9.0) Destroys an expanded banner view (and the overlay it opened), reporting the expansion closed: the destroyed
        // view never raises OnAdFullScreenContentClosed, and the service would keep counting a fullscreen ad on screen.
        private void DropExpandedBanner(Unit unit)
        {
            bool wasLoaded = unit.BannerLoaded;
            bool wasReported = unit.BannerExpansionReported;
            unit.Generation++;
            unit.RetryPending = false;
            unit.RetryAttempt = 0;
            ReleaseBanner(unit);
            if (wasReported) BannerExpandedChanged?.Invoke(unit.Id, false);
            if (wasLoaded) RaiseReadyChanged(unit.Id);
        }

        private static void ReleaseBanner(Unit unit)
        {
            unit.BannerExpanded = false;
            unit.BannerExpansionReported = false;
            unit.Banner?.Destroy();
            unit.Banner        = null;
            unit.BannerLoaded  = false;
            unit.LoadedHeightPixels = 0f;
            unit.BannerVisible = false;
        }

        // ---------------------------------------------------------------- revenue

        private void RaisePaid(Unit unit, AdValue value, string adSource)
        {
            if (disposed || value == null) return;
            ImpressionPaid?.Invoke(new AdImpression(
                unit.Id, ProviderId, unit.Format, unit.AdUnitId, adSource,
                value.Value / 1_000_000.0, // AdValue.Value is in micros
                value.CurrencyCode,
                PrecisionName(value.Precision)));
        }

        private static string SafeAdSource(Func<ResponseInfo> getResponseInfo)
        {
            try
            {
                return getResponseInfo()?.GetLoadedAdapterResponseInfo()?.AdSourceName;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Could not read the ad source: {e.Message}");
                return null;
            }
        }

        private static string PrecisionName(AdValue.PrecisionType precision) => precision switch
        {
            AdValue.PrecisionType.Precise           => "exact",
            AdValue.PrecisionType.Estimated         => "estimated",
            AdValue.PrecisionType.PublisherProvided => "publisher_defined",
            _                                       => "unknown",
        };

        // ---------------------------------------------------------------- showing

        private async ValueTask<AdShowResult> ShowInterstitialAsync(Unit unit, CancellationToken cancellationToken)
        {
            InterstitialAd ad = unit.Active ? unit.Slot.Interstitial : null;
            if (ad == null)
            {
                Debug.LogWarning($"{LogPrefix} Interstitial {unit.Id} not shown: not ready (ad not loaded).");
                return AdShowResult.Failed;
            }
            if (cancellationToken.IsCancellationRequested) return AdShowResult.Failed;

            var tcs = new TaskCompletionSource<AdShowResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            ad.OnAdFullScreenContentClosed += () => RunOnMain(() => tcs.TrySetResult(AdShowResult.Completed));
            ad.OnAdFullScreenContentFailed += _ => RunOnMain(() => tcs.TrySetResult(AdShowResult.Failed));
            ad.OnAdPaid += value => RunOnMain(() => RaisePaid(unit, value, SafeAdSource(ad.GetResponseInfo)));
            ad.OnAdClicked += () => RunOnMain(() => RaiseClicked(unit.Id));

            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Showing interstitial {unit.Id}.");
            unit.Slot.Interstitial = null; // consumed, for every placement on the slot
            RaiseReadyChanged(unit.Slot);
            ad.Show();

            // The ad is on screen: the caller's token no longer applies (see class remarks).
            AdShowResult result = await AwaitCloseAsync(tcs);

            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Interstitial {unit.Id} result: {result}.");
            ad.Destroy();
            if (!disposed) EnsureLoading(unit.Slot);
            return result;
        }

        private async ValueTask<AdShowResult> ShowRewardedAsync(Unit unit, CancellationToken cancellationToken)
        {
            RewardedAd ad = unit.Active ? unit.Slot.Rewarded : null;
            if (ad == null)
            {
                Debug.LogWarning($"{LogPrefix} Rewarded {unit.Id} not shown: not ready (ad not loaded).");
                return AdShowResult.Failed;
            }
            if (cancellationToken.IsCancellationRequested) return AdShowResult.Failed;

            var tcs    = new TaskCompletionSource<AdShowResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var earned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Closed = Skipped for now; RewardGrace upgrades it if the reward callback lands (even slightly after the close).
            ad.OnAdFullScreenContentClosed += () => RunOnMain(() => tcs.TrySetResult(AdShowResult.Skipped));
            ad.OnAdFullScreenContentFailed += _ => RunOnMain(() => tcs.TrySetResult(AdShowResult.Failed));
            ad.OnAdPaid += value => RunOnMain(() => RaisePaid(unit, value, SafeAdSource(ad.GetResponseInfo)));
            ad.OnAdClicked += () => RunOnMain(() => RaiseClicked(unit.Id));

            ServerSideVerificationOptions verification = TakeRewardVerification();
            if (verification != null) ad.SetServerSideVerificationOptions(verification);
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Showing rewarded {unit.Id}.");
            unit.Slot.Rewarded = null; // consumed, for every placement on the slot
            RaiseReadyChanged(unit.Slot);
            ad.Show(_ => RunOnMain(() => earned.TrySetResult(true)));

            // The ad is on screen: the caller's token no longer applies (see class remarks).
            AdShowResult closed = await AwaitCloseAsync(tcs);
            AdShowResult result = closed == AdShowResult.Failed
                ? closed
                : await RewardGrace.ResolveAsync(closed, earned.Task, LateRewardGrace);

            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Rewarded {unit.Id} result: {result}.");
            ad.Destroy();
            if (!disposed) EnsureLoading(unit.Slot);
            return result;
        }

        // The intro screen Google requires is the game's, before ShowAsync (ads.ShowRewardedInterstitialAsync).
        private async ValueTask<AdShowResult> ShowRewardedInterstitialAsync(Unit unit, CancellationToken cancellationToken)
        {
            RewardedInterstitialAd ad = unit.Active ? unit.Slot.RewardedInterstitial : null;
            if (ad == null)
            {
                Debug.LogWarning($"{LogPrefix} Rewarded interstitial {unit.Id} not shown: not ready (ad not loaded).");
                return AdShowResult.Failed;
            }
            if (cancellationToken.IsCancellationRequested) return AdShowResult.Failed;

            var tcs    = new TaskCompletionSource<AdShowResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var earned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Closed = Skipped for now; RewardGrace upgrades it if the reward callback lands (even slightly after the close).
            ad.OnAdFullScreenContentClosed += () => RunOnMain(() => tcs.TrySetResult(AdShowResult.Skipped));
            ad.OnAdFullScreenContentFailed += _ => RunOnMain(() => tcs.TrySetResult(AdShowResult.Failed));
            ad.OnAdPaid += value => RunOnMain(() => RaisePaid(unit, value, SafeAdSource(ad.GetResponseInfo)));
            ad.OnAdClicked += () => RunOnMain(() => RaiseClicked(unit.Id));

            ServerSideVerificationOptions verification = TakeRewardVerification();
            if (verification != null) ad.SetServerSideVerificationOptions(verification);
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Showing rewarded interstitial {unit.Id}.");
            unit.Slot.RewardedInterstitial = null; // consumed, for every placement on the slot
            RaiseReadyChanged(unit.Slot);
            ad.Show(_ => RunOnMain(() => earned.TrySetResult(true)));

            // The ad is on screen: the caller's token no longer applies (see class remarks).
            AdShowResult closed = await AwaitCloseAsync(tcs);
            AdShowResult result = closed == AdShowResult.Failed
                ? closed
                : await RewardGrace.ResolveAsync(closed, earned.Task, LateRewardGrace);

            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Rewarded interstitial {unit.Id} result: {result}.");
            ad.Destroy();
            if (!disposed) EnsureLoading(unit.Slot);
            return result;
        }

        private async ValueTask<AdShowResult> ShowAppOpenAsync(Unit unit, CancellationToken cancellationToken)
        {
            if (!unit.Active || !IsAppOpenReady(unit.Slot))
            {
                Debug.LogWarning($"{LogPrefix} App open {unit.Id} not shown: not ready (ad not loaded or expired).");
                return AdShowResult.Failed;
            }
            if (cancellationToken.IsCancellationRequested) return AdShowResult.Failed;

            AppOpenAd ad = unit.Slot.AppOpen;
            var tcs = new TaskCompletionSource<AdShowResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            ad.OnAdFullScreenContentClosed += () => RunOnMain(() => tcs.TrySetResult(AdShowResult.Completed));
            ad.OnAdFullScreenContentFailed += _ => RunOnMain(() => tcs.TrySetResult(AdShowResult.Failed));
            ad.OnAdPaid += value => RunOnMain(() => RaisePaid(unit, value, SafeAdSource(ad.GetResponseInfo)));
            ad.OnAdClicked += () => RunOnMain(() => RaiseClicked(unit.Id));

            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Showing app open {unit.Id}.");
            unit.Slot.AppOpen = null; // consumed, for every placement on the slot
            RaiseReadyChanged(unit.Slot);
            ad.Show();

            // The ad is on screen: the caller's token no longer applies (see class remarks).
            AdShowResult result = await AwaitCloseAsync(tcs);

            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} App open {unit.Id} result: {result}.");
            ad.Destroy();
            if (!disposed) EnsureLoading(unit.Slot);
            return result;
        }

        private async Task<AdShowResult> AwaitCloseAsync(TaskCompletionSource<AdShowResult> tcs)
        {
            pendingShows.Add(tcs);
            try { return await tcs.Task; }
            finally { pendingShows.Remove(tcs); }
        }

        // Creates the view on first show (hidden until it loads; the desired visibility is remembered), shows it and
        // returns Completed. Failed only when the SDK is not ready or the placement is not active (not allowed).
        private AdShowResult ShowBanner(Unit unit)
        {
            if (!initialized)
            {
                Debug.LogWarning($"{LogPrefix} Banner {unit.Id} not shown: SDK not initialized.");
                return AdShowResult.Failed;
            }
            if (!unit.Active)
            {
                if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Banner {unit.Id} not shown: placement not active.");
                return AdShowResult.Failed;
            }

            unit.BannerVisible = true;
            if (unit.Banner == null)
            {
                try
                {
                    CreateBanner(unit);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{LogPrefix} Banner {unit.Id} could not be created: {e.Message}");
                    ReleaseBanner(unit);
                    return AdShowResult.Failed;
                }
                if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Banner {unit.Id} created; it appears once loaded.");
                return AdShowResult.Completed;
            }

            if (unit.BannerLoaded) unit.Banner.Show();
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Showing banner {unit.Id}.");
            return AdShowResult.Completed;
        }

        private void CreateBanner(Unit unit)
        {
            AdPlacement p = unit.Placement;
            AdSize size = GetAdSize(p);
            BannerView banner;
            if (p.Position == BannerPosition.Custom && p.Size != BannerSize.Collapsible)
            {
                // GMA custom positions are dp from the top-left (of the safe area on iOS). Adaptive banners span the width.
                float scale = MobileAds.Utils.GetDeviceScale();
                float widthDp = size.Width > 0 ? size.Width : Screen.safeArea.width / (scale > 0f ? scale : 1f);
                float heightDp = size.Height > 0 ? size.Height : EstimateBannerHeightPixels(size) / (scale > 0f ? scale : 1f);
                Vector2 at = BannerLayout.CustomTopLeftDp(p.CustomCenter, new Vector2(widthDp, heightDp), scale);
                banner = new BannerView(unit.AdUnitId, size, Mathf.RoundToInt(at.x), Mathf.RoundToInt(at.y));
            }
            else
            {
                if (p.Position == BannerPosition.Custom)
                    Debug.LogWarning($"{LogPrefix} Banner {unit.Id}: a collapsible banner cannot use a custom position - placed at the bottom.");
                banner = new BannerView(unit.AdUnitId, size, ToAdMobPosition(p.Position));
            }
            unit.Banner       = banner;
            unit.BannerLoaded = false;
            unit.LoadedHeightPixels = 0f;
            int generation    = unit.Generation;

            banner.OnBannerAdLoaded += () => RunOnMain(() =>
            {
                if (disposed || !ReferenceEquals(unit.Banner, banner)) return;
                unit.RetryAttempt = 0;
                bool firstLoad = !unit.BannerLoaded;
                unit.BannerLoaded = true;
                // Also fires on every auto-refresh: only honour the desired visibility, never force-hide.
                if (unit.BannerVisible) banner.Show(); else banner.Hide();
                if (firstLoad)
                {
                    RememberBannerHeight(unit);
                    if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Banner {unit.Id} loaded.");
                    RaiseReadyChanged(unit.Id);
                }
            });
            banner.OnBannerAdLoadFailed += error => RunOnMain(() =>
            {
                if (disposed || !ReferenceEquals(unit.Banner, banner) || generation != unit.Generation) return;
                string msg = error?.GetMessage() ?? "unknown error";
                PlacementLoadFailed?.Invoke(unit.Id, msg);
                // Reload into the same view; a failed view is still valid for LoadAd.
                ScheduleRetry(unit, "Banner", msg, () =>
                {
                    if (ReferenceEquals(unit.Banner, banner)) banner.LoadAd(GetBannerRequest(p));
                });
            });
            banner.OnAdPaid += value => RunOnMain(() => RaisePaid(unit, value, SafeAdSource(banner.GetResponseInfo)));
            banner.OnAdClicked += () => RunOnMain(() =>
            {
                unit.BannerClickedAt = Time.realtimeSinceStartup;
                RaiseClicked(unit.Id);
            });
            banner.OnAdFullScreenContentOpened += () => RunOnMain(() =>
            {
                if (disposed || !ReferenceEquals(unit.Banner, banner)) return;
                unit.BannerExpanded = true;
                // (0.9.0) A collapsible banner opening its expanded panel by itself is a bigger banner, not a fullscreen
                // ad: reporting it made the service refuse every interstitial while it stayed open. Fullscreen content
                // opened by a tap (in-app page, store) is still reported.
                bool byTap = Time.realtimeSinceStartup - unit.BannerClickedAt < BannerTapToOpenSeconds;
                unit.BannerExpansionReported = byTap || p.Size != BannerSize.Collapsible;
                if (unit.BannerExpansionReported) BannerExpandedChanged?.Invoke(unit.Id, true);
                else if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Banner {unit.Id} expanded its collapsible panel.");
            });
            banner.OnAdFullScreenContentClosed += () => RunOnMain(() =>
            {
                if (disposed || !ReferenceEquals(unit.Banner, banner) || !unit.BannerExpanded) return;
                bool reported = unit.BannerExpansionReported;
                unit.BannerExpanded = false;
                unit.BannerExpansionReported = false;
                if (reported) BannerExpandedChanged?.Invoke(unit.Id, false);
            });

            banner.Hide(); // created hidden; OnBannerAdLoaded applies the desired visibility
            banner.LoadAd(GetBannerRequest(p));
        }

        // Expected height of a banner before it loads. A fixed size carries its height in dp; an anchored adaptive size
        // has Height 0 until the SDK picks it, so Google's documented rule is applied: width * 50 / 320 dp, at most 90 dp
        // and 15 % of the screen height, at least 50 dp. The first real load replaces the estimate (RememberBannerHeight).
        private static float EstimateBannerHeightPixels(AdSize size)
        {
            float scale = MobileAds.Utils.GetDeviceScale();
            if (scale <= 0f) return 0f;
            if (size.Height > 0) return Mathf.Round(size.Height * scale);
            if (size.AdType != AdSize.Type.AnchoredAdaptive) return 0f;
            float widthDp  = size.Width > 0 ? size.Width : MobileAds.Utils.GetDeviceSafeWidth();
            return Mathf.Round(BannerLayout.AdaptiveBannerHeightDp(widthDp, Screen.height / scale) * scale);
        }

        // (0.9.0) Bar height in dp of a banner-like native: for a NativeBanner, an adaptive banner's height (Adaptive) or
        // the former smart banner's (Smart); otherwise NativeHeightDp.
        private static int NativeBarHeightDp(AdPlacement p)
        {
            if (p.Format != AdFormat.NativeBanner || p.NativeBannerHeight == NativeBannerHeight.Fixed) return p.EffectiveNativeHeightDp;
            float scale = MobileAds.Utils.GetDeviceScale();
            if (scale <= 0f) return p.EffectiveNativeHeightDp;
            float screenHeightDp = Screen.height / scale;
            return Mathf.RoundToInt(p.NativeBannerHeight == NativeBannerHeight.Smart
                ? BannerLayout.SmartBannerHeightDp(Screen.width / scale, screenHeightDp)
                : BannerLayout.AdaptiveBannerHeightDp(MobileAds.Utils.GetDeviceSafeWidth(), screenHeightDp));
        }

        // Per placement and screen size, so a rotation / resolution change never reuses a wrong height.
        private static string BannerHeightKey(string placementId) =>
            $"coregame.ads.admob.banner_height.{placementId}.{Screen.width}x{Screen.height}";

        private static void RememberBannerHeight(Unit unit)
        {
            try
            {
                float height = unit.Banner.GetHeightInPixels();
                if (height <= 0f) return;
                unit.LoadedHeightPixels = height;
                unit.ExpectedHeightPixels = height;
                // No PlayerPrefs.Save (a blocking disk write mid-session): Unity saves on pause / quit, and losing the value
                // in a crash only means the next launch estimates the height again.
                PlayerPrefs.SetFloat(BannerHeightKey(unit.Id), height);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Banner {unit.Id} height not remembered: {e.Message}");
            }
        }

        private const int SmallBannerHeightDp = 32;

        private static AdSize GetAdSize(AdPlacement p) => p.Format == AdFormat.Mrec ? AdSize.MediumRectangle : p.Size switch
        {
            BannerSize.MediumRectangle                    => AdSize.MediumRectangle,
            // A custom size: full width (FullWidth = -1, the safe area on iOS) at a fixed 32 dp.
            BannerSize.SmallFullWidth                     => new AdSize(AdSize.FullWidth, SmallBannerHeightDp),
            BannerSize.Adaptive or BannerSize.Collapsible =>
                AdSize.GetCurrentOrientationAnchoredAdaptiveBannerAdSizeWithWidth(AdSize.FullWidth),
            _                                             => AdSize.Banner,
        };

        private static AdRequest GetBannerRequest(AdPlacement p)
        {
            var request = new AdRequest();
            if (p.Size == BannerSize.Collapsible)
            {
                bool top = p.Position is BannerPosition.Top
                                      or BannerPosition.TopLeft
                                      or BannerPosition.TopRight;
                request.Extras.Add("collapsible", top ? "top" : "bottom");
            }
            return request;
        }

        private static AdPosition ToAdMobPosition(BannerPosition pos) => pos switch
        {
            BannerPosition.Top         => AdPosition.Top,
            BannerPosition.Bottom      => AdPosition.Bottom,
            BannerPosition.TopLeft     => AdPosition.TopLeft,
            BannerPosition.TopRight    => AdPosition.TopRight,
            BannerPosition.BottomLeft  => AdPosition.BottomLeft,
            BannerPosition.BottomRight => AdPosition.BottomRight,
            BannerPosition.Center      => AdPosition.Center,
            _                          => AdPosition.Bottom,
        };

        // ---------------------------------------------------------------- native ads (0.9.0)

        private AdMobNativeBridge NativeBridge => nativeBridge ??= new AdMobNativeBridge(RunOnMain, OnNativeEvent);

        // Banner-like native: loaded on activation only with PreloadBanner, or again once a show created it.
        private void PreloadNative(Unit unit)
        {
            if (unit.NativeStarted || unit.Placement.PreloadBanner) EnsureNativeLoading(unit);
        }

        private void EnsureNativeLoading(Unit unit)
        {
            if (disposed || !initialized || !unit.Active || unit.Loading || unit.RetryPending || unit.BannerLoaded) return;
            unit.NativeStarted = true;
            unit.Loading       = true;
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Native {unit.Id} loading.");
            NativeBridge.Load(unit.NativeKey, unit.AdUnitId);
        }

        private void LoadNative(Slot slot)
        {
            BeginLoad(slot);
            NativeBridge.Load(slot.NativeKey, slot.AdUnitId);
        }

        // Like ShowBanner: Completed once the show is requested; the ad appears when it has loaded.
        private AdShowResult ShowNativeBanner(Unit unit)
        {
            if (!initialized)
            {
                Debug.LogWarning($"{LogPrefix} Native {unit.Id} not shown: SDK not initialized.");
                return AdShowResult.Failed;
            }
            if (!unit.Active)
            {
                if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Native {unit.Id} not shown: placement not active.");
                return AdShowResult.Failed;
            }

            unit.BannerVisible = true;
            if (unit.NativeShowRequested) return AdShowResult.Completed; // already on screen (or on its way): same ad
            if (unit.BannerLoaded)
            {
                PresentNative(unit);
                return AdShowResult.Completed;
            }
            EnsureNativeLoading(unit); // no-op while a load or a retry is pending
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Native {unit.Id} requested; it appears once loaded.");
            return AdShowResult.Completed;
        }

        // Placement fields are read at show time.
        private void PresentNative(Unit unit)
        {
            AdPlacement p = unit.Placement;
            int heightDp  = NativeBarHeightDp(p);
            unit.NativeShowRequested = true;
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Showing native {unit.Id}.");
            if (p.Format == AdFormat.NativeCollapsible)
            {
                NativeBridge.ShowCollapsible(unit.NativeKey, heightDp, p.NativeStartExpanded,
                    NativeCollapseDelay(p),
                    AdMobNativeBridge.Argb(p.NativeBackgroundColor), AdMobNativeBridge.Argb(p.NativeTextColor),
                    AdMobNativeBridge.Argb(p.NativeCtaColor), AdMobNativeBridge.Argb(p.NativeCtaTextColor));
            }
            else
            {
                bool top = p.Position is BannerPosition.Top or BannerPosition.TopLeft or BannerPosition.TopRight;
                NativeBridge.ShowBanner(unit.NativeKey, heightDp, top,
                    AdMobNativeBridge.Argb(p.NativeBackgroundColor), AdMobNativeBridge.Argb(p.NativeTextColor),
                    AdMobNativeBridge.Argb(p.NativeCtaColor), AdMobNativeBridge.Argb(p.NativeCtaTextColor));
            }
        }

        // A shown native ad is spent: hiding it releases the ad, and a fresh one loads while the placement is active.
        private void HideNative(Unit unit)
        {
            if (nativeBridge == null || !unit.NativeStarted) return;
            nativeBridge.Hide(unit.NativeKey);
            if (!unit.NativeShowRequested) return; // an unshown loaded ad stays ready
            unit.NativeShowRequested = false;
            unit.NativeOnScreen      = false;
            unit.NativeExpanded      = false;
            unit.BannerLoaded        = false;
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Native {unit.Id} hidden; loading a fresh ad.");
            RaiseReadyChanged(unit.Id);
            EnsureNativeLoading(unit);
        }

        private void DestroyNative(Unit unit)
        {
            bool wasLoaded = unit.Active && unit.BannerLoaded;
            unit.ResetLoad(); // a pending retry or load belongs to the destroyed ad
            ReleaseNative(unit);
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Native {unit.Id} destroyed.");
            if (wasLoaded) RaiseReadyChanged(unit.Id);
        }

        private void ReleaseNative(Unit unit)
        {
            if (unit.NativeKey == null) return;
            if (unit.NativeStarted) nativeBridge?.Destroy(unit.NativeKey);
            unit.NativeStarted       = false;
            unit.NativeShowRequested = false;
            unit.NativeOnScreen      = false;
            unit.NativeExpanded      = false;
            unit.BannerLoaded        = false;
            unit.BannerVisible       = false;
        }

        // A native interstitial on screen is destroyed after it closes (ShowNativeInterstitialAsync).
        private void ReleaseNative(Slot slot)
        {
            if (slot.NativeKey == null) return;
            slot.NativeReady = false;
            if (slot.NativeShowing == null) nativeBridge?.Destroy(slot.NativeKey);
        }

        private float NativeHeightPixels(Unit unit)
        {
            float scale = MobileAds.Utils.GetDeviceScale(); // dp -> px (iOS: points -> px)
            return scale > 0f ? Mathf.Round(NativeBarHeightDp(unit.Placement) * scale) : 0f;
        }

        // Like ShowInterstitialAsync: closed = Completed, show failure = Failed, the token counts only before the show.
        private async ValueTask<AdShowResult> ShowNativeInterstitialAsync(Unit unit, CancellationToken cancellationToken)
        {
            Slot slot = unit.Slot;
            if (!unit.Active || !slot.NativeReady || slot.NativeShowing != null)
            {
                Debug.LogWarning($"{LogPrefix} Native interstitial {unit.Id} not shown: not ready (ad not loaded).");
                return AdShowResult.Failed;
            }
            if (cancellationToken.IsCancellationRequested) return AdShowResult.Failed;

            var tcs = new TaskCompletionSource<AdShowResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            slot.NativeShowTcs = tcs;
            slot.NativeShowing = unit;
            slot.NativeReady   = false; // consumed, for every placement on the slot
            RaiseReadyChanged(slot);

            AdPlacement p = unit.Placement;
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Showing native interstitial {unit.Id}.");
            NativeBridge.ShowInterstitial(slot.NativeKey, NativeCloseDelay(p),
                AdMobNativeBridge.Argb(p.NativeBackgroundColor), AdMobNativeBridge.Argb(p.NativeTextColor),
                AdMobNativeBridge.Argb(p.NativeCtaColor), AdMobNativeBridge.Argb(p.NativeCtaTextColor));

            // The ad is on screen: the caller's token no longer applies (see class remarks).
            AdShowResult result = await AwaitCloseAsync(tcs);

            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Native interstitial {unit.Id} result: {result}.");
            if (ReferenceEquals(slot.NativeShowTcs, tcs))
            {
                slot.NativeShowTcs = null;
                slot.NativeShowing = null;
            }
            if (!disposed)
            {
                if (slot.IsActive) EnsureLoading(slot);
                else nativeBridge?.Destroy(slot.NativeKey); // every placement deactivated during the show
            }
            return result;
        }

        // Main thread (the bridge marshals).
        private void OnNativeEvent(string key, string evt, AdMobNativeBridge.EventData data)
        {
            if (disposed) return;
            if (nativeUnits.TryGetValue(key, out Unit unit)) OnNativeUnitEvent(unit, evt, data);
            else if (nativeSlots.TryGetValue(key, out Slot slot)) OnNativeSlotEvent(slot, evt, data);
        }

        private void OnNativeUnitEvent(Unit unit, string evt, AdMobNativeBridge.EventData data)
        {
            switch (evt)
            {
                case AdMobNativeBridge.EvLoaded:
                    if (!unit.Loading)
                    {
                        // Not ours (destroyed / deactivated meanwhile): free it unless the key is in use again.
                        if (!unit.NativeStarted) nativeBridge?.Destroy(unit.NativeKey);
                        return;
                    }
                    unit.Loading      = false;
                    unit.RetryAttempt = 0;
                    if (unit.BannerLoaded) return; // the native side re-reported the unshown ad
                    unit.BannerLoaded = true;
                    if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Native {unit.Id} loaded ({data.adSource}).");
                    if (unit.BannerVisible) PresentNative(unit); // a show was waiting for it
                    RaiseReadyChanged(unit.Id);
                    break;

                case AdMobNativeBridge.EvFailed:
                {
                    if (!unit.Loading) return;
                    unit.Loading = false;
                    string msg = string.IsNullOrEmpty(data.error) ? "unknown error" : data.error;
                    PlacementLoadFailed?.Invoke(unit.Id, msg);
                    ScheduleRetry(unit, "Native", msg, () => EnsureNativeLoading(unit));
                    break;
                }

                case AdMobNativeBridge.EvShown:
                    // Ignored when hidden before the view came up (the hide already removed it).
                    if (unit.NativeShowRequested && unit.BannerVisible) unit.NativeOnScreen = true;
                    break;

                case AdMobNativeBridge.EvShowFailed:
                {
                    if (!unit.NativeShowRequested) return;
                    Debug.LogWarning($"{LogPrefix} Native {unit.Id} could not be shown: {data.error}");
                    // The intent is dropped (the game's next ShowBanner tries again), so a view that never comes up
                    // cannot turn load -> show -> fail into a request loop. A fresh ad is loaded for that next show.
                    unit.NativeShowRequested = false;
                    unit.NativeOnScreen      = false;
                    unit.BannerVisible       = false;
                    unit.BannerLoaded        = false;
                    RaiseReadyChanged(unit.Id);
                    EnsureNativeLoading(unit);
                    break;
                }

                case AdMobNativeBridge.EvExpanded:
                case AdMobNativeBridge.EvCollapsed:
                {
                    if (!unit.NativeShowRequested) return;
                    bool expanded = evt == AdMobNativeBridge.EvExpanded;
                    // Raised on changes only: a show that opens collapsed is not a player's collapse.
                    if (unit.NativeExpanded == expanded) return;
                    unit.NativeExpanded = expanded;
                    NativeExpandedChanged?.Invoke(unit.Id, expanded);
                    break;
                }

                case AdMobNativeBridge.EvClicked:
                    RaiseClicked(unit.Id);
                    break;

                case AdMobNativeBridge.EvPaid:
                    RaiseNativePaid(unit, data);
                    break;
            }
        }

        private void OnNativeSlotEvent(Slot slot, string evt, AdMobNativeBridge.EventData data)
        {
            switch (evt)
            {
                case AdMobNativeBridge.EvLoaded:
                    if (!slot.Loading || !slot.IsActive)
                    {
                        if (!slot.IsActive && slot.NativeShowing == null) nativeBridge?.Destroy(slot.NativeKey);
                        return;
                    }
                    slot.Loading      = false;
                    slot.RetryAttempt = 0;
                    if (slot.NativeReady) return;
                    slot.NativeReady = true;
                    if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Native interstitial {slot.Name} loaded ({data.adSource}).");
                    RaiseReadyChanged(slot);
                    break;

                case AdMobNativeBridge.EvFailed:
                {
                    if (!slot.Loading || !slot.IsActive) return;
                    slot.Loading = false;
                    string msg = string.IsNullOrEmpty(data.error) ? "unknown error" : data.error;
                    foreach (Unit member in slot.Members)
                        if (member.Active) PlacementLoadFailed?.Invoke(member.Id, msg);
                    ScheduleRetry(slot, "Native interstitial", msg, () => EnsureLoading(slot));
                    break;
                }

                case AdMobNativeBridge.EvShowFailed:
                    if (slot.NativeShowTcs == null) return;
                    Debug.LogWarning($"{LogPrefix} Native interstitial {slot.Name} could not be shown: {data.error}");
                    slot.NativeShowTcs.TrySetResult(AdShowResult.Failed);
                    break;

                case AdMobNativeBridge.EvClosed:
                    slot.NativeShowTcs?.TrySetResult(AdShowResult.Completed);
                    break;

                case AdMobNativeBridge.EvClicked:
                {
                    Unit owner = NativeSlotOwner(slot);
                    if (owner != null) RaiseClicked(owner.Id);
                    break;
                }

                case AdMobNativeBridge.EvPaid:
                {
                    Unit owner = NativeSlotOwner(slot);
                    if (owner != null) RaiseNativePaid(owner, data);
                    break;
                }
            }
        }

        // The placement whose show is on screen, else the first active one on the slot.
        private static Unit NativeSlotOwner(Slot slot)
        {
            if (slot.NativeShowing != null) return slot.NativeShowing;
            foreach (Unit member in slot.Members)
                if (member.Active) return member;
            return slot.Members.Count > 0 ? slot.Members[0] : null;
        }

        // Same AdImpression as RaisePaid; the native payload carries the AdValue fields.
        private void RaiseNativePaid(Unit unit, AdMobNativeBridge.EventData data)
        {
            if (disposed) return;
            ImpressionPaid?.Invoke(new AdImpression(
                unit.Id, ProviderId, unit.Format, unit.AdUnitId,
                string.IsNullOrEmpty(data.adSource) ? null : data.adSource,
                data.valueMicros / 1_000_000.0,
                data.currency,
                NativePrecisionName(data.precision)));
        }

        // AdValue precision codes of the native SDKs (0 unknown, 1 estimated, 2 publisher provided, 3 precise).
        private static string NativePrecisionName(int precision) => precision switch
        {
            3 => "exact",
            1 => "estimated",
            2 => "publisher_defined",
            _ => "unknown",
        };
#endif

        /// <summary>Load/retry bookkeeping shared by a banner placement and a fullscreen slot.</summary>
        private abstract class LoadState
        {
            public bool Loading;
            public bool RetryPending;
            public int RetryAttempt;
            /// <summary>Bumped on deactivation/teardown; callbacks from an older generation are discarded.</summary>
            public int Generation;

            public abstract bool IsActive { get; }
            public abstract string Name { get; }

            public void ResetLoad()
            {
                Generation++;
                Loading      = false;
                RetryPending = false;
                RetryAttempt = 0;
            }
        }

        /// <summary>
        /// The one fullscreen ad of a (format, ad unit id), shared by every placement routed to it. Loaded while at
        /// least one of those placements is active.
        /// </summary>
        private sealed class Slot : LoadState
        {
            /// <summary>App open ads expire 4 hours after load; a stale ad must be discarded and reloaded.</summary>
            public static readonly TimeSpan AppOpenExpiry = TimeSpan.FromHours(4);

            public readonly AdFormat Format;
            public readonly string AdUnitId;
            public readonly List<Unit> Members = new();

#if GAMELIB_ADMOB
            public InterstitialAd Interstitial;
            public RewardedAd Rewarded;
            public RewardedInterstitialAd RewardedInterstitial;
            public AppOpenAd AppOpen;
            public DateTime AppOpenLoadedAt;
#endif
            /// <summary>NativeInterstitial: bridge key (null for other formats).</summary>
            public string NativeKey;
            /// <summary>NativeInterstitial: an unshown native ad is loaded for the key.</summary>
            public bool NativeReady;
            /// <summary>NativeInterstitial: placement whose show is on screen (paid / click attribution), and its outcome.</summary>
            public Unit NativeShowing;
            public TaskCompletionSource<AdShowResult> NativeShowTcs;

            public Slot(AdFormat format, string adUnitId)
            {
                Format   = format;
                AdUnitId = adUnitId;
            }

            public override bool IsActive
            {
                get
                {
                    foreach (Unit unit in Members)
                        if (unit.Active) return true;
                    return false;
                }
            }

            public override string Name
            {
                get
                {
                    var ids = new List<string>(Members.Count);
                    foreach (Unit unit in Members) ids.Add(unit.Id);
                    return string.Join("+", ids);
                }
            }
        }

        /// <summary>State of one placement routed to AdMob (load state is used by banners only).</summary>
        private sealed class Unit : LoadState
        {
            public readonly AdPlacement Placement;
            public readonly string AdUnitId;

            /// <summary>True while the composite wants this placement loaded (banners: allowed).</summary>
            public bool Active;
            /// <summary>Shared fullscreen ad state; null for banners/MRECs.</summary>
            public Slot Slot;

#if GAMELIB_ADMOB
            public BannerView Banner;
#endif
            public bool BannerLoaded;
            public float ExpectedHeightPixels; // cached GetExpectedBannerHeightPixels (orientation is fixed per session)
            public float LoadedHeightPixels;   // measured height of the loaded view; 0 = not measured / no view
            // Desired visibility: Show() before the ad finished loading must still win.
            public bool BannerVisible;
            public bool BannerExpanded; // (0.9.0) the banner's fullscreen content (collapsible expansion, in-app page) is open
            public bool BannerExpansionReported; // (0.9.0) ... and it was reported through BannerExpandedChanged (opened by a tap)
            public float BannerClickedAt = float.NegativeInfinity; // realtimeSinceStartup of the last banner tap

            // Banner-like native (NativeBanner / NativeCollapsible) only; BannerLoaded = an unspent native ad is loaded.
            /// <summary>Bridge key; null for every other format.</summary>
            public string NativeKey;
            /// <summary>A native load was requested since activation (the banner's "view exists").</summary>
            public bool NativeStarted;
            /// <summary>The loaded ad was handed to a show call: it is spent once hidden.</summary>
            public bool NativeShowRequested;
            /// <summary>The native view reported <c>shown</c> and was not hidden since.</summary>
            public bool NativeOnScreen;
            /// <summary>NativeCollapsible: currently expanded.</summary>
            public bool NativeExpanded;

            public Unit(AdPlacement placement, string adUnitId)
            {
                Placement = placement;
                AdUnitId  = adUnitId;
            }

            public string Id => Placement.Id;
            public AdFormat Format => Placement.Format;
            public override bool IsActive => Active;
            public override string Name => Id;
        }
    }
}
