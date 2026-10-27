using UnityEngine;

namespace FlapGame.GameLib.Ads
{
    /// <summary>Banner / MREC geometry shared by the adapters.</summary>
    public static class BannerLayout
    {
        /// <summary>
        /// Top-left corner, in dp from the top-left of the safe area, of an ad of <paramref name="sizeDp"/> centred at
        /// <paramref name="centerFraction"/> of the safe area (<see cref="BannerPosition.Custom"/>), kept inside it.
        /// </summary>
        public static Vector2 CustomTopLeftDp(Vector2 centerFraction, Vector2 sizeDp, float pixelsPerDp) =>
            CustomTopLeftDp(centerFraction, sizeDp, pixelsPerDp, Screen.safeArea.size);

        /// <inheritdoc cref="CustomTopLeftDp(Vector2, Vector2, float)"/>
        public static Vector2 CustomTopLeftDp(Vector2 centerFraction, Vector2 sizeDp, float pixelsPerDp, Vector2 safeAreaPixels)
        {
            float density = pixelsPerDp > 0f ? pixelsPerDp : 1f;
            float width   = safeAreaPixels.x / density;
            float height  = safeAreaPixels.y / density;
            float x = Mathf.Clamp01(centerFraction.x) * width  - sizeDp.x / 2f;
            float y = Mathf.Clamp01(centerFraction.y) * height - sizeDp.y / 2f;
            return new Vector2(Mathf.Clamp(x, 0f, Mathf.Max(0f, width - sizeDp.x)),
                               Mathf.Clamp(y, 0f, Mathf.Max(0f, height - sizeDp.y)));
        }
    }
}
