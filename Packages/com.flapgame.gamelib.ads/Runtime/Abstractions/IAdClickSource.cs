using System;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Optional capability of an adapter AND of <c>CompositeAdsService</c>: raised with the placement id when the player
    /// clicks an ad (banner or fullscreen). A click usually opens a browser or the store - a game that shows App Open
    /// ads on resume uses it to skip the return from that click. Raised on the main thread.
    /// </summary>
    public interface IAdClickSource
    {
        /// <summary>Raised with the placement id of the clicked ad.</summary>
        event Action<string> AdClicked;
    }
}
