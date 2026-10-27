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

        [Tooltip("Shows only on every Nth request (ShowAsync - CanShow alone does not count) of this placement; a request " +
                 "blocked by another rule still counts, so the next one shows. 0 or 1 = every request. Ignored for banners.")]
        [Min(0)] public int ShowEveryNthRequest;

        [Tooltip("ShowEveryNthRequest keeps counting across sessions (saved on the device), so the 3rd request can be in " +
                 "the next session. Off = the count starts again every session.")]
        public bool PersistRequestCount;

        [Tooltip("ShowAsync waits up to this many seconds for the ad to load when it is the only thing missing (every " +
                 "rule allows the show). 0 = fail at once when nothing is loaded. Ignored for banners.")]
        [Min(0f)] public float WaitForFillSeconds;

        [Tooltip("Hidden when the player owns the no-ads product. Turn off for rewarded placements.")]
        public bool SuppressedByNoAds = true;

        [Header("Banner / Mrec only")]
        public BannerSize Size;
        public BannerPosition Position;

        [Tooltip("Position Custom only: centre of the ad as a fraction of the safe area, (0, 0) = top-left, (1, 1) = " +
                 "bottom-right. The ad is kept inside the safe area.")]
        public Vector2 CustomCenter = new(0.5f, 0.5f);

        [Tooltip("Creates the banner hidden as soon as its provider is ready, so the first ShowAsync displays a loaded ad " +
                 "instead of an empty slot. A hidden banner stops refreshing until shown. Not for collapsible banners.")]
        public bool PreloadBanner;

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
