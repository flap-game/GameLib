namespace FlapGame.GameLib.Ads
{
    public enum BannerSize
    {
        Standard,        // fixed 320×50
        MediumRectangle, // fixed 300×250 (MREC)
        Adaptive,        // full-width anchored adaptive
        Collapsible,     // collapsible adaptive — AdMob only; falls back to Standard banner on MAX
        SmallFullWidth,  // full width, 32 dp high — AdMob only (custom size); falls back to Standard banner on MAX
    }
}
