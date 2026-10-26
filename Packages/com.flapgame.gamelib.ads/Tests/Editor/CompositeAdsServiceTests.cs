using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace FlapGame.GameLib.Ads.Tests
{
    public class CompositeAdsServiceTests
    {
        private FakeAdsAdapter adMob;
        private FakeAdsAdapter max;
        private AdsModule settings;
        private float clock;
        private bool noAds;

        [SetUp]
        public void SetUp()
        {
            adMob = new FakeAdsAdapter(AdProviderIds.AdMob);
            max   = new FakeAdsAdapter(AdProviderIds.Max);
            clock = 0f;
            noAds = false;
            settings = ScriptableObject.CreateInstance<AdsModule>();
            settings.Placements.Add(new AdPlacement
            {
                Id = "inter", Format = AdFormat.Interstitial,
                Routes = { new AdRoute(AdProviderType.Max, "m1"), new AdRoute(AdProviderType.AdMob, "a1") },
                CooldownSeconds = 30f, MaxShowsPerSession = 2, SuppressedByNoAds = true,
            });
            settings.Placements.Add(new AdPlacement
            {
                Id = "reward", Format = AdFormat.Rewarded,
                Routes = { new AdRoute(AdProviderType.AdMob, "a2") },
                SuppressedByNoAds = false,
            });
            settings.Placements.Add(new AdPlacement
            {
                Id = "banner", Format = AdFormat.Banner,
                Routes = { new AdRoute(AdProviderType.Max, "m3"), new AdRoute(AdProviderType.AdMob, "a3") },
            });
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(settings);

        private CompositeAdsService Create() =>
            new(new IAdsProviderAdapter[] { adMob, max }, settings, null, () => noAds, () => clock);

        private static T Await<T>(ValueTask<T> task) => task.AsTask().GetAwaiter().GetResult();

        [Test]
        public void Waterfall_FirstReadyRouteWins_InRouteOrder()
        {
            using var ads = Create();
            adMob.MarkReady("inter");
            max.MarkReady("inter");

            Assert.That(Await(ads.ShowAsync("inter")), Is.EqualTo(AdShowResult.Completed));
            Assert.That(max.Shown, Is.EqualTo(new[] { "inter" }), "MAX is listed first in Routes");
            Assert.That(adMob.Shown, Is.Empty);
        }

        [Test]
        public void Waterfall_SkipsRoutesThatAreNotReady()
        {
            using var ads = Create();
            adMob.MarkReady("inter"); // MAX (first route) has nothing loaded

            Assert.That(Await(ads.ShowAsync("inter")), Is.EqualTo(AdShowResult.Completed));
            Assert.That(max.Shown, Is.Empty);
            Assert.That(adMob.Shown, Is.EqualTo(new[] { "inter" }));
        }

        [Test]
        public void Waterfall_FailedRoute_FallsThroughToNext()
        {
            using var ads = Create();
            max.MarkReady("inter");
            max.NextResult = AdShowResult.Failed;
            adMob.MarkReady("inter");

            Assert.That(Await(ads.ShowAsync("inter")), Is.EqualTo(AdShowResult.Completed));
            Assert.That(max.Shown.Count, Is.EqualTo(1));
            Assert.That(adMob.Shown.Count, Is.EqualTo(1));
        }

        [Test]
        public void Cooldown_BlocksUntilElapsed()
        {
            using var ads = Create();
            max.MarkReady("inter");

            Assert.That(Await(ads.ShowAsync("inter")), Is.EqualTo(AdShowResult.Completed));
            Assert.That(ads.CanShow("inter", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.Cooldown));
            Assert.That(Await(ads.ShowAsync("inter")), Is.EqualTo(AdShowResult.Failed));
            Assert.That(max.Shown.Count, Is.EqualTo(1), "blocked show must not reach the adapter");

            clock = 30f;
            Assert.That(ads.CanShow("inter", out reason), Is.True);
            Assert.That(reason, Is.EqualTo(AdBlockReason.None));
        }

        [Test]
        public void SessionCap_BlocksAfterMaxShows()
        {
            using var ads = Create();
            max.MarkReady("inter");

            Await(ads.ShowAsync("inter"));
            clock = 100f;
            Await(ads.ShowAsync("inter"));
            clock = 200f;

            Assert.That(ads.CanShow("inter", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.SessionCap));
            Assert.That(max.Shown.Count, Is.EqualTo(2));
        }

        [Test]
        public void NoAdsOwned_BlocksSuppressedPlacement_ButNotRewarded()
        {
            using var ads = Create();
            max.MarkReady("inter");
            adMob.MarkReady("reward");
            noAds = true;

            Assert.That(ads.CanShow("inter", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.NoAdsOwned));
            Assert.That(Await(ads.ShowAsync("inter")), Is.EqualTo(AdShowResult.Failed));

            Assert.That(ads.CanShow("reward", out reason), Is.True);
            Assert.That(Await(ads.ShowAsync("reward")), Is.EqualTo(AdShowResult.Completed));
        }

        [Test]
        public void CanShow_ReportsUnknownAndNotReady()
        {
            using var ads = Create();

            Assert.That(ads.CanShow("nope", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.UnknownPlacement));

            Assert.That(ads.CanShow("inter", out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.NotReady));
            Assert.That(ads.IsPlacementReady("inter"), Is.False);

            max.MarkReady("inter");
            Assert.That(ads.IsPlacementReady("inter"), Is.True);
        }

        [Test]
        public void PlacementReadyChanged_ForwardsFromProviders()
        {
            using var ads = Create();
            var seen = new List<string>();
            ads.PlacementReadyChanged += seen.Add;

            adMob.MarkReady("reward");
            max.MarkReady("inter");

            Assert.That(seen, Is.EqualTo(new[] { "reward", "inter" }));
        }

        [Test]
        public void ShowInterstitialAsync_Failed_StillInvokesOnCompleted()
        {
            using var ads = Create();
            int completed = 0;

            // Nothing loaded -> Failed, but the game-flow callback must still fire.
            AdShowResult result = Await(ads.ShowInterstitialAsync("inter", onCompleted: () => completed++));

            Assert.That(result, Is.EqualTo(AdShowResult.Failed));
            Assert.That(completed, Is.EqualTo(1));

            Await(ads.ShowAppOpenAsync("nope", onCompleted: () => completed++));
            Assert.That(completed, Is.EqualTo(2));
        }

        [Test]
        public void ShowRewardedAsync_OnlyRewardsOnCompleted()
        {
            using var ads = Create();
            adMob.MarkReady("reward");
            adMob.NextResult = AdShowResult.Skipped;
            int rewarded = 0;

            Assert.That(Await(ads.ShowRewardedAsync("reward", () => rewarded++)), Is.False);
            Assert.That(rewarded, Is.EqualTo(0));

            adMob.NextResult = AdShowResult.Completed;
            Assert.That(Await(ads.ShowRewardedAsync("reward", () => rewarded++)), Is.True);
            Assert.That(rewarded, Is.EqualTo(1));
        }

        [Test]
        public void Banner_ForwardsToFirstInstalledRoute_EvenBeforeLoad()
        {
            using var ads = Create();
            // Neither adapter has the banner loaded: desired-visibility semantics still hand it to MAX (first route).
            Await(ads.ShowBannerAsync("banner"));

            Assert.That(max.Shown, Is.EqualTo(new[] { "banner" }));
            Assert.That(adMob.Shown, Is.Empty);
        }

        [Test]
        public void ApplyOverrides_ChangesRulesAndRoutes_ForKnownIdsOnly()
        {
            using var ads = Create();
            max.MarkReady("inter");
            adMob.MarkReady("inter");

            ads.ApplyOverrides(new List<AdPlacementOverride>
            {
                new() { Id = "inter", CooldownSeconds = 0f, MaxShowsPerSession = 0, SuppressedByNoAds = false,
                        Routes = new List<AdRoute> { new AdRoute(AdProviderType.AdMob, "a1") } },
                new() { Id = "ghost", Routes = new List<AdRoute> { new AdRoute(AdProviderType.Max, "x") } },
            });

            noAds = true;
            Await(ads.ShowAsync("inter"));
            Await(ads.ShowAsync("inter"));
            Await(ads.ShowAsync("inter"));

            Assert.That(adMob.Shown.Count, Is.EqualTo(3), "override routes to AdMob only, no cooldown/cap/no-ads");
            Assert.That(max.Shown, Is.Empty);
            Assert.That(ads.Placements.ContainsKey("ghost"), Is.False);
        }

        [Test]
        public void NullAdsAdapter_ServesNoneRoutes_AndCompletes()
        {
            settings.Placements.Add(new AdPlacement { Id = "mock", Format = AdFormat.Interstitial, Routes = { new AdRoute(AdProviderType.None) } });
            var nullAdapter = new NullAdsAdapter(settings, fakeDelaySeconds: 0f, allowFakeAds: true);
            using var ads = new CompositeAdsService(new IAdsProviderAdapter[] { nullAdapter }, settings, null, null, () => clock);

            Assert.That(ads.IsPlacementReady("mock"), Is.False, "not before InitializeAsync");
            ads.InitializeAsync().AsTask().GetAwaiter().GetResult();

            Assert.That(ads.IsPlacementReady("mock"), Is.True);
            Assert.That(ads.IsPlacementReady("inter"), Is.False, "no None route -> not served");
            Assert.That(Await(ads.ShowAsync("mock")), Is.EqualTo(AdShowResult.Completed));
        }
    }
}
