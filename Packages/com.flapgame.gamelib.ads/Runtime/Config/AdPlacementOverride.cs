using System;
using System.Collections.Generic;
using UnityEngine.Scripting;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// One row of the remote <c>ads_placements</c> JSON array. Every rule field is nullable: a field left out
    /// of the JSON keeps the local value, so <c>{"Id":"inter","CooldownSeconds":45}</c> changes only the cooldown.
    /// <para>
    /// What a remote row can NOT change: <c>Format</c>, banner size/position, the ad unit ids and the set of
    /// providers. Adapters preload the units they were handed at install time, so <see cref="Routes"/> can only
    /// reorder or drop providers the local placement already routes to; a route naming another provider is
    /// ignored, and any <c>AdUnitId</c> in it is replaced by the local unit id of that provider.
    /// </para>
    /// <para>
    /// JSON: field names as below; <c>Provider</c> accepts the enum name (<c>"Max"</c>, case-insensitive) or its
    /// number (<c>2</c>). Example: <c>[{"Id":"inter","Routes":[{"Provider":"AdMob"},{"Provider":"Max"}],"MaxShowsPerSession":5}]</c>.
    /// </para>
    /// </summary>
    [Serializable, Preserve]
    public sealed class AdPlacementOverride
    {
        public string Id;
        public List<AdRoute> Routes;
        public float? CooldownSeconds;
        public int? MaxShowsPerSession;
        public int? ShowEveryNthRequest;
        public bool? SuppressedByNoAds;

        [Preserve]
        public AdPlacementOverride() { }

        /// <summary>A row that sets every rule field from <paramref name="p"/> (the pre-0.5 "replace everything" semantics).</summary>
        public static AdPlacementOverride FromPlacement(AdPlacement p) => p == null ? null : new AdPlacementOverride
        {
            Id                 = p.Id,
            Routes             = p.Clone().Routes,
            CooldownSeconds    = p.CooldownSeconds,
            MaxShowsPerSession = p.MaxShowsPerSession,
            ShowEveryNthRequest = p.ShowEveryNthRequest,
            SuppressedByNoAds  = p.SuppressedByNoAds,
        };
    }
}
