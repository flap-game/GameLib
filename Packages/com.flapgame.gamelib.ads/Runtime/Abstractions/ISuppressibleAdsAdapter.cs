namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Optional adapter capability: <c>CompositeAdsService</c> calls it when the no-ads product becomes owned
    /// (or stops being owned) so the adapter stops preloading placements the player can no longer see and
    /// releases what it already loaded. Adapters that do not implement it keep preloading (harmless, wasted requests).
    /// </summary>
    public interface ISuppressibleAdsAdapter
    {
        /// <summary>True = do not load or reload <paramref name="placementId"/>, destroy its banner and loaded ads; false = resume loading.</summary>
        void SetSuppressed(string placementId, bool suppressed);
    }
}
