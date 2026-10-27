using System.Collections.Generic;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Optional capability of an adapter: segment targeting - the player is in segment <c>key</c> with these values, and
    /// the network's dashboard serves each segment its own waterfall / floors (AppLovin MAX segments). Set through
    /// <c>ads.SetAdSegment(key, values)</c>; the service passes the whole set on every change and before the SDK starts.
    /// </summary>
    public interface IAdSegments
    {
        /// <param name="segments">Every segment the game set: key -> values, keys and values 0..32000.</param>
        void ApplySegments(IReadOnlyDictionary<int, int[]> segments);
    }
}
