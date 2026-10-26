using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace FlapGame.GameLib.Ads.Tests
{
    /// <summary>The shared app open rule: no ad on the first launch, one on every real return, none on the way back from an ad / purchase / link.</summary>
    public class AppOpenLauncherTests
    {
        private FakeAdsAdapter adapter;
        private AdsModule settings;
        private CompositeAdsService ads;
        private FakeLifecycle lifecycle;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(AppOpenLauncher.LaunchCountKey);
            adapter = new FakeAdsAdapter(AdProviderIds.Max);
            settings = ScriptableObject.CreateInstance<AdsModule>();
            settings.Policy.AppOpenMinIntervalSeconds = 0f;
            settings.Placements.Add(new AdPlacement
            {
                Id = "app_open", Format = AdFormat.AppOpen,
                Routes = { new AdRoute("max", "o1", "oi1") },
            });
            ads = new CompositeAdsService(new IAdsProviderAdapter[] { adapter }, settings);
            ads.InitializeAsync().AsTask().GetAwaiter().GetResult();
            adapter.MarkReady("app_open");
            lifecycle = new FakeLifecycle();
        }

        [TearDown]
        public void TearDown()
        {
            ads.Dispose();
            UnityEngine.Object.DestroyImmediate(settings);
            PlayerPrefs.DeleteKey(AppOpenLauncher.LaunchCountKey);
        }

        private AppOpenLauncher Launcher() =>
            new(ads, lifecycle, "app_open") { IgnoreReturnsInEditor = false, ColdStartWaitSeconds = 0f };

        [Test]
        public void FirstLaunch_ShowsNothing_ButAReturnDoes()
        {
            using AppOpenLauncher launcher = Launcher();
            launcher.Start();
            Assert.That(launcher.IsFirstLaunch, Is.True);
            Assert.That(adapter.Shown, Is.Empty);

            lifecycle.RaiseResumed(new AppResumeInfo(TimeSpan.FromSeconds(30), false, null));
            Assert.That(adapter.Shown, Is.EqualTo(new[] { "app_open" }), "the first-launch rule only covers the launch itself");
        }

        [Test]
        public void SecondLaunch_ShowsAtLaunch()
        {
            PlayerPrefs.SetInt(AppOpenLauncher.LaunchCountKey, 1);
            using AppOpenLauncher launcher = Launcher();
            launcher.Start();
            Assert.That(launcher.LaunchCount, Is.EqualTo(2));
            Assert.That(adapter.Shown, Is.EqualTo(new[] { "app_open" }));
        }

        [Test]
        public void ReturnFromExternalActivity_ShowsNothing()
        {
            using AppOpenLauncher launcher = Launcher();
            launcher.Start();
            lifecycle.RaiseResumed(new AppResumeInfo(TimeSpan.FromSeconds(30), true, "ad_click"));
            Assert.That(adapter.Shown, Is.Empty);
        }

        [Test]
        public void Gate_BlocksTheAd()
        {
            using AppOpenLauncher launcher = Launcher();
            launcher.Gate = () => false;
            launcher.Start();
            lifecycle.RaiseResumed(new AppResumeInfo(TimeSpan.FromSeconds(30), false, null));
            Assert.That(adapter.Shown, Is.Empty);
        }

        [Test]
        public async Task ColdStartWait_StartsWhenAdsStarted_NotAtLaunch()
        {
            PlayerPrefs.SetInt(AppOpenLauncher.LaunchCountKey, 1);
            var slow = new FakeAdsAdapter(AdProviderIds.Max) { InitGate = new TaskCompletionSource<bool>() };
            using var slowAds = new CompositeAdsService(new IAdsProviderAdapter[] { slow }, settings);
            using var launcher = new AppOpenLauncher(slowAds, lifecycle, "app_open") { IgnoreReturnsInEditor = false, ColdStartWaitSeconds = 0.2f };
            launcher.Start();
            ValueTask init = slowAds.InitializeAsync();

            await Task.Delay(500); // consent + SDK init outlast the cold-start wait
            Assert.That(slow.Shown, Is.Empty);

            slow.InitGate.SetResult(true);
            await init;
            slow.MarkReady("app_open");
            for (int i = 0; i < 100 && slow.Shown.Count == 0; i++) await Task.Delay(20);

            Assert.That(slow.Shown, Is.EqualTo(new[] { "app_open" }), "the wait counts from SDK init, so the launch ad still shows");
        }

        private sealed class FakeLifecycle : IAppLifecycle
        {
            public bool IsPaused => false;
            public bool HasFocus => true;
            public event Action<bool> PauseChanged { add { } remove { } }
            public event Action<bool> FocusChanged { add { } remove { } }
            public event Action<AppResumeInfo> Resumed;
            public event Action Quitting { add { } remove { } }
            public IDisposable BeginExternalActivity(string reason) => new AppResumeTracker().BeginExternalActivity(reason);
            public void MarkExternalActivity(string reason) { }
            public void RaiseResumed(AppResumeInfo info) => Resumed?.Invoke(info);
        }
    }
}
