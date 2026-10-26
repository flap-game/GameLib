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
    /// <item>the very first launch after install shows no app open ad;</item>
    /// <item>every later launch shows one as soon as it is loaded (within <see cref="ColdStartWaitSeconds"/>);</item>
    /// <item>every return from the background shows one;</item>
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

        /// <param name="ads">The ads service.</param>
        /// <param name="lifecycle">Source of returns from the background.</param>
        /// <param name="placementId">The app open placement id (format <see cref="AdFormat.AppOpen"/>).</param>
        public AppOpenLauncher(IAdsService ads, IAppLifecycle lifecycle, string placementId)
        {
            this.ads         = ads ?? throw new ArgumentNullException(nameof(ads));
            this.lifecycle   = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            this.placementId = placementId ?? throw new ArgumentNullException(nameof(placementId));
            lifecycle.Resumed += OnResumed;
        }

        /// <summary>How long a launch waits for the app open ad to load; after that the launch shows none.</summary>
        public float ColdStartWaitSeconds { get; set; } = 10f;

        /// <summary>Minimum time in the background before a return shows an ad. 0 = every return.</summary>
        public float MinBackgroundSeconds { get; set; }

        /// <summary>Ignore returns in the Editor, which pauses on every window switch (default true; tests turn it off).</summary>
        public bool IgnoreReturnsInEditor { get; set; } = true;

        /// <summary>No app open ad on the first launch after install (default true).</summary>
        public bool SkipFirstLaunch { get; set; } = true;

        /// <summary>The game's own condition (tutorial, ads switched off...). Null = always allowed.</summary>
        public Func<bool> Gate { get; set; }

        /// <summary>This launch's number since install (1 = first launch). 0 before <see cref="Start"/>.</summary>
        public int LaunchCount { get; private set; }

        /// <summary>True during the first launch after install.</summary>
        public bool IsFirstLaunch => LaunchCount == 1;

        /// <summary>Counts the launch and, unless it is the first one, shows the launch app open ad. Once per process.</summary>
        public void Start()
        {
            if (started || disposed) return;
            started = true;
            try
            {
                LaunchCount = PlayerPrefs.GetInt(LaunchCountKey, 0) + 1;
                PlayerPrefs.SetInt(LaunchCountKey, LaunchCount);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                LaunchCount = 2; // unknown: treat as a returning player
                Debug.LogWarning($"{LogPrefix} Launch count unavailable ({e.Message}).");
            }

            if (SkipFirstLaunch && IsFirstLaunch)
            {
                Debug.Log($"{LogPrefix} First launch after install: no app open ad.");
                return;
            }
            _ = ColdStartAsync();
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

            Debug.Log($"{LogPrefix} Resume: {info}.");
            if (info.FromExternalActivity || info.TimeAway.TotalSeconds < MinBackgroundSeconds) return;
            _ = TryShowAsync("return");
        }

        private async Task ColdStartAsync()
        {
            float deadline = Time.realtimeSinceStartup + ColdStartWaitSeconds;
            try
            {
                while (!ads.IsPlacementReady(placementId))
                {
                    if (Time.realtimeSinceStartup >= deadline)
                    {
                        Debug.Log($"{LogPrefix} Launch ad not loaded within {ColdStartWaitSeconds:0}s: none this launch.");
                        return;
                    }
                    if (lifecycle.IsPaused) return; // left during loading: the return shows one instead
                    await Task.Delay(ColdStartPollMilliseconds, lifetime.Token);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            await TryShowAsync("launch");
        }

        private async Task TryShowAsync(string trigger)
        {
            if (disposed || showing) return;
            if (Gate != null && !Gate())
            {
                Debug.Log($"{LogPrefix} Not shown on {trigger}: blocked by the game.");
                return;
            }
            if (!ads.CanShow(placementId, out AdBlockReason reason))
            {
                Debug.Log($"{LogPrefix} Not shown on {trigger}: {reason}.");
                return;
            }

            showing = true;
            try
            {
                AdShowResult result = await ads.ShowAsync(placementId, lifetime.Token);
                Debug.Log($"{LogPrefix} Shown on {trigger}: {result}.");
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                showing = false;
            }
        }
    }
}
