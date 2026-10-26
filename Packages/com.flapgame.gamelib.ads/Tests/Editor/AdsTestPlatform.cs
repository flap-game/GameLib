using NUnit.Framework;

namespace FlapGame.GameLib.Ads.Tests
{
    /// <summary>
    /// AdRoute.CurrentAdUnitId picks the iOS unit ids when the active build target is iOS (UNITY_IOS). Tests whose
    /// fixtures expect the Android ids are skipped there instead of failing.
    /// </summary>
    internal static class AdsTestPlatform
    {
        public static void RequireAndroidUnitIds()
        {
#if UNITY_IOS
            Assert.Ignore("Expects Android ad unit ids; the active build target is iOS.");
#endif
        }
    }
}
