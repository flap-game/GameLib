using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FlapGame.GameLib.AppsFlyer.Tests
{
    public class AppsFlyerTests
    {
        private sealed class FakeBackend : IAppsFlyerBackend
        {
            public readonly List<string> Calls = new();
            public readonly List<(string Name, Dictionary<string, string> Values)> Events = new();
            public (string Network, AdMediation Mediation, string Currency, double Revenue, Dictionary<string, string> Values) Revenue;

            public string Version => "test";
            public Task InitializeAsync(string devKey, string appId, bool debug) { Calls.Add($"init:{devKey}:{appId}"); return Task.CompletedTask; }
            public Task StartAsync() { Calls.Add("start"); return Task.CompletedTask; }
            public void SetCustomerUserId(string userId) => Calls.Add("cuid:" + userId);
            public void LogEvent(string name, Dictionary<string, string> values) { Calls.Add("event:" + name); Events.Add((name, values)); }
            public void LogAdRevenue(string network, AdMediation mediation, string currency, double revenue, Dictionary<string, string> values)
            {
                Calls.Add("revenue");
                Revenue = (network, mediation, currency, revenue, values);
            }
            public void SetConsent(AppsFlyerConsentData consent) => Calls.Add($"consent:{consent.IsUserSubjectToGdpr}:{consent.HasConsentForDataUsage}");
            public void EnableTcfDataCollection(bool enabled) => Calls.Add("tcf:" + enabled);
            public void StartPurchaseConnector(bool sandbox, bool storeKit2) => Calls.Add("connector");
        }

        private sealed class FakeAtt : IAttPrompt
        {
            public AttStatus Status { get; set; } = AttStatus.NotDetermined;
            public AttStatus Answer = AttStatus.Authorized;
            public int Requests;

            public Task<AttStatus> RequestAsync()
            {
                Requests++;
                Status = Answer;
                return Task.FromResult(Answer);
            }
        }

        private static AppsFlyerStartup.Options Options(AttMode att = AttMode.Request) => new()
        {
            DevKey = "key", AppId = "123", Att = att,
            ConsentWait = TimeSpan.FromSeconds(60), AttWait = TimeSpan.FromSeconds(60),
        };

        // ---------- sink ----------

        [Test]
        public void AdRevenue_GoesToLogAdRevenue_WithMediationAndPlacement()
        {
            var backend = new FakeBackend();
            new AppsFlyerSink(backend, AppsFlyerPurchaseMode.Manual, false, null).TrackAdRevenue(new AdRevenue
            {
                Platform = "AppLovin", Network = "Mintegral", Format = AdFormatKind.Interstitial, AdUnitId = "u1",
                Placement = "inter_win", Value = 0.02, Currency = "USD", CountryCode = "VN",
            });

            Assert.That(backend.Revenue.Network, Is.EqualTo("Mintegral"));
            Assert.That(backend.Revenue.Mediation, Is.EqualTo(AdMediation.ApplovinMax));
            Assert.That(backend.Revenue.Currency, Is.EqualTo("USD"));
            Assert.That(backend.Revenue.Revenue, Is.EqualTo(0.02));
            Assert.That(backend.Revenue.Values["ad_unit"], Is.EqualTo("u1"));
            Assert.That(backend.Revenue.Values["ad_type"], Is.EqualTo("interstitial"));
            Assert.That(backend.Revenue.Values["placement"], Is.EqualTo("inter_win"));
            Assert.That(backend.Revenue.Values["country"], Is.EqualTo("VN"));
        }

        [Test]
        public void Purchase_Manual_IsAfPurchase_OtherModesSendNothing()
        {
            var purchase = new PurchaseRecord
            {
                ProductId = "no_ads", ProductKind = PurchaseProductKind.NonConsumable, Price = 2.99m, Currency = "eur", TransactionId = "GPA.1",
            };

            var manual = new FakeBackend();
            new AppsFlyerSink(manual, AppsFlyerPurchaseMode.Manual, false, null).TrackPurchase(purchase);
            Assert.That(manual.Events, Has.Count.EqualTo(1));
            var (name, v) = manual.Events[0];
            Assert.That(name, Is.EqualTo("af_purchase"));
            Assert.That(v["af_revenue"], Is.EqualTo("2.99"));
            Assert.That(v["af_currency"], Is.EqualTo("EUR"));
            Assert.That(v["af_content_id"], Is.EqualTo("no_ads"));
            Assert.That(v["af_content_type"], Is.EqualTo("non_consumable"));
            Assert.That(v["af_order_id"], Is.EqualTo("GPA.1"));
            Assert.That(v["af_quantity"], Is.EqualTo("1"));

            var connector = new FakeBackend();
            new AppsFlyerSink(connector, AppsFlyerPurchaseMode.PurchaseConnector, false, null).TrackPurchase(purchase);
            Assert.That(connector.Events, Is.Empty, "the connector logs validated purchases itself");
        }

        [Test]
        public void Events_OnlyListedOnes_AsInvariantStrings()
        {
            var backend = new FakeBackend();
            var sink = new AppsFlyerSink(backend, AppsFlyerPurchaseMode.Manual, false, new[] { "level_complete" });

            sink.Track(new AnalyticsEvent("level_start"));
            sink.Track(new AnalyticsEvent("level_complete", new Dictionary<string, object> { ["level"] = 3, ["time"] = 1.5f, ["won"] = true }));
            sink.SetUserProperty("tier", "vip");
            sink.SetUserId("u1");

            Assert.That(backend.Calls, Is.EqualTo(new[] { "event:level_complete", "cuid:u1" }));
            Assert.That(backend.Events[0].Values, Is.EquivalentTo(new Dictionary<string, string> { ["level"] = "3", ["time"] = "1.5", ["won"] = "true" }));
        }

        [Test]
        public void Consent_MapsTheCmpAnswer()
        {
            Assert.That(AppsFlyerConsentData.From(new ConsentState()), Is.Null);

            AppsFlyerConsentData outside = AppsFlyerConsentData.From(new ConsentState { IsResolved = true }).Value;
            Assert.That(outside.IsUserSubjectToGdpr, Is.False);
            Assert.That(outside.HasConsentForDataUsage, Is.Null);

            AppsFlyerConsentData denied = AppsFlyerConsentData.From(new ConsentState { IsResolved = true, GdprApplies = true, HasUserConsent = false }).Value;
            Assert.That(denied.IsUserSubjectToGdpr, Is.True);
            Assert.That(denied.HasConsentForDataUsage, Is.False);
            Assert.That(denied.HasConsentForAdsPersonalization, Is.False);
            Assert.That(denied.HasConsentForAdStorage, Is.False);
        }

        [Test]
        public void Mediation_FromAdRevenuePlatformNames()
        {
            Assert.That(AppsFlyerSink.Mediation("AppLovin"), Is.EqualTo(AdMediation.ApplovinMax));
            Assert.That(AppsFlyerSink.Mediation("AdMob"), Is.EqualTo(AdMediation.GoogleAdMob));
            Assert.That(AppsFlyerSink.Mediation("ironSource"), Is.EqualTo(AdMediation.IronSource));
            Assert.That(AppsFlyerSink.Mediation("other"), Is.EqualTo(AdMediation.Custom));
        }

        // ---------- startup order ----------

        [Test]
        public async Task Startup_WaitsForTheCmp_ThenConsent_ThenAtt_ThenStarts()
        {
            var backend = new FakeBackend();
            var consent = new ConsentStateStore();
            var att = new FakeAtt();
            var startup = new AppsFlyerStartup(backend, consent, att, Options(), () => Task.CompletedTask, () => true);

            Task run = startup.RunAsync(CancellationToken.None);
            await Task.Yield();
            Assert.That(backend.Calls, Is.EqualTo(new[] { "init:key:123" }), "waits for the CMP answer");
            Assert.That(att.Requests, Is.EqualTo(0), "no ATT prompt over the consent form");

            consent.Update(new ConsentState { GdprApplies = true, HasUserConsent = true });
            await run;

            Assert.That(backend.Calls, Is.EqualTo(new[] { "init:key:123", "consent:True:True", "start" }));
            Assert.That(att.Requests, Is.EqualTo(1));
            Assert.That(consent.Current.Att, Is.EqualTo(AttStatus.Authorized), "the ATT answer is shared");
            Assert.That(consent.Current.GdprApplies, Is.True, "SetAtt keeps the CMP answer");
        }

        [Test]
        public async Task Startup_WithoutCmp_DoesNotWait()
        {
            var backend = new FakeBackend();
            var startup = new AppsFlyerStartup(backend, new ConsentStateStore(), new FakeAtt { Status = AttStatus.NotApplicable },
                                               Options(), () => Task.CompletedTask, () => false);

            await startup.RunAsync(CancellationToken.None);

            Assert.That(backend.Calls, Is.EqualTo(new[] { "init:key:123", "start" }));
        }

        [Test]
        public async Task Startup_ConsentTimeout_StartsAnyway()
        {
            var backend = new FakeBackend();
            var options = Options();
            options.ConsentWait = TimeSpan.FromMilliseconds(30);
            var startup = new AppsFlyerStartup(backend, new ConsentStateStore(), new FakeAtt { Status = AttStatus.Denied },
                                               options, () => Task.CompletedTask, () => true);

            LogAssert.Expect(LogType.Warning, new Regex("No consent answer"));
            await startup.RunAsync(CancellationToken.None);

            Assert.That(backend.Calls[^1], Is.EqualTo("start"));
        }

        [Test]
        public async Task Startup_AttNotDeterminedWhileInactive_IsAskedAgain()
        {
            var backend = new FakeBackend();
            var consent = new ConsentStateStore();
            var att = new FakeAtt { Answer = AttStatus.NotDetermined };
            var startup = new AppsFlyerStartup(backend, consent, att, Options(), null, null, (_, _) => Task.CompletedTask);
            consent.Update(new ConsentState());

            await startup.RunAsync(CancellationToken.None);

            Assert.That(att.Requests, Is.EqualTo(3));
            Assert.That(backend.Calls[^1], Is.EqualTo("start"));
        }

        [Test]
        public async Task Startup_WaitForOther_PollsUntilAnswered()
        {
            var backend = new FakeBackend();
            var att = new FakeAtt();
            int polls = 0;
            Task Delay(TimeSpan t, CancellationToken ct)
            {
                if (t == TimeSpan.FromSeconds(60)) return Task.Delay(Timeout.Infinite, ct);
                if (++polls == 3) att.Status = AttStatus.Denied; // MAX's flow answered
                return Task.CompletedTask;
            }
            var consent = new ConsentStateStore();
            consent.Update(new ConsentState());
            var startup = new AppsFlyerStartup(backend, consent, att, Options(AttMode.WaitForOther), null, null, Delay);

            await startup.RunAsync(CancellationToken.None);

            Assert.That(att.Requests, Is.EqualTo(0), "never prompts itself");
            Assert.That(consent.Current.Att, Is.EqualTo(AttStatus.Denied));
            Assert.That(backend.Calls[^1], Is.EqualTo("start"));
        }

        [Test]
        public async Task Startup_TcfAndPurchaseConnector()
        {
            var backend = new FakeBackend();
            var options = Options(AttMode.DoNotWait);
            options.ConsentSource = AppsFlyerConsentSource.Tcf;
            options.PurchaseMode = AppsFlyerPurchaseMode.PurchaseConnector;
            var consent = new ConsentStateStore();
            consent.Update(new ConsentState { GdprApplies = true });
            var startup = new AppsFlyerStartup(backend, consent, new FakeAtt(), options, null, null);

            await startup.RunAsync(CancellationToken.None);

            Assert.That(backend.Calls, Is.EqualTo(new[] { "init:key:123", "tcf:True", "connector", "start" }));
        }
    }
}
