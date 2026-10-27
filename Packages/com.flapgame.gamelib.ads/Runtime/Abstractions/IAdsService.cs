using System;
using System.Threading;
using System.Threading.Tasks;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Game-facing ads abstraction. Gameplay only says "show the ad at this placement"; which format,
    /// SDK and unit id fill it lives in <c>AdsModule.Placements</c>. Resolve from
    /// <c>GameLibBootstrap.Services</c>; never touch AdMob/MAX SDK types from gameplay. The runtime impl
    /// is usually <c>CompositeAdsService</c>, which walks each placement's routes in order.
    /// Prefer the helpers in <c>AdsServiceExtensions</c> (<c>ShowRewardedAsync</c>, <c>ShowInterstitialAsync</c>, ...).
    /// </summary>
    public interface IAdsService
    {
        /// <summary>Which provider backs this instance (None for the composite and the Null adapter).</summary>
        AdProviderType ProviderType { get; }

        /// <summary>True once the underlying SDK(s) finished initializing. Placements start loading after this.</summary>
        bool IsInitialized { get; }

        /// <summary>Raised with the placement id whenever an ad for it finishes loading or is consumed.</summary>
        event Action<string> PlacementReadyChanged;

        /// <summary>
        /// Raised once per <see cref="ShowAsync"/> of any placement: placement id and why it is not shown, or
        /// <see cref="AdBlockReason.None"/> when it goes to the ad SDK. For "ad requested" analytics.
        /// </summary>
        event Action<string, AdBlockReason> ShowRequested;

        /// <summary>An ad finished loading: placement id, provider id (<c>admob</c>, <c>max</c>...).</summary>
        event Action<string, string> PlacementLoaded;

        /// <summary>
        /// An ad failed to load: placement id, provider id, the SDK's error message. The adapter retries by itself; this
        /// is for "load failed" analytics, not something to act on.
        /// </summary>
        event Action<string, string, string> PlacementLoadFailed;

        /// <summary>Raised for every paid impression (impression-level revenue) from any provider. <c>AdsModule</c> forwards it to <c>IAnalytics.TrackAdRevenue</c>.</summary>
        event Action<AdImpression> ImpressionPaid;

        /// <summary>Initializes the underlying ad SDK(s). Called by the installer; idempotent.</summary>
        ValueTask InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>True when an ad for <paramref name="placementId"/> is loaded and ready to show.</summary>
        bool IsPlacementReady(string placementId);

        /// <summary>
        /// True when <see cref="ShowAsync"/> would be attempted right now. <paramref name="reason"/> tells why not:
        /// not loaded, cooldown, session cap, no-ads owned, or an id that is not in the settings.
        /// </summary>
        bool CanShow(string placementId, out AdBlockReason reason);

        /// <summary>Shows the ad at <paramref name="placementId"/> and awaits the outcome (shown, skipped, failed, rewarded).</summary>
        ValueTask<AdShowResult> ShowAsync(string placementId, CancellationToken cancellationToken = default);

        /// <summary>Hides the banner at <paramref name="placementId"/> without destroying it.</summary>
        void HideBanner(string placementId);

        /// <summary>Destroys the banner at <paramref name="placementId"/>, releasing its native resources.</summary>
        void DestroyBanner(string placementId);
    }
}
