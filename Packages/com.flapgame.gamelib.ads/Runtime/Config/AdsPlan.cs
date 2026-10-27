using System;
using System.Collections.Generic;
using UnityEngine.Scripting;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// The remote half of the ads configuration, delivered by an <see cref="IAdsPlanSource"/> (a remote config key such as
    /// <c>ads_plan</c>). Local <see cref="AdsModule"/> is the CATALOG - every provider and the unit ids each placement
    /// may use, per platform; the plan picks from it. Every field is optional: a missing field keeps the local value.
    /// <para>
    /// The plan in effect when ads initialize is the last one the device fetched (remote-config disk cache), so the
    /// provider set it names is used from the next app start; placement rules, route order and policy also apply live
    /// when a fresh plan arrives. Per-country plans and A/B variants are remote-config conditions / experiments on this key.
    /// </para>
    /// <code>
    /// {
    ///   "Name": "max_primary_v2",
    ///   "Providers": ["max", "admob"],
    ///   "LoadStrategy": "Primary",
    ///   "Placements": [
    ///     { "Id": "inter_win", "Routes": ["max", "admob"], "CooldownSeconds": 45 },
    ///     { "Id": "rewarded_coins", "Routes": ["admob", "max"] },
    ///     { "Id": "banner_main", "Enabled": false }
    ///   ],
    ///   "Policy": { "InterstitialMinIntervalSeconds": 60, "FirstInterstitialDelaySeconds": 30 }
    /// }
    /// </code>
    /// </summary>
    [Serializable, Preserve]
    public sealed class AdsPlan
    {
        /// <summary>Label for analytics (user property <c>ads_plan</c>, param on ad events) - name each A/B variant.</summary>
        public string Name;

        /// <summary>Providers allowed to initialize. Null = every enabled provider some placement routes to.</summary>
        public List<string> Providers;

        /// <summary>"Primary" or "Parallel" (<see cref="Models.AdLoadStrategy"/>). Null = local setting.</summary>
        public string LoadStrategy;

        public List<AdsPlanPlacement> Placements;

        public AdsPlanPolicy Policy;

        [Preserve]
        public AdsPlan() { }
    }

    /// <summary>One placement row of <see cref="AdsPlan"/>. Ids not in the local catalog are ignored.</summary>
    [Serializable, Preserve]
    public sealed class AdsPlanPlacement
    {
        public string Id;

        /// <summary>
        /// Provider ids in waterfall order. Only providers that have a LOCAL route (unit id) for this placement are used;
        /// unknown ones are skipped with a warning. Null/empty = local order.
        /// </summary>
        public List<string> Routes;

        public float? CooldownSeconds;
        public int?   MaxShowsPerSession;
        public int?   ShowEveryNthRequest;
        public bool?  PersistRequestCount;
        public float? WaitForFillSeconds;
        public bool?  SuppressedByNoAds;

        /// <summary>False = the placement never shows (<c>AdBlockReason.PlacementDisabled</c>) and nothing loads for it.</summary>
        public bool? Enabled;

        [Preserve]
        public AdsPlanPlacement() { }
    }

    /// <summary>Optional overrides of <see cref="AdsPolicy"/>.</summary>
    [Serializable, Preserve]
    public sealed class AdsPlanPolicy
    {
        public float? InterstitialMinIntervalSeconds;
        public float? FirstInterstitialDelaySeconds;
        public bool?  InterstitialIntervalAcrossSessions;
        public int?   InterstitialFromLevel;
        public bool?  RewardedResetsInterstitialTimer;
        public float? AppOpenSuppressSecondsAfterFullscreen;
        public float? AppOpenMinIntervalSeconds;
        public bool?  AppOpenFollowsInterstitialRules;

        [Preserve]
        public AdsPlanPolicy() { }
    }
}
