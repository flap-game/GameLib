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
    }
}
