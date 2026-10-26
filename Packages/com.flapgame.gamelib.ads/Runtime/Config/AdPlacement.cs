using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// A named spot in the game where an ad shows. Gameplay only knows the <see cref="Id"/>;
    /// which SDK and unit id fill it is decided here (and may be overridden by remote config).
    /// </summary>
    [Serializable]
    public sealed class AdPlacement
    {
        public string Id;
        public AdFormat Format;

        [Tooltip("Waterfall: the first route whose adapter has an ad ready wins. Provider None = Null/mock adapter.")]
        public List<AdRoute> Routes = new();

        [Tooltip("Minimum seconds between two shows of this placement. 0 = none. Ignored for banners.")]
        [Min(0f)] public float CooldownSeconds;

        [Tooltip("Maximum shows per app session. 0 = unlimited. Ignored for banners.")]
        [Min(0)] public int MaxShowsPerSession;

        [Tooltip("Hidden when the player owns the no-ads product. Turn off for rewarded placements.")]
        public bool SuppressedByNoAds = true;

        [Header("Banner / Mrec only")]
        public BannerSize Size;
        public BannerPosition Position;

        public bool IsBannerLike => Format == AdFormat.Banner || Format == AdFormat.Mrec;

        public AdPlacement Clone()
        {
            var copy = (AdPlacement)MemberwiseClone();
            copy.Routes = new List<AdRoute>(Routes?.Count ?? 0);
            if (Routes != null)
                foreach (AdRoute r in Routes)
                    copy.Routes.Add(r?.Clone());
            return copy;
        }
    }
}
