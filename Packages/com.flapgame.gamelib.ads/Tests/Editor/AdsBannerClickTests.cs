using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FlapGame.GameLib.Ads.Tests
{
    /// <summary>0.6.3: banner height and ad clicks forwarded by the composite.</summary>
    public class AdsBannerClickTests
    {
        private FakeAdsAdapter adMob;
        private FakeAdsAdapter max;
        private AdsModule settings;

        [SetUp]
        public void SetUp()
        {
            adMob    = new FakeAdsAdapter(AdProviderIds.AdMob);
            max      = new FakeAdsAdapter(AdProviderIds.Max);
            settings = ScriptableObject.CreateInstance<AdsModule>();
            settings.Placements.Add(new AdPlacement
            {
                Id = "banner", Format = AdFormat.Banner,
                Routes = { new AdRoute("max", "m1", "mi1"), new AdRoute("admob", "a1", "ai1") },
            });
            settings.Placements.Add(new AdPlacement
            {
                Id = "inter", Format = AdFormat.Interstitial,
                Routes = { new AdRoute("admob", "a2", "ai2") },
            });
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        private CompositeAdsService Create()
        {
            var ads = new CompositeAdsService(new IAdsProviderAdapter[] { adMob, max }, settings);
            ads.InitializeAsync().AsTask().GetAwaiter().GetResult();
            return ads;
        }

        [Test]
        public void BannerHeight_ComesFromTheRouteWhoseBannerIsLoaded()
        {
            using CompositeAdsService ads = Create();
            adMob.BannerHeight = 147f;
            max.BannerHeight   = 168f;
            Assert.That(ads.GetBannerHeightPixels("banner"), Is.EqualTo(0f), "nothing loaded");

            adMob.MarkReady("banner");
            Assert.That(ads.GetBannerHeightPixels("banner"), Is.EqualTo(147f), "only the admob banner is loaded");

            max.MarkReady("banner");
            Assert.That(ads.GetBannerHeightPixels("banner"), Is.EqualTo(168f), "max is the first route");
        }

        [Test]
        public void BannerHeight_IsZero_ForFullscreenAndUnknownPlacements()
        {
            using CompositeAdsService ads = Create();
            adMob.BannerHeight = 147f;
            adMob.MarkReady("inter");
            Assert.That(ads.GetBannerHeightPixels("inter"), Is.EqualTo(0f));
            Assert.That(ads.GetBannerHeightPixels("nope"), Is.EqualTo(0f));
            Assert.That(ads.GetBannerHeightPixels(null), Is.EqualTo(0f));
        }

        [Test]
        public void Clicks_FromEveryAdapter_AreForwarded_UntilDisposed()
        {
            CompositeAdsService ads = Create();
            var clicked = new List<string>();
            ads.AdClicked += clicked.Add;

            adMob.Click("banner");
            max.Click("inter");
            Assert.That(clicked, Is.EqualTo(new[] { "banner", "inter" }));

            ads.Dispose();
            adMob.Click("banner");
            Assert.That(clicked.Count, Is.EqualTo(2), "unsubscribed on dispose");
        }
    }
}
