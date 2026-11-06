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

        [Tooltip("Banner only, MAX only: an opaque color behind the banner (AppLovin asks for a non-transparent one for " +
                 "banners to be fully functional). Alpha 0 = not set.")]
        public Color BannerBackgroundColor = Color.clear;

        [Header("Native only (AdMob)")]
        [Tooltip("NativeBanner / NativeCollapsible: bar height in dp (the collapsed bar for NativeCollapsible). 0 = default " +
                 "(NativeBanner 100, NativeCollapsible 50).")]
        [Min(0)] public int NativeHeightDp;

        [Tooltip("NativeInterstitial: seconds before the close button appears (a 'Close in Ns' countdown shows meanwhile). " +
                 "0 = the close button shows at once. Remote config can override it.")]
        [Min(0)] public int NativeCloseDelaySeconds = 5;

        [Tooltip("NativeCollapsible: seconds after the ad opens expanded before its collapse button appears. Remote config " +
                 "can override it.")]
        [Min(0)] public int NativeCollapseButtonDelaySeconds = 1;

        [Tooltip("NativeCollapsible: each show opens the ad expanded (media visible). Off = it shows as the collapsed bar.")]
        public bool NativeStartExpanded = true;

        [Tooltip("NativeCollapsible: hide the ad when the player collapses it (the game shows it again with ShowBanner). " +
                 "Off = the collapsed bar stays on screen. INativeCollapseSource.NativeCollapsed fires either way.")]
        public bool NativeHideOnCollapse = true;

        [Tooltip("Native: background of the ad view.")]
        public Color NativeBackgroundColor = Color.white;

        [Tooltip("Native: headline / body text color.")]
        public Color NativeTextColor = new(0.067f, 0.067f, 0.067f, 1f);

        [Tooltip("Native: call-to-action button color.")]
        public Color NativeCtaColor = new(0.039f, 0.494f, 0.988f, 1f);

        [Tooltip("Native: call-to-action button text color.")]
        public Color NativeCtaTextColor = Color.white;

        [Header("AppLovin MAX only")]
        [Tooltip("Extra parameters for this placement's MAX ad unit (MaxSdk.Set*ExtraParameter), set before it loads - only " +
                 "the ones AppLovin documents or support asks for.")]
        public List<AdKeyValue> MaxExtraParameters = new();

        public bool IsBannerLike => Format is AdFormat.Banner or AdFormat.Mrec or AdFormat.NativeBanner or AdFormat.NativeCollapsible;

        /// <summary>(0.9.0) NativeBanner, NativeCollapsible or NativeInterstitial.</summary>
        public bool IsNative => Format is AdFormat.NativeBanner or AdFormat.NativeCollapsible or AdFormat.NativeInterstitial;

        /// <summary>(0.9.0) <see cref="NativeHeightDp"/>, or the format's default when 0 (NativeBanner 100, NativeCollapsible 50).</summary>
        public int EffectiveNativeHeightDp => NativeHeightDp > 0 ? NativeHeightDp : Format == AdFormat.NativeCollapsible ? 50 : 100;

        public AdPlacement Clone()
        {
            var copy = (AdPlacement)MemberwiseClone();
            copy.Routes = new List<AdRoute>(Routes?.Count ?? 0);
            if (Routes != null)
                foreach (AdRoute r in Routes)
                    copy.Routes.Add(r?.Clone());
            copy.MaxExtraParameters = new List<AdKeyValue>(MaxExtraParameters?.Count ?? 0);
            if (MaxExtraParameters != null)
                foreach (AdKeyValue e in MaxExtraParameters)
                    copy.MaxExtraParameters.Add(e == null ? null : new AdKeyValue(e.Key, e.Value));
            return copy;
        }
    }
}
