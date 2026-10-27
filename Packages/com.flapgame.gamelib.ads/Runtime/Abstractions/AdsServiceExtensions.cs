using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Ads
{
    public static class AdsServiceExtensions
    {
        /// <summary>Reports the player's level for <c>AdsPolicy.InterstitialFromLevel</c>. Call at start and on every level change.</summary>
        public static void SetPlayerLevel(this IAdsService ads, int level)
        {
            if (ads is CompositeAdsService composite) composite.PlayerLevel = level;
            else Debug.LogWarning($"[Ads] SetPlayerLevel needs the GameLib ads service, not {ads?.GetType().Name ?? "null"}.");
        }

        /// <summary>Adds a game rule for fullscreen ads (see <see cref="AdShowRule"/>).</summary>
        public static void AddRule(this IAdsService ads, AdShowRule rule)
        {
            if (ads is CompositeAdsService composite) composite.AddRule(rule);
            else Debug.LogWarning($"[Ads] AddRule needs the GameLib ads service, not {ads?.GetType().Name ?? "null"}.");
        }

        /// <summary>On-screen height in pixels of a shown banner / MREC (0 = none). See <c>CompositeAdsService.BannerHeightChanged</c>.</summary>
        public static float GetVisibleBannerHeightPixels(this IAdsService ads, string placementId) =>
            ads is CompositeAdsService composite ? composite.GetVisibleBannerHeightPixels(placementId) : 0f;

        /// <summary>
        /// Shows the ad, waiting up to <paramref name="waitForFillSeconds"/> for it to load when nothing else blocks the show
        /// (overrides <c>AdPlacement.WaitForFillSeconds</c> for this call). Other services show without waiting.
        /// </summary>
        public static ValueTask<AdShowResult> ShowAsync(
            this IAdsService ads,
            string placementId,
            float waitForFillSeconds,
            CancellationToken cancellationToken = default)
            => ads is CompositeAdsService composite
                ? composite.ShowAsync(placementId, waitForFillSeconds, cancellationToken)
                : ads.ShowAsync(placementId, cancellationToken);

        /// <summary>Removes a rule added with <see cref="AddRule"/>.</summary>
        public static bool RemoveRule(this IAdsService ads, AdShowRule rule) =>
            ads is CompositeAdsService composite && composite.RemoveRule(rule);

        /// <summary>Forgets intervals, cooldowns and request counts (see <c>CompositeAdsService.ResetPacing</c>).</summary>
        public static void ResetPacing(this IAdsService ads)
        {
            if (ads is CompositeAdsService composite) composite.ResetPacing();
            else Debug.LogWarning($"[Ads] ResetPacing needs the GameLib ads service, not {ads?.GetType().Name ?? "null"}.");
        }

        /// <summary>
        /// Opens the ad SDK's debug screen (AdMob Ad Inspector, MAX Mediation Debugger) for a QA menu: every mediated
        /// network, whether it is integrated, and which ad loaded from where. <paramref name="providerId"/> null = the first
        /// installed one that has a debugger. AdMob opens it on test devices only. False when nothing opened.
        /// </summary>
        public static Task<bool> OpenDebuggerAsync(this IAdsService ads, string providerId = null)
        {
            if (ads is CompositeAdsService composite) return composite.OpenDebuggerAsync(providerId);
            Debug.LogWarning($"[Ads] OpenDebuggerAsync needs the GameLib ads service, not {ads?.GetType().Name ?? "null"}.");
            return Task.FromResult(false);
        }

        /// <summary>
        /// Tells the ad SDKs the game's sound setting, so video ads start at that volume or muted (AdMob: volume and mute,
        /// MAX: mute only). Call at start and on every change - only when the game has its own sound setting: Google warns a
        /// low or muted app volume makes fewer video ads eligible. <paramref name="volume"/> 0..1.
        /// </summary>
        public static void SetAdAudio(this IAdsService ads, float volume, bool muted)
        {
            if (ads is CompositeAdsService composite) composite.SetAdAudio(volume, muted);
            else Debug.LogWarning($"[Ads] SetAdAudio needs the GameLib ads service, not {ads?.GetType().Name ?? "null"}.");
        }

        /// <summary>True while an interstitial, rewarded or app open ad is on screen (false for another service).</summary>
        public static bool IsShowingFullscreen(this IAdsService ads) =>
            ads is CompositeAdsService composite && composite.IsShowingFullscreen;

        /// <summary>
        /// Shows a rewarded ad and invokes <paramref name="onRewarded"/> only when the user earns the reward.
        /// Returns true if the reward was granted.
        /// </summary>
        public static async ValueTask<bool> ShowRewardedAsync(
            this IAdsService ads,
            string placementId,
            Action onRewarded,
            CancellationToken cancellationToken = default)
        {
            AdShowResult result = await ads.ShowAsync(placementId, cancellationToken);
            if (result == AdShowResult.Completed)
                onRewarded?.Invoke();
            return result == AdShowResult.Completed;
        }

        /// <summary>
        /// Shows a rewarded ad using a drag-drop placement reference.
        /// </summary>
        public static ValueTask<bool> ShowRewardedAsync(
            this IAdsService ads,
            AdPlacementRef placement,
            Action onRewarded,
            CancellationToken cancellationToken = default)
            => ads.ShowRewardedAsync((string)placement, onRewarded, cancellationToken);

        /// <summary>
        /// Shows an interstitial ad and invokes <paramref name="onCompleted"/> when the user dismisses it
        /// or when it could not be shown at all. The callback ALWAYS fires (Completed, Skipped or Failed) —
        /// use this for game-flow continuation, not for granting rewards.
        /// Returns the raw <see cref="AdShowResult"/>.
        /// </summary>
        public static async ValueTask<AdShowResult> ShowInterstitialAsync(
            this IAdsService ads,
            string placementId,
            Action onCompleted = null,
            CancellationToken cancellationToken = default)
        {
            AdShowResult result = await ads.ShowAsync(placementId, cancellationToken);
            // Game flow must continue whatever happened to the ad - including Failed (not loaded, blocked, no-ads).
            onCompleted?.Invoke();
            return result;
        }

        /// <summary>
        /// Shows an interstitial ad using a drag-drop placement reference.
        /// </summary>
        public static ValueTask<AdShowResult> ShowInterstitialAsync(
            this IAdsService ads,
            AdPlacementRef placement,
            Action onCompleted = null,
            CancellationToken cancellationToken = default)
            => ads.ShowInterstitialAsync((string)placement, onCompleted, cancellationToken);

        /// <summary>
        /// Shows an app open ad and invokes <paramref name="onCompleted"/> when the user dismisses it.
        /// Typically called on app foreground/resume; the callback ALWAYS fires, also when the ad failed to
        /// show (not loaded, expired, blocked). Returns the raw <see cref="AdShowResult"/>.
        /// </summary>
        public static async ValueTask<AdShowResult> ShowAppOpenAsync(
            this IAdsService ads,
            string placementId,
            Action onCompleted = null,
            CancellationToken cancellationToken = default)
        {
            AdShowResult result = await ads.ShowAsync(placementId, cancellationToken);
            // Game flow must continue whatever happened to the ad - including Failed (not loaded, blocked, no-ads).
            onCompleted?.Invoke();
            return result;
        }

        /// <summary>
        /// Shows an app open ad using a drag-drop placement reference.
        /// </summary>
        public static ValueTask<AdShowResult> ShowAppOpenAsync(
            this IAdsService ads,
            AdPlacementRef placement,
            Action onCompleted = null,
            CancellationToken cancellationToken = default)
            => ads.ShowAppOpenAsync((string)placement, onCompleted, cancellationToken);

        /// <summary>
        /// Shows a banner ad for the given placement.
        /// <para>
        /// Unlike interstitial and rewarded, this returns as soon as the banner is visible —
        /// it does NOT await the banner's dismissal because banners persist on screen until
        /// explicitly hidden via <see cref="IAdsService.HideBanner"/> or removed via
        /// <see cref="IAdsService.DestroyBanner"/>. Completed = accepted: a banner requested before
        /// the ad SDKs are up is shown once its provider is ready. Failed = it cannot be shown.
        /// </para>
        /// </summary>
        public static ValueTask<AdShowResult> ShowBannerAsync(
            this IAdsService ads,
            string placementId,
            CancellationToken cancellationToken = default)
            => ads.ShowAsync(placementId, cancellationToken);

        /// <summary>
        /// Shows a banner ad using a drag-drop placement reference.
        /// See <see cref="ShowBannerAsync(IAdsService,string,CancellationToken)"/> for semantics.
        /// </summary>
        public static ValueTask<AdShowResult> ShowBannerAsync(
            this IAdsService ads,
            AdPlacementRef placement,
            CancellationToken cancellationToken = default)
            => ads.ShowBannerAsync((string)placement, cancellationToken);

        /// <summary>
        /// Shows any ad type using a drag-drop placement reference.
        /// </summary>
        public static ValueTask<AdShowResult> ShowAsync(
            this IAdsService ads,
            AdPlacementRef placement,
            CancellationToken cancellationToken = default)
            => ads.ShowAsync((string)placement, cancellationToken);

        public static bool IsPlacementReady(this IAdsService ads, AdPlacementRef placement)
            => ads.IsPlacementReady((string)placement);

        public static bool CanShow(this IAdsService ads, AdPlacementRef placement, out AdBlockReason reason)
            => ads.CanShow((string)placement, out reason);

        /// <summary>Shorthand when the reason does not matter.</summary>
        public static bool CanShow(this IAdsService ads, string placementId)
            => ads.CanShow(placementId, out _);

        public static void HideBanner(this IAdsService ads, AdPlacementRef placement)
            => ads.HideBanner((string)placement);

        public static void DestroyBanner(this IAdsService ads, AdPlacementRef placement)
            => ads.DestroyBanner((string)placement);
    }
}
