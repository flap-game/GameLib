using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace FlapGame.GameLib.Ads.Tests
{
    /// <summary>0.8.2: request / load events, ResetPacing, the saved every-Nth count, the fullscreen pause.</summary>
    public class AdsPacingAndEventsTests
    {
        private FakeAdsAdapter max;
        private FakeAdsAdapter adMob;
        private AdsModule settings;
        private float clock;

        [SetUp]
        public void SetUp()
        {
            max = new FakeAdsAdapter(AdProviderIds.Max);
            adMob = new FakeAdsAdapter(AdProviderIds.AdMob);
            clock = 0f;
            settings = ScriptableObject.CreateInstance<AdsModule>();
            settings.Placements.Add(new AdPlacement
            {
                Id = "inter", Format = AdFormat.Interstitial,
                Routes = { new AdRoute("max", "m1", "mi1"), new AdRoute("admob", "a1", "ai1") },
            });
            settings.Placements.Add(new AdPlacement
            {
                Id = "banner", Format = AdFormat.Banner,
                Routes = { new AdRoute("max", "m2", "mi2") },
            });
            ClearPrefs();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(settings);
            ClearPrefs();
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        private static void ClearPrefs()
        {
            PlayerPrefs.DeleteKey(CompositeAdsService.LastInterstitialKey);
            PlayerPrefs.DeleteKey(CompositeAdsService.RequestCountKeyPrefix + "inter");
        }

        private CompositeAdsService Create() => new(new IAdsProviderAdapter[] { max, adMob }, settings, null, null, () => clock);

        private static T Await<T>(ValueTask<T> task) => task.AsTask().GetAwaiter().GetResult();

        private AdPlacement Inter => settings.Placements.Find(p => p.Id == "inter");

        [Test]
        public void ShowRequested_OncePerShow_WithTheReason()
        {
            using var ads = Create();
            var requests = new List<(string, AdBlockReason)>();
            ads.ShowRequested += (id, reason) => requests.Add((id, reason));

            Await(ads.ShowAsync("inter"));
            max.MarkReady("inter");
            Await(ads.ShowAsync("inter"));
            Await(ads.ShowAsync("missing"));

            Assert.That(requests, Is.EqualTo(new[]
            {
                ("inter", AdBlockReason.NotReady),
                ("inter", AdBlockReason.None),
                ("missing", AdBlockReason.UnknownPlacement),
            }));
        }

        [Test]
        public void LoadedAndLoadFailed_CarryTheProvider()
        {
            using var ads = Create();
            var loaded = new List<(string, string)>();
            var failed = new List<(string, string, string)>();
            ads.PlacementLoaded += (id, provider) => loaded.Add((id, provider));
            ads.PlacementLoadFailed += (id, provider, error) => failed.Add((id, provider, error));

            max.FailLoad("inter", "no fill");
            adMob.MarkReady("inter");
            adMob.MarkNotReady("inter"); // consumed: not a load

            Assert.That(failed, Is.EqualTo(new[] { ("inter", "max", "no fill") }));
            Assert.That(loaded, Is.EqualTo(new[] { ("inter", "admob") }));
        }

        [Test]
        public void ResetPacing_ClearsIntervalsCooldownsAndCounts()
        {
            settings.Policy.InterstitialMinIntervalSeconds = 60f;
            Inter.CooldownSeconds = 30f;
            Inter.MaxShowsPerSession = 1;
            using var ads = Create();
            max.MarkReady("inter");
            Assert.That(Await(ads.ShowAsync("inter")), Is.EqualTo(AdShowResult.Completed));
            Assert.That(ads.CanShow("inter", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.GlobalInterval));
            Assert.That(PlayerPrefs.HasKey(CompositeAdsService.LastInterstitialKey), Is.True);

            int changes = 0;
            ads.PlacementReadyChanged += id => { if (id == "inter") changes++; };
            ads.ResetPacing();

            Assert.That(ads.CanShow("inter", out _), Is.True);
            Assert.That(changes, Is.EqualTo(1), "UI bound to readiness hears it");
            Assert.That(PlayerPrefs.HasKey(CompositeAdsService.LastInterstitialKey), Is.False);
        }

        [Test]
        public void PersistRequestCount_CarriesTheCountIntoTheNextSession()
        {
            Inter.ShowEveryNthRequest = 3;
            Inter.PersistRequestCount = true;
            using (CompositeAdsService first = Create())
            {
                max.MarkReady("inter");
                Assert.That(Await(first.ShowAsync("inter")), Is.EqualTo(AdShowResult.Failed));
                Assert.That(Await(first.ShowAsync("inter")), Is.EqualTo(AdShowResult.Failed));
            }

            using (CompositeAdsService second = Create())
            {
                Assert.That(second.CanShow("inter", out _), Is.True, "the third request is in the next session");
                Assert.That(Await(second.ShowAsync("inter")), Is.EqualTo(AdShowResult.Completed));
                Assert.That(PlayerPrefs.HasKey(CompositeAdsService.RequestCountKeyPrefix + "inter"), Is.False, "a show starts the count again");
                Assert.That(Await(second.ShowAsync("inter")), Is.EqualTo(AdShowResult.Failed));
                second.ResetPacing();
                Assert.That(PlayerPrefs.HasKey(CompositeAdsService.RequestCountKeyPrefix + "inter"), Is.False);
            }

            Inter.PersistRequestCount = false;
            using CompositeAdsService plain = Create();
            Await(plain.ShowAsync("inter"));
            Assert.That(PlayerPrefs.HasKey(CompositeAdsService.RequestCountKeyPrefix + "inter"), Is.False, "off: nothing saved");
        }

        [Test]
        public void FullscreenPause_StopsTimeAndAudio_AndPutsThemBack()
        {
            using var ads = Create();
            max.MarkReady("inter");
            max.Pending = new TaskCompletionSource<AdShowResult>();
            Time.timeScale = 0.5f;
            using var pause = new FullscreenAdPause(ads, pauseTime: true, muteAudio: true);

            ValueTask<AdShowResult> show = ads.ShowAsync("inter");
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(AudioListener.pause, Is.True);

            max.Pending.SetResult(AdShowResult.Completed);
            Await(show);
            Assert.That(Time.timeScale, Is.EqualTo(0.5f), "the value from before the ad");
            Assert.That(AudioListener.pause, Is.False);
        }

        [Test]
        public void FullscreenPause_KeepsAPauseTheGameSetDuringTheAd()
        {
            using var ads = Create();
            max.MarkReady("inter");
            max.Pending = new TaskCompletionSource<AdShowResult>();
            using var pause = new FullscreenAdPause(ads, pauseTime: true, muteAudio: false);

            ValueTask<AdShowResult> show = ads.ShowAsync("inter");
            Time.timeScale = 0.25f; // the game's own slow motion / pause menu
            max.Pending.SetResult(AdShowResult.Completed);
            Await(show);
            Assert.That(Time.timeScale, Is.EqualTo(0.25f));
        }

        [Test]
        public void OpenDebugger_OpensTheInitializedProvider_AndAnnouncesIt()
        {
            using var ads = Create();
            var opening = new List<string>();
            ads.DebuggerOpening += opening.Add;

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("No ad debugger"));
            Assert.That(ads.OpenDebuggerAsync().GetAwaiter().GetResult(), Is.False, "nothing initialized yet");

            ads.InitializeAsync().AsTask().GetAwaiter().GetResult();
            Assert.That(ads.OpenDebuggerAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(max.DebuggerOpened, Is.EqualTo(1), "null = the first provider that has one");
            Assert.That(((IAdsService)ads).OpenDebuggerAsync("admob").GetAwaiter().GetResult(), Is.True);
            Assert.That(adMob.DebuggerOpened, Is.EqualTo(1));
            Assert.That(opening, Is.EqualTo(new[] { "max", "admob" }), "the installer marks the trip as game-initiated");

            adMob.DebuggerResult = false; // not a test device
            Assert.That(ads.OpenDebuggerAsync("admob").GetAwaiter().GetResult(), Is.False);
        }

        [Test]
        public void Validate_WarnsWhenMaxServesASmallBanner()
        {
            settings.Placements.Find(p => p.Id == "banner").Size = BannerSize.SmallFullWidth;
            var issues = new List<GameLibIssue>();
            settings.Validate(issues);
            Assert.That(issues, Has.Some.Matches<GameLibIssue>(i => i.Message.Contains("SmallFullWidth")));
        }
    }
}
