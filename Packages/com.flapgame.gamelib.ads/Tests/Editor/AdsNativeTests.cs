using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace FlapGame.GameLib.Ads.Tests
{
    /// <summary>0.9.0: AdMob native formats in the composite - pacing, banner path, collapse, overrides, Doctor.</summary>
    public class AdsNativeTests
    {
        private FakeAdsAdapter adMob;
        private AdsModule settings;
        private float clock;

        [SetUp]
        public void SetUp()
        {
            adMob = new FakeAdsAdapter(AdProviderIds.AdMob);
            clock = 0f;
            settings = ScriptableObject.CreateInstance<AdsModule>();
            settings.Placements.Add(new AdPlacement
            {
                Id = "inter", Format = AdFormat.Interstitial,
                Routes = { new AdRoute("admob", "a1", "ai1") },
            });
            settings.Placements.Add(new AdPlacement
            {
                Id = "ni", Format = AdFormat.NativeInterstitial,
                Routes = { new AdRoute("admob", "a2", "ai2") },
            });
            settings.Placements.Add(new AdPlacement
            {
                Id = "nb", Format = AdFormat.NativeBanner, Position = BannerPosition.Bottom,
                Routes = { new AdRoute("admob", "a3", "ai3") },
            });
            settings.Placements.Add(new AdPlacement
            {
                Id = "nc", Format = AdFormat.NativeCollapsible, Position = BannerPosition.Bottom,
                Routes = { new AdRoute("admob", "a4", "ai4") },
            });
            PlayerPrefs.DeleteKey(CompositeAdsService.LastInterstitialKey);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(settings);
            PlayerPrefs.DeleteKey(CompositeAdsService.LastInterstitialKey);
        }

        private CompositeAdsService Create(AdsPlan plan = null)
        {
            var ads = new CompositeAdsService(new IAdsProviderAdapter[] { adMob }, settings, plan, null, () => clock);
            ads.InitializeAsync().AsTask().GetAwaiter().GetResult();
            return ads;
        }

        private static T Await<T>(ValueTask<T> task) => task.AsTask().GetAwaiter().GetResult();

        private AdPlacement Find(string id) => settings.Placements.Find(p => p.Id == id);

        [Test]
        public void NativeInterstitial_SharesTheInterstitialPacing()
        {
            settings.Policy.InterstitialMinIntervalSeconds = 60f;
            settings.Policy.FirstInterstitialDelaySeconds = 10f;
            settings.Policy.InterstitialFromLevel = 3;
            using CompositeAdsService ads = Create();
            ads.SetPlayerLevel(5);
            adMob.MarkReady("inter");
            adMob.MarkReady("ni");

            Assert.That(ads.CanShow("ni", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.SessionStartGrace));

            clock = 20f;
            Assert.That(Await(ads.ShowAsync("ni")), Is.EqualTo(AdShowResult.Completed));
            Assert.That(adMob.Shown, Is.EqualTo(new[] { "ni" }));
            Assert.That(ads.CanShow("inter", out reason), Is.False, "one timer for both");
            Assert.That(reason, Is.EqualTo(AdBlockReason.GlobalInterval));
            Assert.That(ads.CanShow("ni", out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.GlobalInterval));

            clock = 100f;
            Assert.That(ads.CanShow("ni", out reason), Is.True, reason.ToString());
            ads.SetPlayerLevel(1);
            Assert.That(ads.CanShow("ni", out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.BelowLevel));
        }

        [Test]
        public void NativeBanner_ShowsAndHides_ThroughTheBannerApi()
        {
            using CompositeAdsService ads = Create();
            adMob.BannerHeight = 120f;
            adMob.MarkReady("nb");

            Assert.That(Await(ads.ShowBannerAsync("nb")), Is.EqualTo(AdShowResult.Completed));
            Assert.That(adMob.Shown, Is.EqualTo(new[] { "nb" }));
            Assert.That(ads.IsShowingFullscreen, Is.False, "a native bar is not fullscreen");
            Assert.That(ads.GetVisibleBannerHeightPixels("nb"), Is.EqualTo(120f));

            ads.HideBanner("nb");
            Assert.That(adMob.Hidden, Does.Contain("nb"));
            Assert.That(ads.GetVisibleBannerHeightPixels("nb"), Is.EqualTo(0f));
        }

        [Test]
        public void NativeCollapse_WithHideOnCollapse_HidesThenRaisesTheEvent()
        {
            using CompositeAdsService ads = Create();
            adMob.BannerHeight = 50f;
            adMob.MarkReady("nc");
            var events = new List<(string, bool)>();
            float heightAtEvent = -1f;
            ads.NativeExpandedChanged += (id, expanded) =>
            {
                events.Add((id, expanded));
                heightAtEvent = ads.GetVisibleBannerHeightPixels(id);
            };
            Assert.That(Await(ads.ShowBannerAsync("nc")), Is.EqualTo(AdShowResult.Completed));

            adMob.NativeExpand("nc", false);
            Assert.That(adMob.Hidden, Has.No.Member("nc"), "shown collapsed (NativeStartExpanded off): not the player's collapse");
            Assert.That(heightAtEvent, Is.EqualTo(50f));

            adMob.NativeExpand("nc", true);
            Assert.That(adMob.Hidden, Has.No.Member("nc"), "expanding never hides");

            adMob.NativeExpand("nc", false);
            Assert.That(events, Is.EqualTo(new[] { ("nc", false), ("nc", true), ("nc", false) }));
            Assert.That(adMob.Hidden, Does.Contain("nc"));
            Assert.That(heightAtEvent, Is.EqualTo(0f), "hidden before the event: no longer wanted");
        }

        [Test]
        public void NativeCollapse_WithoutHideOnCollapse_StaysVisible_AndUnsubscribesOnDispose()
        {
            Find("nc").NativeHideOnCollapse = false;
            CompositeAdsService ads = Create();
            adMob.BannerHeight = 50f;
            adMob.MarkReady("nc");
            var events = new List<(string, bool)>();
            ads.NativeExpandedChanged += (id, expanded) => events.Add((id, expanded));
            Await(ads.ShowBannerAsync("nc"));

            adMob.NativeExpand("nc", false);
            Assert.That(events, Is.EqualTo(new[] { ("nc", false) }));
            Assert.That(adMob.Hidden, Has.No.Member("nc"));
            Assert.That(ads.GetVisibleBannerHeightPixels("nc"), Is.EqualTo(50f));

            ads.Dispose();
            adMob.NativeExpand("nc", true);
            Assert.That(events.Count, Is.EqualTo(1), "unsubscribed on dispose");
        }

        [Test]
        public void NativeDelays_FollowThePlan_ThenTheLegacyOverrides()
        {
            var plan = new AdsPlan
            {
                Placements = new List<AdsPlanPlacement>
                {
                    new() { Id = "ni", NativeCloseDelaySeconds = 2 },
                    new() { Id = "nc", NativeCollapseButtonDelaySeconds = -4 },
                },
            };
            using CompositeAdsService ads = Create(plan);
            Assert.That(ads.Placements["ni"].NativeCloseDelaySeconds, Is.EqualTo(2));
            Assert.That(ads.Placements["ni"].NativeCollapseButtonDelaySeconds, Is.EqualTo(1), "absent: local value");
            Assert.That(ads.Placements["nc"].NativeCollapseButtonDelaySeconds, Is.EqualTo(0), "clamped at 0");

            ads.ApplyOverrides(new List<AdPlacementOverride> { new() { Id = "ni", NativeCloseDelaySeconds = 8 } });
            Assert.That(ads.Placements["ni"].NativeCloseDelaySeconds, Is.EqualTo(8));
            ads.ApplyOverrides(new List<AdPlacementOverride> { new() { Id = "ni", CooldownSeconds = 3f } });
            Assert.That(ads.Placements["ni"].NativeCloseDelaySeconds, Is.EqualTo(8), "a later row without the field keeps it");
            Assert.That(Find("ni").NativeCloseDelaySeconds, Is.EqualTo(5), "settings untouched");

            AdPlacementOverride row = AdPlacementOverride.FromPlacement(Find("nc"));
            Assert.That(row.NativeCloseDelaySeconds, Is.EqualTo(5));
            Assert.That(row.NativeCollapseButtonDelaySeconds, Is.EqualTo(1));
        }

        [Test]
        public void NativeDelays_ParseFromThePlanJson()
        {
            Assert.That(RemoteConfigAdsPlanSource.TryParse(
                "{\"Placements\":[{\"Id\":\"ni\",\"NativeCloseDelaySeconds\":7},{\"Id\":\"inter\",\"CooldownSeconds\":3}]}",
                out AdsPlan plan), Is.True);
            Assert.That(plan.Placements[0].NativeCloseDelaySeconds, Is.EqualTo(7));
            Assert.That(plan.Placements[0].NativeCollapseButtonDelaySeconds, Is.Null);
            Assert.That(plan.Placements[1].NativeCloseDelaySeconds, Is.Null, "old JSON without the field");
        }

        [Test]
        public void Validate_NativeOnMax_IsAnError_AndCollapsiblePositionIsIgnored()
        {
            var issues = new List<GameLibIssue>();
            settings.Validate(issues);
            Assert.That(issues, Has.None.Matches<GameLibIssue>(i => i.Message.Contains("no native ads") || i.Message.Contains("Position Top")),
                        "AdMob routes at the bottom: fine");

            Find("nb").Routes.Add(new AdRoute("max", "m3", "mi3"));
            Find("nc").Position = BannerPosition.Top;
            issues.Clear();
            settings.Validate(issues);
            Assert.That(issues, Has.Some.Matches<GameLibIssue>(i => i.Severity == GameLibIssueSeverity.Error
                                                                    && i.Message.Contains("'nb'") && i.Message.Contains("MAX has no native ads")));
            Assert.That(issues, Has.None.Matches<GameLibIssue>(i => i.Message.Contains("'nc'")),
                        "a collapsible ignores Position (always the bottom): no issue, the default Position is Top");
        }

        [Test]
        public void NullAdapter_ShowsNativeBars_LikeBanners()
        {
            var mock = new NullAdsAdapter(settings, 0f, serveAllPlacements: true, allowFakeAds: true);
            mock.InitializeAsync(null).AsTask().GetAwaiter().GetResult();
            Assert.That(Await(mock.ShowAsync("nb")), Is.EqualTo(AdShowResult.Completed), "a banner shows before it loaded");
            Assert.That(Await(mock.ShowAsync("nc")), Is.EqualTo(AdShowResult.Completed));
        }

        [TestCase(393f, 894f, 61f, TestName = "AdaptiveHeight_Phone_IsWidthTimes50Over320")]
        [TestCase(320f, 640f, 50f, TestName = "AdaptiveHeight_NarrowPhone_IsAtLeast50")]
        [TestCase(800f, 1280f, 90f, TestName = "AdaptiveHeight_Tablet_IsAtMost90")]
        [TestCase(800f, 360f, 54f, TestName = "AdaptiveHeight_Landscape_IsAtMost15PercentOfTheScreen")]
        public void AdaptiveBannerHeightDp_FollowsGooglesRule(float widthDp, float screenDp, float expected)
        {
            Assert.That(BannerLayout.AdaptiveBannerHeightDp(widthDp, screenDp), Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void NativeBanner_MatchesTheBannerHeight_ByDefault()
        {
            var p = new AdPlacement { Format = AdFormat.NativeBanner };
            Assert.That(p.NativeMatchBannerHeight, Is.True);
            Assert.That(p.EffectiveNativeHeightDp, Is.EqualTo(100), "the fixed height used when matching is turned off");
        }
    }
}
