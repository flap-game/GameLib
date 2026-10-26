using NUnit.Framework;
using UnityEngine;

namespace FlapGame.GameLib.Ads.Tests
{
    /// <summary>0.6.4: no-ads reported by the game, banner height known before the first fill.</summary>
    public class AdsNoAdsBannerReserveTests
    {
        private FakeAdsAdapter adMob;
        private AdsModule settings;
        private bool noAds;

        [SetUp]
        public void SetUp()
        {
            noAds    = false;
            adMob    = new FakeAdsAdapter(AdProviderIds.AdMob);
            settings = ScriptableObject.CreateInstance<AdsModule>();
            settings.Placements.Add(new AdPlacement
            {
                Id = "banner", Format = AdFormat.Banner, SuppressedByNoAds = true,
                Routes = { new AdRoute("admob", "a1", "ai1") },
            });
            settings.Placements.Add(new AdPlacement
            {
                Id = "inter", Format = AdFormat.Interstitial, SuppressedByNoAds = true,
                Routes = { new AdRoute("admob", "a2", "ai2") },
            });
            settings.Placements.Add(new AdPlacement
            {
                Id = "rewarded", Format = AdFormat.Rewarded, SuppressedByNoAds = false,
                Routes = { new AdRoute("admob", "a3", "ai3") },
            });
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        private CompositeAdsService Create()
        {
            var ads = new CompositeAdsService(new IAdsProviderAdapter[] { adMob }, settings, noAdsOwned: () => noAds);
            ads.InitializeAsync().AsTask().GetAwaiter().GetResult();
            return ads;
        }

        [Test]
        public void NoAds_StopsSuppressedPlacements_AndDestroysTheBanner()
        {
            using CompositeAdsService ads = Create();
            adMob.MarkReady("inter");
            Assert.That(adMob.Active, Is.EquivalentTo(new[] { "banner", "inter", "rewarded" }));

            noAds = true;
            ads.RefreshNoAds();

            Assert.That(adMob.Active, Is.EquivalentTo(new[] { "rewarded" }), "rewarded keeps loading");
            Assert.That(adMob.Destroyed, Is.EqualTo(new[] { "banner" }));
            Assert.That(adMob.SuppressCalls, Does.Contain(("inter", true)));
            Assert.That(ads.CanShow("inter", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.NoAdsOwned));

            ads.RefreshNoAds();
            Assert.That(adMob.Destroyed.Count, Is.EqualTo(1), "unchanged value is a no-op");

            noAds = false;
            ads.RefreshNoAds();
            Assert.That(adMob.Active, Is.EquivalentTo(new[] { "banner", "inter", "rewarded" }), "loading resumes");
            Assert.That(ads.CanShow("inter", out reason), Is.True, reason.ToString());
        }

        [Test]
        public void ExpectedBannerHeight_BeforeFill_ThenLoadedHeight_AndZeroWithNoAds()
        {
            using CompositeAdsService ads = Create();
            adMob.ExpectedBannerHeight = 168f;
            adMob.BannerHeight         = 170f;

            Assert.That(ads.GetBannerHeightPixels("banner"), Is.EqualTo(0f), "not loaded yet");
            Assert.That(ads.GetExpectedBannerHeightPixels("banner"), Is.EqualTo(168f), "requested size before the fill");

            adMob.MarkReady("banner");
            Assert.That(ads.GetExpectedBannerHeightPixels("banner"), Is.EqualTo(170f), "the loaded height wins");

            noAds = true;
            ads.RefreshNoAds();
            Assert.That(ads.GetExpectedBannerHeightPixels("banner"), Is.EqualTo(0f), "no banner with no-ads");
            Assert.That(ads.GetExpectedBannerHeightPixels("inter"), Is.EqualTo(0f));
            Assert.That(ads.GetExpectedBannerHeightPixels(null), Is.EqualTo(0f));
        }
    }
}
