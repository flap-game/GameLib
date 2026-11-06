using UnityEngine;

namespace FlapGame.GameLib.Ads
{
    /// <summary>Banner / MREC geometry shared by the adapters.</summary>
    public static class BannerLayout
    {
        /// <summary>
        /// (0.9.0) Height in dp of an anchored adaptive banner, by Google's documented rule: <paramref name="widthDp"/> x
        /// 50 / 320, at most 90 dp and 15 % of <paramref name="screenHeightDp"/>, at least 50 dp.
        /// </summary>
        public static float AdaptiveBannerHeightDp(float widthDp, float screenHeightDp) =>
            Mathf.Max(50f, Mathf.Min(Mathf.Round(widthDp * 50f / 320f), Mathf.Min(90f, screenHeightDp * 0.15f)));

        /// <summary>
        /// (0.9.0) Height in dp of AdMob's former smart banner: 90 dp on a tablet (shortest screen side >= 600 dp),
        /// otherwise 32 dp when the screen is at most 400 dp tall (a phone in landscape) and 50 dp above that.
        /// </summary>
        public static float SmartBannerHeightDp(float screenWidthDp, float screenHeightDp) =>
            Mathf.Min(screenWidthDp, screenHeightDp) >= 600f ? 90f : screenHeightDp <= 400f ? 32f : 50f;

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
