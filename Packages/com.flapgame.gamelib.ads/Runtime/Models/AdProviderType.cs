namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Pre-0.6 provider enum. Routes now use provider id strings (<see cref="AdProviderIds"/>) so a new SDK needs no
    /// change here; this enum stays only for old serialized data and <c>IAdsService.ProviderType</c>.
    /// </summary>
    public enum AdProviderType
    {
        None  = 0,
        AdMob = 1,
        Max   = 2,
    }
}
