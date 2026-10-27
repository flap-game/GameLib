namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Optional capability of an adapter: server-side verification (SSV) of rewards. The ad network calls the game's server
    /// with the reward, signed, carrying this user id and custom data - AdMob's SSV callback, MAX's S2S reward callback. The
    /// service calls it right before each rewarded / rewarded interstitial show on the provider that shows it.
    /// </summary>
    public interface IRewardVerification
    {
        /// <param name="userId"><c>ads.SetRewardUserId</c>, or null.</param>
        /// <param name="customData">This show's custom data (<c>ads.ShowRewardedAsync(id, customData, ...)</c>), or null.</param>
        void SetRewardVerification(string userId, string customData);
    }
}
