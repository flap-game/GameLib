namespace FlapGame.GameLib.Ads
{
    /// <summary>How many routes of a placement keep an ad loaded at the same time.</summary>
    public enum AdLoadStrategy
    {
        /// <summary>
        /// Only the first route of each placement loads. A later route starts loading when every route before it
        /// failed to load (no fill / error), so one SDK serves while the next one is the fallback. Default.
        /// </summary>
        Primary  = 0,
        /// <summary>Every route loads at once (fastest fallback, costs extra ad requests and memory).</summary>
        Parallel = 1,
    }
}
