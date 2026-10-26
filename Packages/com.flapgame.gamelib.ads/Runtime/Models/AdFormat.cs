namespace FlapGame.GameLib.Ads
{
    /// <summary>What kind of ad a placement shows. Decides which SDK API an adapter uses for it.</summary>
    public enum AdFormat
    {
        Interstitial = 0,
        Rewarded     = 1,
        AppOpen      = 2,
        Banner       = 3,
        Mrec         = 4,
    }
}
