namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Optional capability of an adapter AND of <c>CompositeAdsService</c>: the real on-screen height of a loaded
    /// banner, so the game can lay its content out above it instead of guessing from the SDK's size formulas.
    /// Resolve it with <c>ads as IBannerMetrics</c>; adapters that do not implement it report 0 through the composite.
    /// </summary>
    public interface IBannerMetrics
    {
        /// <summary>Height in screen pixels of the banner at <paramref name="placementId"/> while it is loaded; 0 when not loaded or unknown.</summary>
        float GetBannerHeightPixels(string placementId);

        /// <summary>
        /// (0.6.4) Height in screen pixels the banner at <paramref name="placementId"/> takes once it loads - the loaded
        /// height when there is one, else the size it was requested at. 0 when the placement cannot show a banner
        /// (unknown, disabled, suppressed by no-ads, no active route) or the size is unknown. Lets the game reserve the
        /// space before the first fill, so the layout does not jump when the banner appears.
        /// </summary>
        float GetExpectedBannerHeightPixels(string placementId);
    }
}
