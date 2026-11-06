namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// (0.9.0) Optional capability of an adapter that shows native ads: receives the native timings of a placement after
    /// the plan / remote overrides are applied (the adapter's own <see cref="AdPlacement"/> is the local asset, which
    /// overrides never touch). Called for every native placement whenever the composite rebuilds its placements.
    /// </summary>
    public interface INativeTimingAdapter
    {
        /// <summary>Effective <see cref="AdPlacement.NativeCloseDelaySeconds"/> / <see cref="AdPlacement.NativeCollapseButtonDelaySeconds"/>.</summary>
        void SetNativeTiming(string placementId, int closeDelaySeconds, int collapseButtonDelaySeconds);
    }
}
