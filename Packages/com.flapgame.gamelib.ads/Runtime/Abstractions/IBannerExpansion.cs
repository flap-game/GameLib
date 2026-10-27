using System;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Optional capability of an adapter: a banner / MREC opened fullscreen content (an expandable creative, or the in-app
    /// page after a tap) and closed it again. The service treats that time as a fullscreen ad on screen.
    /// </summary>
    public interface IBannerExpansion
    {
        /// <summary>Placement id, true = expanded, false = back to its slot.</summary>
        event Action<string, bool> BannerExpandedChanged;
    }
}
