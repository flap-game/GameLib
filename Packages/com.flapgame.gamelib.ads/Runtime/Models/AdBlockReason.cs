namespace FlapGame.GameLib.Ads
{
    /// <summary>Why <c>IAdsService.CanShow</c> said no. <see cref="None"/> means the placement can be shown now.</summary>
    public enum AdBlockReason
    {
        None             = 0,
        NotReady         = 1,
        Cooldown         = 2,
        SessionCap       = 3,
        NoAdsOwned       = 4,
        UnknownPlacement = 5,
        /// <summary>Another fullscreen ad (interstitial / rewarded / app open) is on screen right now.</summary>
        AnotherAdShowing = 6,
        /// <summary>App open blocked: a fullscreen ad closed less than <c>AdsModule.AppOpenSuppressSecondsAfterFullscreen</c> ago.</summary>
        AfterFullscreenAd = 7,
        /// <summary>Interstitial / app open blocked by <c>AdsPolicy.InterstitialMinIntervalSeconds</c> (time since the last fullscreen ad of any placement).</summary>
        GlobalInterval = 8,
        /// <summary>Interstitial / app open blocked by <c>AdsPolicy.FirstInterstitialDelaySeconds</c> (too soon after the session started).</summary>
        SessionStartGrace = 9,
        /// <summary>The ads plan turned this placement off (<c>Enabled: false</c>).</summary>
        PlacementDisabled = 10,
        /// <summary>None of the placement's routes has an initialized provider (SDK missing, disabled by the plan, or failed to start).</summary>
        NoProvider = 11,
        /// <summary>App open blocked by <c>AdsPolicy.AppOpenMinIntervalSeconds</c> (time since the last app open ad).</summary>
        AppOpenInterval = 12,
    }
}
