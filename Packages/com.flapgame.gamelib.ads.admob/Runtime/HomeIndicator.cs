using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// (0.9.0) iOS: while an ad bar sits on the screen's bottom edge, the home indicator auto-hides over it (it comes back
    /// on touch) and the bottom edge gesture is deferred, so a swipe over the bar does not leave the game. Each bar is an
    /// owner; the game's own <c>Device.hideHomeButton</c> / <c>deferSystemGesturesMode</c> come back when the last owner
    /// goes. Unity's root view controller answers prefersHomeIndicatorAutoHidden / preferredScreenEdgesDeferringSystemGestures
    /// from these two settings and calls setNeedsUpdate... when they change. A game that changes them itself while a bar
    /// is up gets its values overwritten when the last bar goes. Main thread only; no-op off iOS.
    /// </summary>
    internal static class HomeIndicator
    {
        private static readonly HashSet<string> owners = new HashSet<string>();
        private static bool overridden;
#if UNITY_IOS && !UNITY_EDITOR
        private static bool savedHideHomeButton;
        private static UnityEngine.iOS.SystemGestureDeferMode savedDeferMode;
#endif

        public static void Set(string owner, bool onBottomEdge)
        {
            if (onBottomEdge) owners.Add(owner);
            else owners.Remove(owner);
            Apply();
        }

        public static void ReleaseAll(string ownerPrefix)
        {
            owners.RemoveWhere(o => o.StartsWith(ownerPrefix, StringComparison.Ordinal));
            Apply();
        }

        private static void Apply()
        {
            bool want = owners.Count > 0;
            if (want == overridden) return;
            overridden = want;
#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                if (want)
                {
                    savedHideHomeButton = UnityEngine.iOS.Device.hideHomeButton;
                    savedDeferMode = UnityEngine.iOS.Device.deferSystemGesturesMode;
                    UnityEngine.iOS.Device.hideHomeButton = true;
                    UnityEngine.iOS.Device.deferSystemGesturesMode = savedDeferMode | UnityEngine.iOS.SystemGestureDeferMode.BottomEdge;
                }
                else
                {
                    UnityEngine.iOS.Device.hideHomeButton = savedHideHomeButton;
                    UnityEngine.iOS.Device.deferSystemGesturesMode = savedDeferMode;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Ads] Home indicator update failed: {e.Message}");
            }
#endif
        }
    }
}
