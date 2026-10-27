using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using FlapGame.GameLib.Ads.Editor;

namespace FlapGame.GameLib.Ads.Tests
{
    /// <summary>Scriptable <see cref="IAdsProviderAdapter"/> shared by the ads tests.</summary>
    internal sealed class FakeAdsAdapter : IAdsProviderAdapter, ISuppressibleAdsAdapter, IBannerMetrics, IAdClickSource
    {
        public readonly HashSet<string> Ready = new();
        public readonly HashSet<string> Active = new();
        public readonly List<(string id, bool active)> ActiveCalls = new();
        public readonly List<string> Shown = new();
        public readonly List<string> Hidden = new();
        public readonly List<string> Destroyed = new();
        public readonly List<(string id, bool suppressed)> SuppressCalls = new();
        public readonly List<ConsentState> Consents = new();
        public TaskCompletionSource<AdShowResult> Pending;
        public AdShowResult NextResult = AdShowResult.Completed;
        public TaskCompletionSource<bool> InitGate;
        public Exception InitError;
        public AdsProviderContext Context;
        public Action<string> OnInit;
        public bool Disposed;

        public FakeAdsAdapter(string providerId) => ProviderId = providerId;

        public string ProviderId { get; }
        public bool IsInitialized { get; private set; }
        public event Action<string> PlacementReadyChanged;
        public event Action<string, string> PlacementLoadFailed;
        public event Action<AdImpression> ImpressionPaid;

        public async ValueTask InitializeAsync(AdsProviderContext context, CancellationToken cancellationToken = default)
        {
            Context = context;
            OnInit?.Invoke(ProviderId);
            if (InitError != null) throw InitError;
            if (InitGate != null) await InitGate.Task;
            IsInitialized = true;
        }

        public void SetPlacementActive(string placementId, bool active)
        {
            ActiveCalls.Add((placementId, active));
            if (active) Active.Add(placementId);
            else Active.Remove(placementId);
        }

        public bool IsPlacementReady(string placementId) => placementId != null && Ready.Contains(placementId);

        public ValueTask<AdShowResult> ShowAsync(string placementId, CancellationToken cancellationToken = default)
        {
            Shown.Add(placementId);
            return Pending != null ? new ValueTask<AdShowResult>(Pending.Task) : new ValueTask<AdShowResult>(NextResult);
        }

        public void HideBanner(string placementId) => Hidden.Add(placementId);
        public void DestroyBanner(string placementId) => Destroyed.Add(placementId);
        public void ApplyConsent(ConsentState consent) => Consents.Add(consent);
        public void SetSuppressed(string placementId, bool suppressed) => SuppressCalls.Add((placementId, suppressed));
        public void Dispose() => Disposed = true;

        public void MarkReady(string id)
        {
            Ready.Add(id);
            PlacementReadyChanged?.Invoke(id);
        }

        public void MarkNotReady(string id)
        {
            Ready.Remove(id);
            PlacementReadyChanged?.Invoke(id);
        }

        public void FailLoad(string id, string error = "no fill") => PlacementLoadFailed?.Invoke(id, error);

        public void Pay(AdImpression impression) => ImpressionPaid?.Invoke(impression);

        // 0.6.3 capabilities
        public float BannerHeight;
        public event Action<string> AdClicked;
        public float GetBannerHeightPixels(string placementId) => Ready.Contains(placementId) ? BannerHeight : 0f;
        public void Click(string id) => AdClicked?.Invoke(id);

        // 0.6.4: requested size, reported while the placement is active
        public float ExpectedBannerHeight;
        public float GetExpectedBannerHeightPixels(string placementId) => Active.Contains(placementId) ? ExpectedBannerHeight : 0f;
    }

    /// <summary>Scriptable factory for catalog / selection / consent tests.</summary>
    internal sealed class FakeAdsFactory : IAdsProviderFactory
    {
        public readonly List<string> Log;
        public IConsentService Consent;

        public FakeAdsFactory(string id, int consentPriority = 0, IConsentService consent = null, List<string> log = null)
        {
            ProviderId      = id;
            ConsentPriority = consentPriority;
            Consent         = consent;
            Log             = log;
        }

        public string ProviderId { get; }
        public string DisplayName => ProviderId;
        public int ConsentPriority { get; }
        public FakeAdsAdapter Created;

        public IAdsProviderAdapter CreateAdapter()
        {
            Created = new FakeAdsAdapter(ProviderId);
            if (Log != null) Created.OnInit = id => Log.Add("init:" + id);
            return Created;
        }

        public IConsentService CreateConsentService(AdsModule settings) => Consent;
    }

    internal sealed class FakeConsent : IConsentService
    {
        public readonly List<string> Log;
        public readonly string Name;
        public ConsentStatus StatusValue = ConsentStatus.Obtained;
        public bool CanRequest = true;

        public FakeConsent(string name, List<string> log)
        {
            Name = name;
            Log  = log;
        }

        public ConsentStatus Status => StatusValue;
        public bool CanRequestAds => CanRequest;
        public bool IsPrivacyOptionsRequired => true;

        public ValueTask RequestAsync(CancellationToken cancellationToken = default)
        {
            Log?.Add("consent:" + Name);
            return default;
        }

        public ValueTask ShowPrivacyOptionsAsync(CancellationToken cancellationToken = default)
        {
            Log?.Add("privacy:" + Name);
            StatusValue = ConsentStatus.Required; // the user withdrew consent in the form
            return default;
        }
    }

    /// <summary>0.6 plugin model: load strategy, plan, policy, selection, consent, impressions.</summary>
    public class AdsPluginTests
    {
        private FakeAdsAdapter adMob;
        private FakeAdsAdapter max;
        private FakeAdsAdapter levelPlay;
        private AdsModule settings;
        private float clock;

        [SetUp]
        public void SetUp()
        {
            adMob     = new FakeAdsAdapter(AdProviderIds.AdMob);
            max       = new FakeAdsAdapter(AdProviderIds.Max);
            levelPlay = new FakeAdsAdapter(AdProviderIds.LevelPlay);
            clock = 0f;
            settings = ScriptableObject.CreateInstance<AdsModule>();
            settings.Placements.Add(new AdPlacement
            {
                Id = "inter", Format = AdFormat.Interstitial,
                Routes = { new AdRoute("max", "m1", "mi1"), new AdRoute("admob", "a1", "ai1"), new AdRoute("levelplay", "l1", "li1") },
            });
            settings.Placements.Add(new AdPlacement
            {
                Id = "inter2", Format = AdFormat.Interstitial,
                Routes = { new AdRoute("admob", "a5") },
            });
            settings.Placements.Add(new AdPlacement
            {
                Id = "reward", Format = AdFormat.Rewarded, SuppressedByNoAds = false,
                Routes = { new AdRoute("admob", "a2") },
            });
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(settings);
            PlayerPrefs.DeleteKey(CompositeAdsService.LastInterstitialKey);
        }

        private CompositeAdsService Create(AdsPlan plan = null) =>
            new(new IAdsProviderAdapter[] { adMob, max, levelPlay }, settings, plan, null, () => clock);

        private static void Init(CompositeAdsService ads) => ads.InitializeAsync().AsTask().GetAwaiter().GetResult();

        private static T Await<T>(ValueTask<T> task) => task.AsTask().GetAwaiter().GetResult();

        // ---------- load strategy ----------

        [Test]
        public void Primary_ActivatesOnlyTheFirstRoute_AfterInit()
        {
            AdsTestPlatform.RequireAndroidUnitIds();
            using var ads = Create();
            Assert.That(max.Active, Is.Empty, "nothing loads before the adapter initialized");
            Init(ads);

            Assert.That(max.Active, Does.Contain("inter"));
            Assert.That(adMob.Active, Has.No.Member("inter"));
            Assert.That(levelPlay.Active, Has.No.Member("inter"));
            Assert.That(adMob.Active, Does.Contain("reward"));
        }

        [Test]
        public void Primary_LoadFailure_ActivatesNextRoute_AndReadyPrimaryDeactivatesFallback()
        {
            using var ads = Create();
            Init(ads);

            max.FailLoad("inter");
            Assert.That(adMob.Active, Does.Contain("inter"), "fallback opened");
            Assert.That(levelPlay.Active, Has.No.Member("inter"));

            adMob.FailLoad("inter");
            Assert.That(levelPlay.Active, Does.Contain("inter"), "second fallback opened");

            max.MarkReady("inter");
            Assert.That(max.Active, Does.Contain("inter"));
            Assert.That(adMob.Active, Has.No.Member("inter"), "primary ready again -> fallbacks stop loading");
            Assert.That(levelPlay.Active, Has.No.Member("inter"));
        }

        [Test]
        public void Primary_FallbackThatIsShowing_StaysActiveUntilClosed()
        {
            using var ads = Create();
            Init(ads);
            max.FailLoad("inter");
            adMob.MarkReady("inter");
            adMob.Pending = new TaskCompletionSource<AdShowResult>();

            ValueTask<AdShowResult> show = ads.ShowAsync("inter");
            max.MarkReady("inter");
            Assert.That(adMob.Active, Does.Contain("inter"), "showing route is not deactivated");

            adMob.Pending.SetResult(AdShowResult.Completed);
            Assert.That(Await(show), Is.EqualTo(AdShowResult.Completed));
            Assert.That(adMob.Active, Has.No.Member("inter"), "deactivated once closed");
        }

        [Test]
        public void Parallel_ActivatesEveryRoute()
        {
            settings.LoadStrategy = AdLoadStrategy.Parallel;
            using var ads = Create();
            Init(ads);

            Assert.That(max.Active, Does.Contain("inter"));
            Assert.That(adMob.Active, Does.Contain("inter"));
            Assert.That(levelPlay.Active, Does.Contain("inter"));
        }

        [Test]
        public void Plan_LoadStrategy_OverridesSettings()
        {
            using var ads = Create(new AdsPlan { Name = "p", LoadStrategy = "parallel" });
            Init(ads);
            Assert.That(ads.LoadStrategy, Is.EqualTo(AdLoadStrategy.Parallel));
            Assert.That(levelPlay.Active, Does.Contain("inter"));
        }

        [Test]
        public void Show_UsesFirstReadyRoute_InOrder()
        {
            settings.LoadStrategy = AdLoadStrategy.Parallel;
            using var ads = Create();
            Init(ads);
            levelPlay.MarkReady("inter");
            adMob.MarkReady("inter");

            var results = new List<(string, string, AdShowResult)>();
            ads.ShowResult += (p, provider, r) => results.Add((p, provider, r));
            Assert.That(Await(ads.ShowAsync("inter")), Is.EqualTo(AdShowResult.Completed));
            Assert.That(adMob.Shown, Is.EqualTo(new[] { "inter" }));
            Assert.That(levelPlay.Shown, Is.Empty);
            Assert.That(results, Is.EqualTo(new[] { ("inter", "admob", AdShowResult.Completed) }));
        }

        // ---------- initialization ----------

        [Test]
        public void Init_FailingAdapter_IsDropped_OthersStillWork()
        {
            max.InitError = new InvalidOperationException("boom");
            using var ads = Create();
            LogAssert.Expect(LogType.Error, new Regex("max failed to initialize: boom"));
            Init(ads);

            Assert.That(ads.IsProviderInitialized("max"), Is.False);
            Assert.That(ads.GetEffectiveRoutes("inter"), Is.EqualTo(new[] { "admob", "levelplay" }));
            Assert.That(adMob.Active, Does.Contain("inter"), "admob becomes the primary route");
            Assert.That(ads.IsInitialized, Is.True);
        }

        [Test]
        public async Task Init_TimedOutAdapter_IsDropped_AndDoesNotBlockOthers()
        {
            max.InitGate = new TaskCompletionSource<bool>();
            using var ads = Create();
            ads.InitializeTimeout = TimeSpan.FromMilliseconds(50);
            LogAssert.Expect(LogType.Error, new Regex("max failed to initialize: timed out"));

            await ads.InitializeAsync();

            Assert.That(ads.IsProviderInitialized("admob"), Is.True);
            Assert.That(ads.IsProviderInitialized("max"), Is.False);
            Assert.That(ads.GetEffectiveRoutes("inter"), Has.No.Member("max"));
        }

        [Test]
        public void Init_ContextCarriesPlatformUnitIds_AndConsent()
        {
            AdsTestPlatform.RequireAndroidUnitIds();
            using var ads = Create();
            ads.ApplyConsent(new ConsentState { GdprApplies = true, HasUserConsent = false });
            Init(ads);

            Assert.That(adMob.Context, Is.Not.Null);
            var units = new Dictionary<string, string>();
            foreach (AdUnitAssignment u in adMob.Context.Units) units[u.PlacementId] = u.AdUnitId;
            Assert.That(units["inter"], Is.EqualTo("a1"), "Editor = Android unit id");
            Assert.That(units["reward"], Is.EqualTo("a2"));
            Assert.That(adMob.Context.Consent.HasUserConsent, Is.False);
            Assert.That(adMob.Consents, Is.Not.Empty, "consent applied before init");
            Assert.That(adMob.Consents[0].GdprApplies, Is.True);
        }

        // ---------- plan ----------

        [Test]
        public void Plan_RouteOrder_IsUsed_AndUnknownRoutesIgnored()
        {
            AdsTestPlatform.RequireAndroidUnitIds();
            var plan = new AdsPlan
            {
                Name = "ab_b",
                Placements = new List<AdsPlanPlacement> { new() { Id = "inter", Routes = new List<string> { "levelplay", "bogus", "max" } } },
            };
            using var ads = Create(plan);
            Init(ads);

            Assert.That(ads.GetEffectiveRoutes("inter"), Is.EqualTo(new[] { "levelplay", "max" }));
            Assert.That(levelPlay.Active, Does.Contain("inter"));
            Assert.That(max.Active, Has.No.Member("inter"));
            Assert.That(ads.Placements["inter"].Routes[1].CurrentAdUnitId, Is.EqualTo("m1"), "unit ids stay local");
        }

        [Test]
        public void Plan_DisabledPlacement_BlocksAndDeactivates()
        {
            settings.LoadStrategy = AdLoadStrategy.Parallel;
            using var ads = Create();
            Init(ads);
            max.MarkReady("inter");
            Assert.That(max.Active, Does.Contain("inter"));

            ads.ApplyPlan(new AdsPlan { Name = "off", Placements = new List<AdsPlanPlacement> { new() { Id = "inter", Enabled = false } } });

            Assert.That(ads.CanShow("inter", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.PlacementDisabled));
            Assert.That(max.Active, Has.No.Member("inter"));
            Assert.That(adMob.Active, Has.No.Member("inter"));
            Assert.That(Await(ads.ShowAsync("inter")), Is.EqualTo(AdShowResult.Failed));
            Assert.That(max.Shown, Is.Empty);
        }

        [Test]
        public void Plan_NoRouteLeft_GivesNoProvider()
        {
            var plan = new AdsPlan { Name = "x", Providers = new List<string> { "levelplay" } };
            using var ads = Create(plan);
            Init(ads);

            Assert.That(ads.CanShow("reward", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.NoProvider), "reward routes to admob only, the plan allows levelplay only");
            Assert.That(ads.GetEffectiveRoutes("inter"), Is.EqualTo(new[] { "levelplay" }));
        }

        [Test]
        public void Plan_PlacementRules_OverrideSettings()
        {
            settings.Placements[0].CooldownSeconds = 10f;
            var plan = new AdsPlan
            {
                Name = "rules",
                Placements = new List<AdsPlanPlacement> { new() { Id = "inter", CooldownSeconds = 60f, MaxShowsPerSession = 1, SuppressedByNoAds = false } },
            };
            using var ads = Create(plan);
            AdPlacement p = ads.Placements["inter"];
            Assert.That(p.CooldownSeconds, Is.EqualTo(60f));
            Assert.That(p.MaxShowsPerSession, Is.EqualTo(1));
            Assert.That(p.SuppressedByNoAds, Is.False);
        }

        [Test]
        public void Policy_FromLevel_BlocksBelowTheLevel()
        {
            settings.Policy.InterstitialFromLevel = 3;
            using var ads = Create();
            max.MarkReady("inter");
            adMob.MarkReady("reward");

            Assert.That(ads.CanShow("inter", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.BelowLevel));
            Assert.That(ads.CanShow("reward", out _), Is.True, "rewarded is not an interstitial");
            int changes = 0;
            ads.PlacementReadyChanged += id => { if (id == "inter") changes++; };
            ads.SetPlayerLevel(3);
            Assert.That(changes, Is.EqualTo(1), "UI bound to readiness hears the level gate open");
            Assert.That(ads.CanShow("inter", out _), Is.True);
        }

        [Test]
        public void Placement_ShowEveryNthRequest_CountsBlockedRequests()
        {
            settings.Placements.Find(p => p.Id == "inter").ShowEveryNthRequest = 3;
            using var ads = Create();
            max.MarkReady("inter");

            Assert.That(Await(ads.ShowAsync("inter")), Is.EqualTo(AdShowResult.Failed));
            Assert.That(ads.CanShow("inter", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.RequestCount));
            Assert.That(Await(ads.ShowAsync("inter")), Is.EqualTo(AdShowResult.Failed));
            Assert.That(ads.CanShow("inter", out _), Is.True, "the next request is the third");
            Assert.That(Await(ads.ShowAsync("inter")), Is.EqualTo(AdShowResult.Completed));
            Assert.That(max.Shown, Is.EqualTo(new[] { "inter" }));

            // Blocked by another rule on its turn: the following request shows.
            settings.Policy.InterstitialMinIntervalSeconds = 30f;
            using var timed = Create();
            settings.Policy.InterstitialMinIntervalSeconds = 0f;
            Assert.That(Await(timed.ShowAsync("inter")), Is.EqualTo(AdShowResult.Failed));
            Assert.That(Await(timed.ShowAsync("inter")), Is.EqualTo(AdShowResult.Failed));
            Assert.That(Await(timed.ShowAsync("inter")), Is.EqualTo(AdShowResult.Completed));
            clock = 10f;
            for (int i = 0; i < 3; i++) Assert.That(Await(timed.ShowAsync("inter")), Is.EqualTo(AdShowResult.Failed));
            clock = 40f;
            Assert.That(Await(timed.ShowAsync("inter")), Is.EqualTo(AdShowResult.Completed), "the third request was blocked by the interval");
        }

        [Test]
        public void GameRule_BlocksAndHearsRequestsAndShows()
        {
            using var ads = Create();
            max.MarkReady("inter");
            adMob.MarkReady("reward");
            var rule = new CountingRule();
            ads.AddRule(rule);

            Assert.That(ads.CanShow("inter", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.GameRule));
            Assert.That(Await(ads.ShowAsync("inter")), Is.EqualTo(AdShowResult.Failed));
            Assert.That(rule.Requests, Is.EqualTo(new[] { ("inter", AdBlockReason.GameRule) }));

            rule.Open = true;
            Assert.That(Await(ads.ShowAsync("inter")), Is.EqualTo(AdShowResult.Completed));
            Assert.That(rule.Shown, Is.EqualTo(new[] { "inter" }));

            Assert.That(ads.RemoveRule(rule), Is.True);
            rule.Open = false;
            max.MarkReady("inter");
            Assert.That(ads.CanShow("inter", out _), Is.True);
        }

        [Test]
        public void GameRule_ThatThrows_BlocksWithAWarning()
        {
            using var ads = Create();
            max.MarkReady("inter");
            ads.AddRule(AdShowRule.From(_ => throw new InvalidOperationException("boom")));
            LogAssert.Expect(LogType.Warning, new Regex("threw for 'inter'"));
            Assert.That(ads.CanShow("inter", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.GameRule));
        }

        [Test]
        public void Policy_IntervalAcrossSessions_CountsThePreviousSession()
        {
            settings.Policy.InterstitialMinIntervalSeconds = 60f;
            PlayerPrefs.SetString(CompositeAdsService.LastInterstitialKey, (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 20).ToString());
            try
            {
                using (var ads = Create())
                {
                    max.MarkReady("inter");
                    Assert.That(ads.CanShow("inter", out _), Is.True, "off: a new session starts with no interval");
                }

                settings.Policy.InterstitialIntervalAcrossSessions = true;
                using (var ads = Create())
                {
                    max.MarkReady("inter");
                    Assert.That(ads.CanShow("inter", out AdBlockReason reason), Is.False);
                    Assert.That(reason, Is.EqualTo(AdBlockReason.GlobalInterval));
                    clock = 41f;
                    Assert.That(ads.CanShow("inter", out _), Is.True, "60 s after the previous session's interstitial");
                    Assert.That(Await(ads.ShowAsync("inter")), Is.EqualTo(AdShowResult.Completed));
                    Assert.That(long.Parse(PlayerPrefs.GetString(CompositeAdsService.LastInterstitialKey)),
                        Is.GreaterThanOrEqualTo(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1), "saved for the next session");
                }
            }
            finally
            {
                PlayerPrefs.DeleteKey(CompositeAdsService.LastInterstitialKey);
            }
        }

        [Test]
        public void Plan_SetsTheNewRules()
        {
            var plan = new AdsPlan
            {
                Name = "rules",
                Policy = new AdsPlanPolicy { InterstitialFromLevel = 2, InterstitialIntervalAcrossSessions = true },
                Placements = new List<AdsPlanPlacement> { new() { Id = "inter", ShowEveryNthRequest = 4 } },
            };
            using var ads = Create(plan);
            Assert.That(ads.Policy.InterstitialFromLevel, Is.EqualTo(2));
            Assert.That(ads.Policy.InterstitialIntervalAcrossSessions, Is.True);
            Assert.That(ads.Placements["inter"].ShowEveryNthRequest, Is.EqualTo(4));
        }

        [Test]
        public void BannerHeightChanged_OnLoadShowHide()
        {
            settings.Placements.Add(new AdPlacement { Id = "banner", Format = AdFormat.Banner, Routes = { new AdRoute("max", "b1") } });
            using var ads = Create();
            var heights = new List<(string, float)>();
            ads.BannerHeightChanged += (id, px) => heights.Add((id, px));
            max.BannerHeight = 150f;

            Assert.That(Await(ads.ShowAsync("banner")), Is.EqualTo(AdShowResult.Completed));
            Assert.That(heights, Is.Empty, "not loaded yet");
            max.MarkReady("banner");
            Assert.That(heights, Is.EqualTo(new[] { ("banner", 150f) }));
            Assert.That(ads.GetVisibleBannerHeightPixels("banner"), Is.EqualTo(150f));

            ads.HideBanner("banner");
            Assert.That(heights[heights.Count - 1], Is.EqualTo(("banner", 0f)));
            Assert.That(ads.GetVisibleBannerHeightPixels("banner"), Is.EqualTo(0f));
            Await(ads.ShowAsync("banner"));
            Assert.That(heights[heights.Count - 1], Is.EqualTo(("banner", 150f)), "shown again while loaded");
        }

        [Test]
        public void BannerLayout_CustomCentre_StaysInTheSafeArea()
        {
            // 1080 x 1920 px at 3 px/dp = 360 x 640 dp; MREC 300 x 250 dp.
            Vector2 at = BannerLayout.CustomTopLeftDp(new Vector2(0.72f, 0.42f), new Vector2(300f, 250f), 3f, new Vector2(1080f, 1920f));
            Assert.That(at.x, Is.EqualTo(60f).Within(0.01f), "0.72 * 360 - 150 = 109.2, clamped to 360 - 300");
            Assert.That(at.y, Is.EqualTo(0.42f * 640f - 125f).Within(0.01f));
            Vector2 corner = BannerLayout.CustomTopLeftDp(Vector2.zero, new Vector2(300f, 250f), 3f, new Vector2(1080f, 1920f));
            Assert.That(corner, Is.EqualTo(Vector2.zero));
        }

        private sealed class CountingRule : AdShowRule
        {
            public bool Open;
            public readonly List<(string, AdBlockReason)> Requests = new();
            public readonly List<string> Shown = new();

            public override bool Allows(AdPlacement placement) => Open || placement.Format != AdFormat.Interstitial;
            public override void OnRequested(AdPlacement placement, AdBlockReason reason) => Requests.Add((placement.Id, reason));
            public override void OnShown(AdPlacement placement, AdShowResult result) => Shown.Add(placement.Id);
        }

        [Test]
        public void Policy_GlobalInterval_BlocksAnyInterstitial()
        {
            settings.Policy.InterstitialMinIntervalSeconds = 30f;
            using var ads = Create();
            max.MarkReady("inter");
            adMob.MarkReady("inter2");

            Assert.That(Await(ads.ShowAsync("inter")), Is.EqualTo(AdShowResult.Completed));
            clock = 10f;
            Assert.That(ads.CanShow("inter2", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.GlobalInterval));
            clock = 30f;
            Assert.That(ads.CanShow("inter2", out _), Is.True);
        }

        [Test]
        public void Policy_AppOpen_DoesNotRestartTheInterstitialInterval_UnlessItFollowsTheRules()
        {
            settings.Policy.InterstitialMinIntervalSeconds = 180f;
            settings.Policy.AppOpenMinIntervalSeconds = 0f;
            settings.Placements.Add(new AdPlacement { Id = "open", Format = AdFormat.AppOpen, Routes = { new AdRoute("max", "m9") } });
            using (var ads = Create())
            {
                max.MarkReady("open");
                adMob.MarkReady("inter2");
                clock = 200f;
                Assert.That(Await(ads.ShowAsync("open")), Is.EqualTo(AdShowResult.Completed));
                clock = 201f;
                Assert.That(ads.CanShow("inter2", out _), Is.True, "a return to the app does not hold interstitials back");
            }

            settings.Policy.AppOpenFollowsInterstitialRules = true;
            using (var ads = Create())
            {
                max.MarkReady("open");
                adMob.MarkReady("inter2");
                clock = 400f;
                Assert.That(Await(ads.ShowAsync("open")), Is.EqualTo(AdShowResult.Completed));
                clock = 401f;
                Assert.That(ads.CanShow("inter2", out AdBlockReason reason), Is.False);
                Assert.That(reason, Is.EqualTo(AdBlockReason.GlobalInterval));
            }
        }

        [Test]
        public void Policy_FirstInterstitialDelay_FromServiceStart()
        {
            clock = 100f;
            var plan = new AdsPlan { Name = "delay", Policy = new AdsPlanPolicy { FirstInterstitialDelaySeconds = 60f } };
            using var ads = Create(plan);
            max.MarkReady("inter");
            adMob.MarkReady("reward");

            clock = 150f;
            Assert.That(ads.CanShow("inter", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.SessionStartGrace));
            Assert.That(ads.CanShow("reward", out _), Is.True, "rewarded is not an interstitial");
            clock = 160f;
            Assert.That(ads.CanShow("inter", out _), Is.True);
        }

        [Test]
        public void Policy_RewardedResetsInterstitialTimer_FromPlan()
        {
            var plan = new AdsPlan { Name = "reset", Policy = new AdsPlanPolicy { InterstitialMinIntervalSeconds = 30f, RewardedResetsInterstitialTimer = true } };
            using var ads = Create(plan);
            max.MarkReady("inter");
            adMob.MarkReady("reward");

            clock = 100f;
            Assert.That(ads.CanShow("inter", out _), Is.True);
            Assert.That(Await(ads.ShowAsync("reward")), Is.EqualTo(AdShowResult.Completed));
            clock = 110f;
            Assert.That(ads.CanShow("inter", out AdBlockReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AdBlockReason.GlobalInterval));

            ads.ApplyPlan(new AdsPlan { Name = "noreset", Policy = new AdsPlanPolicy { InterstitialMinIntervalSeconds = 30f, RewardedResetsInterstitialTimer = false } });
            clock = 200f;
            Await(ads.ShowAsync("reward"));
            clock = 205f;
            Assert.That(ads.CanShow("inter", out _), Is.True, "without the reset a rewarded ad does not start the interval");
        }

        [Test]
        public void ApplyPlan_Live_ChangesOrderAndPolicy_AndNullRevertsToSettings()
        {
            using var ads = Create();
            Init(ads);
            Assert.That(max.Active, Does.Contain("inter"));
            var seen = new List<string>();
            ads.PlacementReadyChanged += seen.Add;

            ads.ApplyPlan(new AdsPlan
            {
                Name = "live",
                Placements = new List<AdsPlanPlacement> { new() { Id = "inter", Routes = new List<string> { "admob", "max" } } },
                Policy = new AdsPlanPolicy { AppOpenSuppressSecondsAfterFullscreen = 9f },
            });

            Assert.That(adMob.Active, Does.Contain("inter"));
            Assert.That(max.Active, Has.No.Member("inter"));
            Assert.That(ads.AppOpenSuppressSecondsAfterFullscreen, Is.EqualTo(9f));
            Assert.That(seen, Does.Contain("inter"));

            ads.ApplyPlan(null);
            Assert.That(ads.GetEffectiveRoutes("inter"), Is.EqualTo(new[] { "max", "admob", "levelplay" }));
            Assert.That(max.Active, Does.Contain("inter"));
            Assert.That(ads.AppOpenSuppressSecondsAfterFullscreen, Is.EqualTo(5f));
        }

        [Test]
        public void LegacyOverrides_StillApply_AfterThePlan()
        {
            var plan = new AdsPlan { Name = "p", Placements = new List<AdsPlanPlacement> { new() { Id = "inter", CooldownSeconds = 60f } } };
            using var ads = Create(plan);
            ads.ApplyOverrides(new List<AdPlacementOverride> { new() { Id = "inter", CooldownSeconds = 5f, Routes = new List<AdRoute> { new AdRoute(AdProviderType.AdMob) } } });

            Assert.That(ads.Placements["inter"].CooldownSeconds, Is.EqualTo(5f), "legacy rows win over the plan");
            Assert.That(ads.GetEffectiveRoutes("inter"), Is.EqualTo(new[] { "admob" }));

            ads.ApplyPlan(new AdsPlan { Name = "p2", Placements = new List<AdsPlanPlacement> { new() { Id = "inter", MaxShowsPerSession = 3 } } });
            Assert.That(ads.Placements["inter"].CooldownSeconds, Is.EqualTo(5f), "legacy rows survive a new plan");
            Assert.That(ads.Placements["inter"].MaxShowsPerSession, Is.EqualTo(3));
        }

        [Test]
        public void ImpressionPaid_IsReRaised_AndMapsToAdRevenue()
        {
            using var ads = Create();
            var seen = new List<AdImpression>();
            ads.ImpressionPaid += seen.Add;
            var impression = new AdImpression("inter", "max", AdFormat.Interstitial, "m1", "applovin_exchange", 0.0123, null, "exact");
            max.Pay(impression);

            Assert.That(seen, Has.Count.EqualTo(1));
            AdRevenue r = AdsModule.ToAdRevenue(seen[0]);
            Assert.That(r.Platform, Is.EqualTo("AppLovin"));
            Assert.That(r.Network, Is.EqualTo("applovin_exchange"));
            Assert.That(r.Format, Is.EqualTo(AdFormatKind.Interstitial));
            Assert.That(r.Placement, Is.EqualTo("inter"));
            Assert.That(r.AdUnitId, Is.EqualTo("m1"));
            Assert.That(r.Value, Is.EqualTo(0.0123));
            Assert.That(r.Currency, Is.EqualTo("USD"));
            Assert.That(r.Precision, Is.EqualTo("exact"));
        }

        [Test]
        public void Dispose_DisposesAdapters_AndStopsForwarding()
        {
            var ads = Create();
            var seen = new List<string>();
            ads.PlacementReadyChanged += seen.Add;
            ads.Dispose();
            max.MarkReady("inter");
            Assert.That(seen, Is.Empty);
            Assert.That(max.Disposed, Is.True);
        }

        // ---------- plan source ----------

        // ---------- installer selection & consent ----------

        [Test]
        public void SelectProviders_IsCatalogIntersectSettingsIntersectPlan()
        {
            settings.Providers.Add(new AdsProviderConfig { ProviderId = "admob", Enabled = true });
            settings.Providers.Add(new AdsProviderConfig { ProviderId = "max", Enabled = false });
            settings.Providers.Add(new AdsProviderConfig { ProviderId = "levelplay", Enabled = true });
            var catalog = new IAdsProviderFactory[]
            {
                new FakeAdsFactory("admob"), new FakeAdsFactory("max"), new FakeAdsFactory("levelplay"),
                new FakeAdsFactory("none"), new FakeAdsFactory("custom"),
            };

            List<IAdsProviderFactory> noPlan = AdsModule.SelectProviders(catalog, settings, null);
            Assert.That(noPlan.ConvertAll(f => f.ProviderId), Is.EqualTo(new[] { "admob", "levelplay" }),
                        "max disabled, none never selected, custom has no row and no route");

            var plan = new AdsPlan { Name = "p", Providers = new List<string> { "levelplay", "max" } };
            List<IAdsProviderFactory> withPlan = AdsModule.SelectProviders(catalog, settings, plan);
            Assert.That(withPlan.ConvertAll(f => f.ProviderId), Is.EqualTo(new[] { "levelplay" }), "max stays off: the settings switch wins");

            settings.Placements[0].Routes.Add(new AdRoute("custom", "c1"));
            Assert.That(AdsModule.SelectProviders(catalog, settings, null).ConvertAll(f => f.ProviderId),
                        Does.Contain("custom"), "a provider without a row is used when a route names it");

            var notInCatalog = new IAdsProviderFactory[] { new FakeAdsFactory("admob") };
            Assert.That(AdsModule.SelectProviders(notInCatalog, settings, null).ConvertAll(f => f.ProviderId), Is.EqualTo(new[] { "admob" }));
        }

        [Test]
        public void SelectConsent_PicksHighestPriorityWithACmp()
        {
            var log = new List<string>();
            var a = new FakeConsent("ump", log);
            var b = new FakeConsent("lp", log);
            var factories = new IAdsProviderFactory[]
            {
                new FakeAdsFactory("max", 100, null),
                new FakeAdsFactory("admob", 50, a),
                new FakeAdsFactory("levelplay", 10, b),
            };
            Assert.That(AdsModule.SelectConsent(factories, settings), Is.SameAs(a), "max has the top priority but no CMP");
            Assert.That(AdsModule.SelectConsent(new IAdsProviderFactory[] { new FakeAdsFactory("x") }, settings), Is.Null);
        }

        [Test]
        public void MapConsent_FromStatus()
        {
            var c = new FakeConsent("c", null) { StatusValue = ConsentStatus.Required, CanRequest = false };
            ConsentState s = AdsModule.MapConsent(c, new FakeIabStore());
            Assert.That(s.CanRequestAds, Is.False);
            Assert.That(s.GdprApplies, Is.True);
            Assert.That(s.HasUserConsent, Is.False);

            c.StatusValue = ConsentStatus.NotRequired;
            c.CanRequest  = true;
            s = AdsModule.MapConsent(c, new FakeIabStore());
            Assert.That(s.GdprApplies, Is.False);
            Assert.That(s.HasUserConsent, Is.True);
        }

        [Test]
        public async Task Installer_RunsCmpBeforeAdapterInit_AndForwardsPrivacyChanges()
        {
            var log = new List<string>();
            var ump = new FakeConsent("ump", log);
            var admobFactory = new FakeAdsFactory("admob", 50, ump, log);
            var maxFactory   = new FakeAdsFactory("max", 10, new FakeConsent("maxcmp", log), log);
            AdsProviderCatalog.ClearForTests();
            try
            {
                AdsProviderCatalog.Register(admobFactory);
                AdsProviderCatalog.Register(maxFactory);
                settings.Providers.Add(new AdsProviderConfig { ProviderId = "admob" });
                settings.Providers.Add(new AdsProviderConfig { ProviderId = "max" });

                ServiceRegistry services = CoreServices();
                settings.InitializeInBackground = false;
                try
                {
                    await settings.InstallAsync(services);

                    Assert.That(log, Is.EqualTo(new[] { "consent:ump", "init:admob", "init:max" }).Or.EqualTo(new[] { "consent:ump", "init:max", "init:admob" }));
                    Assert.That(services.TryResolve(out IConsentService consent), Is.True);
                    Assert.That(services.TryResolve(out IAdsService ads), Is.True);
                    Assert.That(ads, Is.InstanceOf<CompositeAdsService>());
                    Assert.That(((CompositeAdsService)ads).Adapters.Count, Is.EqualTo(2), "no mock adapter when SDKs are selected");

                    await consent.ShowPrivacyOptionsAsync();
                    ConsentState last = admobFactory.Created.Consents[admobFactory.Created.Consents.Count - 1];
                    Assert.That(last.HasUserConsent, Is.False, "privacy form result pushed to every adapter");
                    Assert.That(maxFactory.Created.Consents[maxFactory.Created.Consents.Count - 1].HasUserConsent, Is.False);
                    Assert.That(services.Resolve<IConsentState>().Current.HasUserConsent, Is.False, "and to the shared consent state");
                }
                finally
                {
                    services.Dispose();
                }
            }
            finally
            {
                AdsProviderCatalog.ClearForTests();
            }
        }

        [Test]
        public async Task Installer_NothingSelected_UsesMockForEveryPlacement()
        {
            AdsProviderCatalog.ClearForTests();
            ServiceRegistry services = CoreServices();
            settings.InitializeInBackground = false;
            settings.NullAdapterFakeDelaySeconds = 0f;
            try
            {
                await settings.InstallAsync(services);
                Assert.That(services.TryResolve(out IAdsService ads), Is.True);
                var composite = (CompositeAdsService)ads;
                Assert.That(composite.Adapters.Count, Is.EqualTo(1));
                Assert.That(composite.Adapters[0].ProviderId, Is.EqualTo("none"));
                Assert.That(ads.IsPlacementReady("inter"), Is.True, "mock serves every placement in the Editor");
                Assert.That(services.TryResolve(out IConsentService _), Is.False, "no CMP installed");
                Assert.That(services.Resolve<IConsentState>().Current.IsResolved, Is.True, "consent published as not required");
            }
            finally
            {
                services.Dispose();
            }
        }

        [Test]
        public async Task Module_NoAdsEntitlement_BlocksSuppressedPlacements_Live()
        {
            AdsProviderCatalog.ClearForTests();
            ServiceRegistry services = CoreServices();
            settings.InitializeInBackground = false;
            settings.NullAdapterFakeDelaySeconds = 0f;
            try
            {
                await settings.InstallAsync(services);
                IAdsService ads = services.Resolve<IAdsService>();
                Assert.That(ads.CanShow("inter", out _), Is.True);

                services.Resolve<IEntitlements>().Grant(Entitlements.NoAds);

                Assert.That(ads.CanShow("inter", out AdBlockReason reason), Is.False);
                Assert.That(reason, Is.EqualTo(AdBlockReason.NoAdsOwned));
                Assert.That(ads.CanShow("reward", out _), Is.True, "rewarded is not suppressed");
            }
            finally
            {
                services.Dispose();
            }
        }

        [Test]
        public async Task Module_ForwardsImpressionRevenue_ToAnalytics()
        {
            var factory = new FakeAdsFactory("max");
            AdsProviderCatalog.ClearForTests();
            AdsProviderCatalog.Register(factory);
            ServiceRegistry services = CoreServices();
            var sink = new RevenueSink();
            services.Resolve<IAnalyticsSinkRegistry>().AddSink(sink);
            settings.InitializeInBackground = false;
            try
            {
                await settings.InstallAsync(services);
                factory.Created.Pay(new AdImpression("inter", "max", AdFormat.Interstitial, "m1", "unity", 0.02, "USD"));

                Assert.That(sink.Revenues, Has.Count.EqualTo(1));
                Assert.That(sink.Revenues[0].Platform, Is.EqualTo("AppLovin"));
                Assert.That(sink.Revenues[0].Placement, Is.EqualTo("inter"));
            }
            finally
            {
                services.Dispose();
                AdsProviderCatalog.ClearForTests();
            }
        }

        [Test]
        public async Task Installer_ProviderJoiningLate_RepublishesConsent()
        {
            // MAX-style CMP: it answers only inside the SDK init, which outlasts the composite's init timeout.
            var cmp = new GatedConsent { StatusValue = ConsentStatus.Unknown };
            var factory = new FakeAdsFactory("max", 10, cmp);
            AdsProviderCatalog.ClearForTests();
            AdsProviderCatalog.Register(factory);
            settings.Providers.Add(new AdsProviderConfig { ProviderId = "max" });
            ServiceRegistry services = CoreServices();
            settings.InitializeInBackground = true;
            try
            {
                await settings.InstallAsync(services); // returns while the consent request is still pending
                var composite = (CompositeAdsService)services.Resolve<IAdsService>();
                composite.InitializeTimeout = TimeSpan.FromMilliseconds(50);
                factory.Created.InitGate = new TaskCompletionSource<bool>();
                LogAssert.Expect(LogType.Error, new Regex("max failed to initialize: timed out"));

                cmp.Gate.SetResult(true);
                for (int i = 0; i < 150 && !composite.IsInitialized; i++) await Task.Delay(20);
                Assert.That(composite.IsInitialized, Is.True);
                Assert.That(composite.IsProviderInitialized("max"), Is.False, "dropped by the init timeout");
                await Task.Delay(100); // let the post-init consent push run
                int pushed = factory.Created.Consents.Count;

                cmp.StatusValue = ConsentStatus.Obtained; // the CMP inside the SDK init answered
                factory.Created.InitGate.SetResult(true);
                for (int i = 0; i < 150 && !composite.IsProviderInitialized("max"); i++) await Task.Delay(20);

                Assert.That(composite.IsProviderInitialized("max"), Is.True, "joined late");
                Assert.That(factory.Created.Consents.Count, Is.GreaterThan(pushed), "consent pushed again on the late join");
                ConsentState expected = IabPrivacy.Map(ConsentStatus.Obtained, true);
                Assert.That(services.Resolve<IConsentState>().Current.GdprApplies, Is.EqualTo(expected.GdprApplies));
            }
            finally
            {
                services.Dispose();
                AdsProviderCatalog.ClearForTests();
            }
        }

        private sealed class GatedConsent : IConsentService
        {
            public readonly TaskCompletionSource<bool> Gate = new();
            public ConsentStatus StatusValue = ConsentStatus.Obtained;
            public ConsentStatus Status => StatusValue;
            public bool CanRequestAds => true;
            public bool IsPrivacyOptionsRequired => false;
            public ValueTask RequestAsync(CancellationToken cancellationToken = default) => new(Gate.Task);
            public ValueTask ShowPrivacyOptionsAsync(CancellationToken cancellationToken = default) => default;
        }

        private static ServiceRegistry CoreServices()
        {
            var services = new ServiceRegistry();
            var analytics = new AnalyticsService();
            services.Register<IAnalytics>(analytics);
            services.Register<IAnalyticsSinkRegistry>(analytics);
            services.Register<IEntitlements>(new MemoryEntitlements());
            services.Register<IConsentState>(new ConsentStateStore());
            return services;
        }

        private sealed class MemoryEntitlements : IEntitlements
        {
            private readonly HashSet<string> owned = new();
            public event Action<string, bool> Changed;
            public bool Has(string key) => owned.Contains(key);
            public void Grant(string key) { if (owned.Add(key)) Changed?.Invoke(key, true); }
            public void Revoke(string key) { if (owned.Remove(key)) Changed?.Invoke(key, false); }
        }

        private sealed class RevenueSink : IAnalyticsSink
        {
            public readonly List<AdRevenue> Revenues = new();
            public void Track(AnalyticsEvent analyticsEvent) { }
            public void SetUserProperty(string name, string value) { }
            public void SetUserId(string userId) { }
            public void TrackAdRevenue(AdRevenue revenue) => Revenues.Add(revenue);
            public void TrackPurchase(PurchaseRecord purchase) { }
        }

        // ---------- null provider ----------

        [Test]
        public void NullAdapter_FailNextLoads_TriggersFallback()
        {
            AdsTestPlatform.RequireAndroidUnitIds();
            settings.Placements.Add(new AdPlacement { Id = "mock", Format = AdFormat.Interstitial, Routes = { new AdRoute("none"), new AdRoute("admob", "a9") } });
            var nullAdapter = new NullAdsAdapter(settings, 0f, allowFakeAds: true) { FailNextLoads = 1 };
            using var ads = new CompositeAdsService(new IAdsProviderAdapter[] { nullAdapter, adMob }, settings, null, null, () => clock);
            Init(ads);

            Assert.That(nullAdapter.IsPlacementReady("mock"), Is.False);
            Assert.That(adMob.Active, Does.Contain("mock"), "mock failed to load -> admob route opened");
        }

        [Test]
        public void NullFactory_HasNoCmp_AndBuildsAMockAdapter()
        {
            var f = new NullAdsProviderFactory();
            Assert.That(f.ProviderId, Is.EqualTo("none"));
            Assert.That(f.ConsentPriority, Is.EqualTo(0));
            Assert.That(f.CreateConsentService(settings), Is.Null);
            using IAdsProviderAdapter a = f.CreateAdapter();
            Assert.That(a, Is.InstanceOf<NullAdsAdapter>());
        }

        // ---------- editor validation & migration ----------

        [Test]
        public void SettingsValidation_ReportsMissingProviderRow_EmptyUnitId_Duplicates_LevelPlayKey()
        {
            settings.Providers.Add(new AdsProviderConfig { ProviderId = "max" });
            settings.Providers.Add(new AdsProviderConfig { ProviderId = "levelplay" });
            settings.Placements.Add(new AdPlacement { Id = "reward", Format = AdFormat.Rewarded, SuppressedByNoAds = false, Routes = { new AdRoute("max", "x") } });
            settings.Placements.Add(new AdPlacement { Id = "ios_only", Format = AdFormat.Interstitial, Routes = { new AdRoute("max", null, "ios1") } });

            List<string> problems = AdsSettingsValidation.Validate(settings, BuildTarget.Android, _ => true);
            string all = string.Join("\n", problems);
            StringAssert.Contains("route provider 'admob' is not in Providers", all);
            StringAssert.Contains("'reward': duplicate placement id", all);
            StringAssert.Contains("'ios_only': 'max' route has no Android ad unit id", all);
            StringAssert.Contains("LevelPlay has no Android app key", all);

            List<string> ios = AdsSettingsValidation.Validate(settings, BuildTarget.iOS, _ => true);
            Assert.That(string.Join("\n", ios), Does.Not.Contain("'ios_only': 'max' route has no"));
        }

        [Test]
        public void SettingsMigration_FillsProviderIdsAndPlatformUnitIds()
        {
            var legacy = ScriptableObject.CreateInstance<AdsModule>();
            try
            {
                legacy.Placements.Add(new AdPlacement { Id = "i", Routes = { new AdRoute(AdProviderType.Max, "m1"), new AdRoute(AdProviderType.None) } });
                Assert.That(AdsSettingsValidation.Migrate(legacy), Is.True);
                AdRoute r = legacy.Placements[0].Routes[0];
                Assert.That(r.ProviderId, Is.EqualTo("max"));
                Assert.That(r.AndroidAdUnitId, Is.EqualTo("m1"));
                Assert.That(r.IosAdUnitId, Is.EqualTo("m1"));
                Assert.That(legacy.Placements[0].Routes[1].ProviderId, Is.EqualTo("none"));
                Assert.That(legacy.FindProvider("max"), Is.Not.Null);
                Assert.That(legacy.FindProvider("none"), Is.Null);
                Assert.That(AdsSettingsValidation.Migrate(legacy), Is.False, "one-shot");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(legacy);
            }
        }
    }
}
