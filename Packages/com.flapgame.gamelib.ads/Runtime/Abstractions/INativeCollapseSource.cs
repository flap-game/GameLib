using System;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// (0.9.0) Optional capability of an adapter AND of <c>CompositeAdsService</c>: a
    /// <see cref="AdFormat.NativeCollapsible"/> ad was expanded or collapsed. Resolve it with
    /// <c>ads as INativeCollapseSource</c>.
    /// </summary>
    public interface INativeCollapseSource
    {
        /// <summary>
        /// Placement id, true = expanded (also when a show opens it expanded), false = collapsed. A collapse by the player
        /// with <see cref="AdPlacement.NativeHideOnCollapse"/> on: the composite hides the placement (as HideBanner) before it
        /// raises false.
        /// </summary>
        event Action<string, bool> NativeExpandedChanged;
    }
}
