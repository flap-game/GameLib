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
    internal sealed class FakeRemoteConfig : IRemoteConfig
    {
        public readonly Dictionary<string, string> Values = new();

        public bool IsReady => true;
        public event Action Activated;
        public Task WhenReadyAsync() => Task.CompletedTask;
        public ValueTask FetchAndActivateAsync(CancellationToken cancellationToken = default) => default;
        public string GetString(string key, string defaultValue = "") => Values.TryGetValue(key, out string v) ? v : defaultValue;
        public bool GetBool(string key, bool defaultValue = false) => defaultValue;
        public long GetLong(string key, long defaultValue = 0L) => defaultValue;
        public double GetDouble(string key, double defaultValue = 0.0) => defaultValue;
        public T GetObject<T>(string key, T defaultValue = default) =>
            Values.TryGetValue(key, out string v) ? RemoteConfigJson.Deserialize(v, defaultValue) : defaultValue;
        public void Activate() => Activated?.Invoke();
    }

    public class AdsPlanSourceTests
    {
        [Test]
        public void PlanJson_Parses_AndUnknownProvidersAreIgnored()
        {
            const string json = "{\"Name\":\"ab\",\"Providers\":[\"MAX\",\"bogus\"],\"LoadStrategy\":\"Parallel\"," +
                                "\"Placements\":[{\"Id\":\"inter\",\"Routes\":[\"levelplay\",\"nope\"],\"Enabled\":false}]," +
                                "\"Policy\":{\"InterstitialMinIntervalSeconds\":45}}";
            LogAssert.Expect(LogType.Warning, new Regex("unknown provider 'bogus'"));
            LogAssert.Expect(LogType.Warning, new Regex("unknown provider 'nope'"));

            Assert.That(RemoteConfigAdsPlanSource.TryParse(json, out AdsPlan plan), Is.True);
            Assert.That(plan.Name, Is.EqualTo("ab"));
            Assert.That(plan.Providers, Is.EqualTo(new[] { "max" }));
            Assert.That(plan.Placements[0].Routes, Is.EqualTo(new[] { "levelplay" }));
            Assert.That(plan.Placements[0].Enabled, Is.False);
            Assert.That(plan.Placements[0].CooldownSeconds, Is.Null);
            Assert.That(plan.Policy.InterstitialMinIntervalSeconds, Is.EqualTo(45f));
            Assert.That(plan.Policy.FirstInterstitialDelaySeconds, Is.Null);
        }

        [Test]
        public void PlanJson_Malformed_IsNoPlan_WithoutThrowing()
        {
            Assert.DoesNotThrow(() =>
            {
                Assert.That(RemoteConfigAdsPlanSource.TryParse("{not json", out AdsPlan plan), Is.False);
                Assert.That(plan, Is.Null);
            });
            LogAssert.Expect(LogType.Warning, new Regex("Ads plan is malformed"));
            Assert.That(RemoteConfigAdsPlanSource.TryParse("", out AdsPlan empty), Is.False);
            Assert.That(empty, Is.Null);
        }

        [Test]
        public void PlanSource_ReadsTheCurrentValueAtInstall()
        {
            var rc = new FakeRemoteConfig();
            rc.Values["ads_plan"] = "{\"Name\":\"cached\"}";
            using var source = new RemoteConfigAdsPlanSource("ads_plan", rc);

            Assert.That(source.TryGetCachedPlan(out AdsPlan plan), Is.True);
            Assert.That(plan.Name, Is.EqualTo("cached"));
        }

        [Test]
        public void PlanSource_RaisesPlanChanged_OnActivatedWithANewValue()
        {
            var rc = new FakeRemoteConfig();
            var source = new RemoteConfigAdsPlanSource("ads_plan", rc);
            var plans = new List<AdsPlan>();
            source.PlanChanged += plans.Add;
            Assert.That(source.TryGetCachedPlan(out _), Is.False);

            rc.Activate();
            Assert.That(plans, Is.Empty, "no value before and after -> no change");

            rc.Values["ads_plan"] = "{\"Name\":\"v1\"}";
            rc.Activate();
            rc.Activate(); // same value -> no second event
            Assert.That(plans, Has.Count.EqualTo(1));
            Assert.That(plans[0].Name, Is.EqualTo("v1"));

            rc.Values["ads_plan"] = "";
            rc.Activate();
            Assert.That(plans, Has.Count.EqualTo(2));
            Assert.That(plans[1], Is.Null, "plan removed");

            source.Dispose();
            rc.Values["ads_plan"] = "{\"Name\":\"v2\"}";
            rc.Activate();
            Assert.That(plans, Has.Count.EqualTo(2), "disposed sources stop listening");
        }

        [Test]
        public async Task Module_ReadsThePlanFromRemoteConfig()
        {
            AdsProviderCatalog.ClearForTests();
            var module = ScriptableObject.CreateInstance<AdsModule>();
            module.Placements.Add(new AdPlacement { Id = "inter", Format = AdFormat.Interstitial });
            module.InitializeInBackground = false;
            module.NullAdapterFakeDelaySeconds = 0f;
            var rc = new FakeRemoteConfig();
            rc.Values["ads_plan"] = "{\"Name\":\"remote\",\"Placements\":[{\"Id\":\"inter\",\"MaxShowsPerSession\":3}]}";

            var services = new ServiceRegistry();
            var analytics = new AnalyticsService();
            services.Register<IAnalytics>(analytics);
            services.Register<IAnalyticsSinkRegistry>(analytics);
            services.Register<IEntitlements>(new PlayerPrefsEntitlements());
            services.Register<IConsentState>(new ConsentStateStore());
            services.Register<IRemoteConfig>(rc);
            try
            {
                await module.InstallAsync(services);

                var ads = (CompositeAdsService)services.Resolve<IAdsService>();
                Assert.That(ads.Placements["inter"].MaxShowsPerSession, Is.EqualTo(3));
            }
            finally
            {
                services.Dispose();
                UnityEngine.Object.DestroyImmediate(module);
            }
        }
    }
}
