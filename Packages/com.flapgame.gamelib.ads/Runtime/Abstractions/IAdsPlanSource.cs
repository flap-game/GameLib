using System;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Where the ads plan comes from. The installer reads <see cref="TryGetCachedPlan"/> BEFORE any SDK initializes
    /// (it decides which SDKs start), then applies every <see cref="PlanChanged"/> live. Register your own in the
    /// service registry before the Ads module runs (lower Order); without one the placements in AdsModule are used as they are.
    /// </summary>
    public interface IAdsPlanSource
    {
        /// <summary>The plan known without network (disk cache, defaults). Must not block and must not throw.</summary>
        bool TryGetCachedPlan(out AdsPlan plan);

        /// <summary>Raised with the new plan (null = plan removed) when it changes during the session.</summary>
        event Action<AdsPlan> PlanChanged;
    }
}
