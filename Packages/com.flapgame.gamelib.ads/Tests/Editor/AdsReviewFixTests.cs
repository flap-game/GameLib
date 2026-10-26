using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FlapGame.GameLib.Ads.Tests
{
    /// <summary>Covers the 0.5.0 ads review fixes: override merge/JSON, release Null adapter, fullscreen guard, no-ads refresh, cancellation, late reward.</summary>
    public class AdsReviewFixTests
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
            clock = 100f;
            noAds = false;
            settings = ScriptableObject.CreateInstance<AdsModule>();
            settings.Policy.AppOpenSuppressSecondsAfterFullscreen = 5f;
            settings.Placements.Add(new AdPlacement
            {
                Id = "inter", Format = AdFormat.Interstitial,
                Routes = { new AdRoute(AdProviderType.Max, "m1"), new AdRoute(AdProviderType.AdMob, "a1") },
                CooldownSeconds = 30f, MaxShowsPerSession = 2, SuppressedByNoAds = true,
            });
            settings.Placements.Add(new AdPlacement
            {
                Id = "reward", Format = AdFormat.Rewarded,
                Routes = { new AdRoute(AdProviderType.AdMob, "a2") }, SuppressedByNoAds = false,
            });
            settings.Placements.Add(new AdPlacement
            {
                Id = "open", Format = AdFormat.AppOpen,
                Routes = { new AdRoute(AdProviderType.AdMob, "a4") }, SuppressedByNoAds = true,
            });
            settings.Placements.Add(new AdPlacement
            {
                Id = "banner", Format = AdFormat.Banner,
                Routes = { new AdRoute(AdProviderType.AdMob, "a3") }, SuppressedByNoAds = true,
            });
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(settings);

        private CompositeAdsService Create() =>
            new(new IAdsProviderAdapter[] { adMob, max }, settings, null, () => noAds, () => clock);

        // ---------- P1: partial override merges only present fields ----------

        [Test]
        public void ApplyOverrides_PartialRow_ChangesOnlyPresentFields()
        {
            using var ads = Create();
            ads.ApplyOverrides(new List<AdPlacementOverride> { new() { Id = "inter", CooldownSeconds = 45f } });

            AdPlacement p = ads.Placements["inter"];
            Assert.That(p.CooldownSeconds, Is.EqualTo(45f));
            Assert.That(p.MaxShowsPerSession, Is.EqualTo(2), "absent field keeps the local value");
            Assert.That(p.SuppressedByNoAds, Is.True);
            Assert.That(p.Routes, Has.Count.EqualTo(2));
            Assert.That(p.Routes[0].AdUnitId, Is.EqualTo("m1"));
        }

        [Test]
        public void ApplyOverrides_Routes_ReorderKeepsLocalUnitIds_AndIgnoresForeignProviders()
        {
            using var ads = Create();
            ads.ApplyOverrides(new List<AdPlacementOverride>
            {
                new() { Id = "inter", Routes = new List<AdRoute> { new AdRoute(AdProviderType.AdMob, "evil"), new AdRoute(AdProviderType.None), new AdRoute(AdProviderType.Max) } },
                new() { Id = "reward", Routes = new List<AdRoute> { new AdRoute(AdProviderType.Max, "x") } },
            });

            List<AdRoute> inter = ads.Placements["inter"].Routes;
            Assert.That(inter, Has.Count.EqualTo(2));
            Assert.That(inter[0].Provider, Is.EqualTo(AdProviderType.AdMob));
            Assert.That(inter[0].AdUnitId, Is.EqualTo("a1"), "remote cannot change unit ids");
            Assert.That(inter[1].Provider, Is.EqualTo(AdProviderType.Max));

            List<AdRoute> reward = ads.Placements["reward"].Routes;
            Assert.That(reward, Has.Count.EqualTo(1), "a row naming only foreign providers keeps the local routes");
            Assert.That(reward[0].Provider, Is.EqualTo(AdProviderType.AdMob));
        }

        // ---------- P0: Null adapter in release ----------

        [Test]
        public async Task NullAdapter_Release_RefusesPlacements_LogsError_AndFails()
        {
            settings.Placements.Add(new AdPlacement { Id = "mock", Format = AdFormat.Rewarded, Routes = { new AdRoute(AdProviderType.None) } });
            var nullAdapter = new NullAdsAdapter(settings, fakeDelaySeconds: 0f, allowFakeAds: false);
            using var ads = new CompositeAdsService(new IAdsProviderAdapter[] { nullAdapter }, settings, null, null, () => clock);

            LogAssert.Expect(LogType.Error, new Regex("Release build without an ad SDK"));
            await ads.InitializeAsync();

            Assert.That(ads.IsPlacementReady("mock"), Is.False);
            Assert.That(nullAdapter.CanShow("mock", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.NotReady));
            Assert.That(await ads.ShowAsync("mock"), Is.EqualTo(AdShowResult.Failed));
        }

        [Test]
        public async Task NullAdapter_EditorOrDevelopment_ServesAndCompletes()
        {
            settings.Placements.Add(new AdPlacement { Id = "mock", Format = AdFormat.Rewarded, Routes = { new AdRoute(AdProviderType.None) } });
            var nullAdapter = new NullAdsAdapter(settings, fakeDelaySeconds: 0f, allowFakeAds: true);
            using var ads = new CompositeAdsService(new IAdsProviderAdapter[] { nullAdapter }, settings, null, null, () => clock);

            await ads.InitializeAsync();

            Assert.That(ads.IsPlacementReady("mock"), Is.True);
            Assert.That(await ads.ShowAsync("mock"), Is.EqualTo(AdShowResult.Completed));
        }

        [Test]
        public void NullAdapter_DefaultFlag_IsTrueInEditor() => Assert.That(NullAdsAdapter.FakeAdsAllowedInThisBuild, Is.True);

        // ---------- P1: fullscreen guard ----------

        [Test]
        public async Task FullscreenGuard_SecondShowWhileShowing_IsBlocked()
        {
            using var ads = Create();
            adMob.MarkReady("reward");
            max.MarkReady("inter");
            adMob.Pending = new TaskCompletionSource<AdShowResult>();
            var changes = new List<bool>();
            ads.FullscreenShowingChanged += changes.Add;

            ValueTask<AdShowResult> first = ads.ShowAsync("reward");
            Assert.That(ads.IsShowingFullscreen, Is.True);
            Assert.That(ads.CanShow("inter", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.AnotherAdShowing));
            Assert.That(await ads.ShowAsync("inter"), Is.EqualTo(AdShowResult.Failed));
            Assert.That(max.Shown, Is.Empty);

            adMob.Pending.SetResult(AdShowResult.Completed);
            Assert.That(await first, Is.EqualTo(AdShowResult.Completed));
            Assert.That(ads.IsShowingFullscreen, Is.False);
            Assert.That(changes, Is.EqualTo(new[] { true, false }));
            Assert.That(ads.CanShow("inter", out _), Is.True);
        }

        [Test]
        public async Task AppOpen_IsSuppressedForAFewSecondsAfterAFullscreenAdClosed()
        {
            using var ads = Create();
            adMob.MarkReady("reward");
            adMob.MarkReady("open");

            Assert.That(ads.CanShow("open", out _), Is.True, "no fullscreen ad yet");
            await ads.ShowAsync("reward");

            clock += 2f;
            Assert.That(ads.CanShow("open", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.AfterFullscreenAd));

            clock += 4f;
            Assert.That(ads.CanShow("open", out _), Is.True);
        }

        // ---------- P1: cancellation ----------

        [Test]
        public async Task Cancellation_AfterFirstRouteFailed_DoesNotOpenSecondRoute()
        {
            using var ads = Create();
            max.MarkReady("inter");
            adMob.MarkReady("inter");
            max.Pending = new TaskCompletionSource<AdShowResult>();
            using var cts = new CancellationTokenSource();

            ValueTask<AdShowResult> show = ads.ShowAsync("inter", cts.Token);
            cts.Cancel();
            max.Pending.SetResult(AdShowResult.Failed);

            Assert.That(await show, Is.EqualTo(AdShowResult.Failed));
            Assert.That(adMob.Shown, Is.Empty, "a cancelled caller never gets a second ad");
            Assert.That(ads.IsShowingFullscreen, Is.False);
        }

        [Test]
        public async Task Cancellation_BeforeShow_ReturnsFailedWithoutShowing()
        {
            using var ads = Create();
            max.MarkReady("inter");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(await ads.ShowAsync("inter", cts.Token), Is.EqualTo(AdShowResult.Failed));
            Assert.That(max.Shown, Is.Empty);
        }

        // ---------- P2: null / empty ids ----------

        [Test]
        public async Task NullOrEmptyPlacementIds_AreRejectedWithoutThrowing()
        {
            using var ads = Create();
            foreach (string id in new[] { null, "" })
            {
                Assert.That(ads.IsPlacementReady(id), Is.False);
                Assert.That(ads.CanShow(id, out AdBlockReason reason), Is.False);
                Assert.That(reason, Is.EqualTo(AdBlockReason.UnknownPlacement));
                Assert.DoesNotThrow(() => ads.HideBanner(id));
                Assert.DoesNotThrow(() => ads.DestroyBanner(id));
                Assert.That(await ads.ShowAsync(id), Is.EqualTo(AdShowResult.Failed));
            }
            Assert.DoesNotThrow(() => ads.ApplyOverrides(new List<AdPlacementOverride> { null, new() { Id = null } }));

            var nullAdapter = new NullAdsAdapter(settings, 0f, serveAllPlacements: true, allowFakeAds: true);
            Assert.That(nullAdapter.IsPlacementReady(null), Is.False);
            Assert.That(nullAdapter.CanShow(null, out _), Is.False);
            Assert.DoesNotThrow(() => nullAdapter.DestroyBanner(null));
        }

        // ---------- P1: no-ads bought mid-session ----------

        [Test]
        public void RefreshNoAds_WhenOwned_DestroysSuppressedBanners_AndSuppressesPreload_Once()
        {
            using var ads = Create();
            noAds = true;
            ads.RefreshNoAds();
            ads.RefreshNoAds(); // no transition -> no second round

            Assert.That(adMob.Destroyed, Is.EqualTo(new[] { "banner" }));
            Assert.That(adMob.SuppressCalls, Does.Contain(("inter", true)));
            Assert.That(adMob.SuppressCalls, Does.Contain(("open", true)));
            Assert.That(adMob.SuppressCalls, Does.Contain(("banner", true)));
            Assert.That(adMob.SuppressCalls, Has.No.Member(("reward", true)), "rewarded is not suppressed by no-ads");
            Assert.That(adMob.SuppressCalls.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task ShowAsync_ReevaluatesNoAds_OnEveryShow()
        {
            using var ads = Create();
            max.MarkReady("inter");
            noAds = true;

            Assert.That(await ads.ShowAsync("inter"), Is.EqualTo(AdShowResult.Failed));
            Assert.That(max.Shown, Is.Empty);
            Assert.That(max.SuppressCalls, Does.Contain(("inter", true)));
        }

        // ---------- P0: late reward ----------

        [Test]
        public async Task RewardGrace_LateRewardWithinGrace_IsCompleted()
        {
            var earned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<AdShowResult> resolve = RewardGrace.ResolveAsync(AdShowResult.Skipped, earned.Task, TimeSpan.FromSeconds(2));
            earned.SetResult(true);
            Assert.That(await resolve, Is.EqualTo(AdShowResult.Completed));
        }

        [Test]
        public async Task RewardGrace_NoReward_IsSkipped_AndFailedStaysFailed()
        {
            var never = new TaskCompletionSource<bool>();
            Assert.That(await RewardGrace.ResolveAsync(AdShowResult.Skipped, never.Task, TimeSpan.FromMilliseconds(20)), Is.EqualTo(AdShowResult.Skipped));
            Assert.That(await RewardGrace.ResolveAsync(AdShowResult.Failed, Task.CompletedTask, TimeSpan.FromSeconds(1)), Is.EqualTo(AdShowResult.Failed));
            Assert.That(await RewardGrace.ResolveAsync(AdShowResult.Skipped, Task.CompletedTask, TimeSpan.Zero), Is.EqualTo(AdShowResult.Completed));
        }
    }
}
