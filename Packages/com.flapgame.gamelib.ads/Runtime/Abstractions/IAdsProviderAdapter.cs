using System;
using System.Threading;
using System.Threading.Tasks;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// One ad SDK behind GameLib. Implemented once per provider assembly (AdMob, MAX, LevelPlay, Null, or your own);
    /// created by that assembly's <see cref="IAdsProviderFactory"/>. Gameplay never sees adapters - it talks to
    /// <see cref="IAdsService"/>, whose composite owns every rule (routes, cooldowns, caps, no-ads, policy) and only
    /// asks adapters to load, report readiness and show.
    /// <para>Threading: every member is called on the main thread, and every event must be raised on the main thread.</para>
    /// <para>
    /// Lifecycle: <see cref="InitializeAsync"/> once (the composite never loads before it completes), then
    /// <see cref="SetPlacementActive"/> decides what the adapter keeps loaded. An adapter must NOT load a placement it
    /// was not told to activate - with the Primary strategy only the first route of a placement loads.
    /// </para>
    /// </summary>
    public interface IAdsProviderAdapter : IDisposable
    {
        /// <summary>Provider id (<see cref="AdProviderIds"/>), equal to its factory's id.</summary>
        string ProviderId { get; }

        /// <summary>True once the SDK finished initializing.</summary>
        bool IsInitialized { get; }

        /// <summary>Raised with the placement id when an ad finished loading, was consumed, or was released.</summary>
        event Action<string> PlacementReadyChanged;

        /// <summary>
        /// Raised with (placement id, error) when a load attempt failed (no fill, network, invalid unit). The composite
        /// uses it to start the next route under the Primary strategy. Keep retrying with backoff while the placement is active.
        /// </summary>
        event Action<string, string> PlacementLoadFailed;

        /// <summary>
        /// Raised for every paid impression (impression-level revenue). The installer forwards it to analytics.
        /// </summary>
        event Action<AdImpression> ImpressionPaid;

        /// <summary>
        /// Initializes the SDK with the placements routed to this provider (<see cref="AdsProviderContext.Units"/>) and the
        /// current consent. Called at most once per successful run; a faulted/cancelled run may be retried. Must not load ads.
        /// </summary>
        ValueTask InitializeAsync(AdsProviderContext context, CancellationToken cancellationToken = default);

        /// <summary>
        /// True = keep an ad loaded for <paramref name="placementId"/> (load now, reload after every show/failure with
        /// backoff). False = stop loading, release what is loaded and destroy its banner. Ids not in the context's units
        /// are ignored. Banners: true means "allowed"; the banner view is created by <see cref="ShowAsync"/>.
        /// </summary>
        void SetPlacementActive(string placementId, bool active);

        /// <summary>True when a fullscreen ad for <paramref name="placementId"/> is loaded and ready (banners: loaded).</summary>
        bool IsPlacementReady(string placementId);

        /// <summary>
        /// Shows the ad. Fullscreen: completes when it closed (Completed = reward earned, Skipped = closed without reward /
        /// interstitial closed, Failed = could not show). The token is honoured only before the ad is on screen.
        /// Banners: creates/loads the view if needed, shows it and returns Completed (Failed when it cannot be created).
        /// </summary>
        ValueTask<AdShowResult> ShowAsync(string placementId, CancellationToken cancellationToken = default);

        /// <summary>Hides the banner without destroying it.</summary>
        void HideBanner(string placementId);

        /// <summary>Destroys the banner view and releases its native resources.</summary>
        void DestroyBanner(string placementId);

        /// <summary>
        /// Applies the privacy state. Called before <see cref="InitializeAsync"/> (via the context) and again whenever it
        /// changes (privacy options form). SDKs that read the IAB TCF string written by the CMP may only need to log.
        /// </summary>
        void ApplyConsent(ConsentState consent);
    }
}
