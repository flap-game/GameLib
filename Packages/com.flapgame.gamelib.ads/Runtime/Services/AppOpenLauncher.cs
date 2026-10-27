using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// The app open rule shared by every GameLib game, started by <see cref="AdsModule"/> when
    /// <see cref="AdsModule.AppOpenPlacementId"/> is set:
    /// <list type="bullet">
    /// <item>the very first launch after install shows no app open ad (<see cref="ShowAtLaunchFrom"/>);</item>
    /// <item>every later launch shows one as soon as it is loaded (within <see cref="ColdStartWaitSeconds"/>), or when the
    /// game asks for it with <see cref="ShowLaunchAdAsync"/> (<see cref="AutoShowAtLaunch"/> off);</item>
    /// <item>every return from the background shows one (<see cref="MinBackgroundSeconds"/>);</item>
    /// <item>two app open ads are at least <c>AdsPolicy.AppOpenMinIntervalSeconds</c> (90 s) apart - enforced by
    /// <see cref="IAdsService.CanShow"/>, which also keeps the no-ads, another-ad-on-screen and just-closed-an-ad rules.</item>
    /// </list>
    /// Leaving the app for an ad, a click on an ad, a store purchase or a link the game opened is not a "return": those
    /// come back through <see cref="IAppLifecycle.Resumed"/> with <see cref="AppResumeInfo.FromExternalActivity"/> set
    /// (the Ads and IAP modules mark their own; call <see cref="IAppLifecycle.MarkExternalActivity"/> before opening a link).
    /// Resolve it from the registry to add the game's own condition with <see cref="Gate"/>.
    /// </summary>
    public sealed class AppOpenLauncher : IDisposable
    {
        /// <summary>PlayerPrefs key counting app launches (survives updates, cleared on uninstall).</summary>
        public const string LaunchCountKey = "gamelib.ads.app_open.launch_count";

        private const string LogPrefix = "[Ads/AppOpen]";
        private const int ColdStartPollMilliseconds = 250;

        private readonly IAdsService ads;
        private readonly IAppLifecycle lifecycle;
        private readonly string placementId;
        private readonly CancellationTokenSource lifetime = new();

        private bool showing;
        private bool started;
        private bool disposed;
        private Task<bool> launchAd;

        /// <param name="ads">The ads service.</param>
        /// <param name="lifecycle">Source of returns from the background.</param>
        /// <param name="placementId">The app open placement id (format <see cref="AdFormat.AppOpen"/>).</param>
        public AppOpenLauncher(IAdsService ads, IAppLifecycle lifecycle, string placementId)
        {
            this.ads         = ads ?? throw new ArgumentNullException(nameof(ads));
            this.lifecycle   = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            this.placementId = placementId ?? throw new ArgumentNullException(nameof(placementId));
            if (ads is CompositeAdsService composite)
                AdsStarted = () => composite.IsInitialized || composite.HasActiveRoute(placementId);
            lifecycle.Resumed += OnResumed;
        }

        /// <summary>
        /// How long a launch waits for the app open ad to load, counted from <see cref="AdsStarted"/> (consent and SDK
        /// init come first and can take longer on a slow device); after that the launch shows none.
        /// </summary>
        public float ColdStartWaitSeconds { get; set; } = 10f;

        /// <summary>
        /// True once the ad SDKs started (consent answered, SDK initialized), which starts the
        /// <see cref="ColdStartWaitSeconds"/> deadline. Set from a <see cref="CompositeAdsService"/>; null = already started.
        /// </summary>
        public Func<bool> AdsStarted { get; set; }

        /// <summary>
        /// Latest point after the launch at which the launch ad may still show (ads starting and loading included), so it
        /// never pops up in the middle of play.
        /// </summary>
        public float ColdStartMaxSeconds { get; set; } = 30f;

        /// <summary>Minimum time in the background before a return shows an ad. 0 = every return.</summary>
        public float MinBackgroundSeconds { get; set; }

        /// <summary>Ignore returns in the Editor, which pauses on every window switch (default true; tests turn it off).</summary>
        public bool IgnoreReturnsInEditor { get; set; } = true;

        /// <summary>
        /// First launch (counted since install) that shows the launch ad: 1 = every launch, 2 = all but the first (default,
        /// the shared rule), 5 = from the 5th launch. Returns from the background are not affected.
        /// </summary>
        public int ShowAtLaunchFrom { get; set; } = 2;

        /// <summary>No app open ad on the first launch after install. Same as <see cref="ShowAtLaunchFrom"/> 2 / 1.</summary>
        [Obsolete("Use ShowAtLaunchFrom (2 = skip the first launch, 1 = every launch).")]
        public bool SkipFirstLaunch
        {
            get => ShowAtLaunchFrom > 1;
            set => ShowAtLaunchFrom = value ? 2 : 1;
        }

        /// <summary>
        /// True (default) = <see cref="Start"/> shows the launch ad as soon as it loads. False = the game shows it from its
        /// loading screen with <see cref="ShowLaunchAdAsync"/> and continues when that returns.
        /// </summary>
        public bool AutoShowAtLaunch { get; set; } = true;

        /// <summary>
        /// PlayerPrefs key of the launch counter, read by <see cref="Start"/>. A game moving to GameLib sets its old key so
        /// its players keep their count. Default <see cref="LaunchCountKey"/>.
        /// </summary>
        public string LaunchCountPrefsKey { get; set; } = LaunchCountKey;

        /// <summary>True when this launch is one that shows the launch ad (<see cref="ShowAtLaunchFrom"/>).</summary>
        public bool IsLaunchAdDue => started && LaunchCount >= ShowAtLaunchFrom;

        /// <summary>The game's own condition (tutorial, ads switched off...). Null = always allowed.</summary>
        public Func<bool> Gate { get; set; }

        /// <summary>This launch's number since install (1 = first launch). 0 before <see cref="Start"/>.</summary>
        public int LaunchCount { get; private set; }

        /// <summary>True during the first launch after install.</summary>
        public bool IsFirstLaunch => LaunchCount == 1;

        /// <summary>
        /// Counts the launch and, when it is due (<see cref="ShowAtLaunchFrom"/>) and <see cref="AutoShowAtLaunch"/> is on,
        /// shows the launch app open ad. Once per process.
        /// </summary>
        public void Start()
        {
            if (started || disposed) return;
            started = true;
            string key = string.IsNullOrEmpty(LaunchCountPrefsKey) ? LaunchCountKey : LaunchCountPrefsKey;
            try
            {
                LaunchCount = PlayerPrefs.GetInt(key, 0) + 1;
                PlayerPrefs.SetInt(key, LaunchCount);
                PlayerPrefs.Save(); // a crash before the next save must not count this launch again
            }
            catch (Exception e)
            {
                LaunchCount = Math.Max(2, ShowAtLaunchFrom); // unknown: treat as a returning player
                Debug.LogWarning($"{LogPrefix} Launch count unavailable ({e.Message}).");
            }

            if (!IsLaunchAdDue)
            {
                if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Launch {LaunchCount} (ads from launch {ShowAtLaunchFrom}): no app open ad.");
                return;
            }
            if (!AutoShowAtLaunch) return;
            launchAd = LaunchAdAsync();
        }

        /// <summary>
        /// Shows the launch ad from the game's loading screen (<see cref="AutoShowAtLaunch"/> off) and completes when it is
        /// closed - or at once when this launch shows none (not due, no-ads, blocked) or when it does not load within
        /// <see cref="ColdStartWaitSeconds"/> of the SDKs starting. True = an ad was on screen. One launch ad per launch:
        /// later calls (and calls while <see cref="AutoShowAtLaunch"/> already shows it) return that same ad's task.
        /// </summary>
        public Task<bool> ShowLaunchAdAsync()
        {
            if (!started) Start();
            if (launchAd != null) return launchAd;
            if (disposed || !IsLaunchAdDue) return Task.FromResult(false);
            return launchAd = LaunchAdAsync();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifecycle.Resumed -= OnResumed;
            lifetime.Cancel();
            lifetime.Dispose();
        }

        private void OnResumed(AppResumeInfo info)
        {
            if (disposed || !started || (IgnoreReturnsInEditor && Application.isEditor)) return;

            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Resume: {info}.");
            if (info.FromExternalActivity || info.TimeAway.TotalSeconds < MinBackgroundSeconds) return;
            _ = TryShowAsync("return");
        }

        private async Task<bool> LaunchAdAsync()
        {
            try
            {
                // The deadline covers the ad load only, not consent + SDK init before it.
                float latest = Time.realtimeSinceStartup + ColdStartMaxSeconds;
                while (AdsStarted != null && !AdsStarted())
                {
                    if (lifecycle.IsPaused) return false; // left during startup: the return shows one instead
                    if (Time.realtimeSinceStartup >= latest)
                    {
                        if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Ads not started {ColdStartMaxSeconds:0}s after launch - no launch app open ad.");
                        return false;
                    }
                    await Task.Delay(ColdStartPollMilliseconds, lifetime.Token);
                }
                float deadline = Math.Min(Time.realtimeSinceStartup + ColdStartWaitSeconds, latest);
                while (!ads.IsPlacementReady(placementId))
                {
                    if (Time.realtimeSinceStartup >= deadline)
                    {
                        if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Launch ad not loaded within {ColdStartWaitSeconds:0}s: none this launch.");
                        return false;
                    }
                    if (lifecycle.IsPaused) return false; // left during loading: the return shows one instead
                    await Task.Delay(ColdStartPollMilliseconds, lifetime.Token);
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            return await TryShowAsync("launch");
        }

        private async Task<bool> TryShowAsync(string trigger)
        {
            if (disposed || showing) return false;
            if (Gate != null && !Gate())
            {
                if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Not shown on {trigger}: blocked by the game.");
                return false;
            }
            if (!ads.CanShow(placementId, out AdBlockReason reason))
            {
                // An every-Nth placement counts ShowAsync requests only: make this one count (it shows nothing).
                if (reason == AdBlockReason.RequestCount) _ = ads.ShowAsync(placementId, lifetime.Token);
                if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Not shown on {trigger}: {reason}.");
                return false;
            }

            showing = true;
            try
            {
                AdShowResult result = await ads.ShowAsync(placementId, lifetime.Token);
                if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Shown on {trigger}: {result}.");
                return result != AdShowResult.Failed;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            finally
            {
                showing = false;
            }
        }
    }
}
