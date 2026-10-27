using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Frequency rules shared by every fullscreen placement (per-placement cooldown and session cap stay on
    /// <see cref="AdPlacement"/>). All values can be changed by the remote ads plan.
    /// </summary>
    [Serializable, Preserve]
    public sealed class AdsPolicy
    {
        [Tooltip("Minimum seconds between two interstitial / app open ads of ANY placement. 0 = no global limit.")]
        [Min(0f)] public float InterstitialMinIntervalSeconds;

        [Tooltip("No interstitial / app open ad during the first N seconds of a session. 0 = none.")]
        [Min(0f)] public float FirstInterstitialDelaySeconds;

        [Tooltip("InterstitialMinIntervalSeconds also counts from the last interstitial of the previous session (saved on the " +
                 "device), so restarting the app does not bring the next one sooner.")]
        public bool InterstitialIntervalAcrossSessions;

        [Tooltip("No interstitial while the player's level (the game reports it with IAdsService.SetPlayerLevel) is below this. " +
                 "0 = no level gate.")]
        [Min(0)] public int InterstitialFromLevel;

        [Tooltip("A completed rewarded ad restarts the interstitial interval (the player just watched an ad by choice).")]
        public bool RewardedResetsInterstitialTimer = true;

        [Tooltip("App open placements are blocked for this many seconds after any fullscreen ad closed (returning from an " +
                 "interstitial/rewarded or from an ad click resumes the app and would otherwise trigger an app open ad).")]
        [Min(0f)] public float AppOpenSuppressSecondsAfterFullscreen = 5f;

        [Tooltip("Minimum seconds between two app open ads (of any app open placement). 0 = no limit.")]
        [Min(0f)] public float AppOpenMinIntervalSeconds = 90f;

        [Tooltip("When false (default) app open ads ignore FirstInterstitialDelaySeconds and InterstitialMinIntervalSeconds: " +
                 "an app open ad shows at launch and on every return, limited only by AppOpenMinIntervalSeconds, and does " +
                 "not restart the interstitial interval. True: it follows both and also restarts the interval.")]
        public bool AppOpenFollowsInterstitialRules;

        public AdsPolicy Clone() => (AdsPolicy)MemberwiseClone();
    }
}
