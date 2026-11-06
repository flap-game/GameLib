namespace FlapGame.GameLib.Ads
{
    /// <summary>What kind of ad a placement shows. Decides which SDK API an adapter uses for it.</summary>
    public enum AdFormat
    {
        Interstitial = 0,
        Rewarded     = 1,
        AppOpen      = 2,
        Banner       = 3,
        Mrec         = 4,
        /// <summary>
        /// Rewarded ad shown at a natural break without the player asking (AdMob only; MAX has none). It follows the
        /// interstitial pacing rules, and Google requires an intro screen with an opt-out first: show it with
        /// <c>ads.ShowRewardedInterstitialAsync</c>.
        /// </summary>
        RewardedInterstitial = 5,
        /// <summary>
        /// (0.9.0) Native ad drawn as a bar at the top or bottom of the safe area (icon, headline, body, rating / store,
        /// CTA, "AD" badge), AdMob only. Banner-like: shown with ShowBanner, hidden with HideBanner, height through
        /// <see cref="IBannerMetrics"/>. Look: <see cref="AdPlacement"/> Native fields.
        /// </summary>
        NativeBanner = 6,
        /// <summary>
        /// (0.9.0) Native ad as a bottom bar that expands over the lower part of the screen with its media (image / video),
        /// AdMob only. The player collapses it with a button that appears after <see cref="AdPlacement.NativeCollapseButtonDelaySeconds"/>.
        /// Banner-like; its reported height is the collapsed bar. Collapse events: <see cref="INativeCollapseSource"/>.
        /// </summary>
        NativeCollapsible = 7,
        /// <summary>
        /// (0.9.0) Native ad shown full screen with a close button that appears after a countdown
        /// (<see cref="AdPlacement.NativeCloseDelaySeconds"/>), AdMob only. Follows the interstitial pacing rules; show it
        /// like an interstitial.
        /// </summary>
        NativeInterstitial = 8,
    }
}
