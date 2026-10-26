using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace FlapGame.GameLib.Analytics.Firebase.Tests
{
    public class FirebaseAnalyticsTests
    {
        private sealed class FakeBackend : IFirebaseAnalyticsBackend
        {
            public readonly List<(string Name, IReadOnlyDictionary<string, object> Parameters)> Events = new();
            public readonly Dictionary<string, string> UserProperties = new();
            public string UserId;
            public FirebaseConsent? Consent;

            public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public void LogEvent(string name, IReadOnlyDictionary<string, object> parameters) => Events.Add((name, parameters));
            public void SetUserProperty(string name, string value) => UserProperties[name] = value;
            public void SetUserId(string userId) => UserId = userId;
            public void SetConsent(FirebaseConsent consent) => Consent = consent;
        }

        private readonly List<string> warnings = new();

        private FirebaseAnalyticsSink Sink(FakeBackend backend, bool logPurchase = false) =>
            new(backend, logPurchase, new FirebaseAnalyticsSanitizer(warnings.Add));

        [SetUp]
        public void SetUp() => warnings.Clear();

        [Test]
        public void Sanitizer_EventThatFits_IsPassedOnWithoutACopy()
        {
            var parameters = new Dictionary<string, object> { ["level"] = 3, ["mode"] = "hard", ["won"] = true };
            var sanitizer = new FirebaseAnalyticsSanitizer(warnings.Add);

            AnalyticsEvent? clean = sanitizer.Sanitize(new AnalyticsEvent("level_complete", parameters));

            Assert.That(clean.Value.Name, Is.EqualTo("level_complete"));
            Assert.That(clean.Value.Parameters, Is.SameAs(parameters));
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void Sanitizer_EventThatDoesNotFit_IsStillCopiedAndFixed()
        {
            var sanitizer = new FirebaseAnalyticsSanitizer(warnings.Add);
            var parameters = new Dictionary<string, object> { ["level"] = 3, ["note"] = null, ["big"] = ulong.MaxValue };

            AnalyticsEvent? clean = sanitizer.Sanitize(new AnalyticsEvent("level_complete", parameters));

            Assert.That(clean.Value.Parameters, Is.Not.SameAs(parameters));
            Assert.That(clean.Value.Parameters["note"], Is.EqualTo(string.Empty));
            Assert.That(clean.Value.Parameters["big"], Is.EqualTo((double)ulong.MaxValue));
        }

        [Test]
        public void Sanitizer_RenamedEvent_IsRenamedTheSameEveryTime_AndWarnedOnce()
        {
            var sanitizer = new FirebaseAnalyticsSanitizer(warnings.Add);

            for (int i = 0; i < 3; i++)
                Assert.That(sanitizer.Sanitize(new AnalyticsEvent("level-complete")).Value.Name, Is.EqualTo("level_complete"));
            Assert.That(sanitizer.Sanitize(new AnalyticsEvent("session_start")).Value.Name, Is.EqualTo("x_session_start"));
            Assert.That(warnings.Count, Is.EqualTo(2));
        }

        [Test]
        public void Sanitizer_IsValidName_MatchesSanitizeName()
        {
            foreach (string name in new[] { "level_complete", "a", "Level9", "1st", "_x", "x-y", "firebase_a", "ga_b", "session_start", new string('a', 40), new string('a', 41), "x_", "a__" })
            {
                bool valid = FirebaseAnalyticsSanitizer.IsValidName(name, 40, new HashSet<string> { "session_start" });
                bool same = FirebaseAnalyticsSanitizer.SanitizeName(name, 40, new HashSet<string> { "session_start" }) == name;
                Assert.That(valid, Is.EqualTo(same), name);
            }
        }

        [Test]
        public void Sanitizer_FixesNames_ToFirebaseGrammar()
        {
            Assert.That(FirebaseAnalyticsSanitizer.SanitizeName("level-complete", 40), Is.EqualTo("level_complete"));
            Assert.That(FirebaseAnalyticsSanitizer.SanitizeName("1st_win", 40), Is.EqualTo("x_1st_win"));
            Assert.That(FirebaseAnalyticsSanitizer.SanitizeName("firebase_x", 40), Is.EqualTo("x_firebase_x"));
            Assert.That(FirebaseAnalyticsSanitizer.SanitizeName("--", 40), Is.Null);
            Assert.That(FirebaseAnalyticsSanitizer.SanitizeName(new string('a', 50), 40), Has.Length.EqualTo(40));
        }

        [Test]
        public void Track_RenamesReservedEvents_TruncatesValues_AndCapsParameters()
        {
            var backend = new FakeBackend();
            var parameters = new Dictionary<string, object> { ["long"] = new string('v', 150) };
            for (int i = 0; i < 30; i++) parameters["p" + i] = i;

            Sink(backend).Track(new AnalyticsEvent("session_start", parameters));

            Assert.That(backend.Events, Has.Count.EqualTo(1));
            Assert.That(backend.Events[0].Name, Is.EqualTo("x_session_start"));
            Assert.That(backend.Events[0].Parameters.Count, Is.EqualTo(FirebaseAnalyticsSanitizer.MaxParametersPerEvent));
            Assert.That((string)backend.Events[0].Parameters["long"], Has.Length.EqualTo(100));
            Assert.That(warnings, Has.Some.Contains("renamed"));
            Assert.That(warnings, Has.Some.Contains("more than 25 parameters"));
        }

        [Test]
        public void UserProperty_AndUserId_AreSanitized()
        {
            var backend = new FakeBackend();
            FirebaseAnalyticsSink sink = Sink(backend);

            sink.SetUserProperty("ab group", new string('x', 40));
            sink.SetUserId("u1");

            Assert.That(backend.UserProperties["ab_group"], Has.Length.EqualTo(36));
            Assert.That(backend.UserId, Is.EqualTo("u1"));
        }

        [Test]
        public void AdRevenue_IsLoggedAsAdImpression()
        {
            var backend = new FakeBackend();
            Sink(backend).TrackAdRevenue(new AdRevenue
            {
                Platform = "AppLovin", Network = "Unity Ads", Format = AdFormatKind.Rewarded, AdUnitId = "unit1",
                Placement = "revive", Value = 0.0123, Currency = "USD", Precision = "exact",
            });

            Assert.That(backend.Events, Has.Count.EqualTo(1));
            var (name, p) = backend.Events[0];
            Assert.That(name, Is.EqualTo("ad_impression"));
            Assert.That(p["ad_platform"], Is.EqualTo("AppLovin"));
            Assert.That(p["ad_source"], Is.EqualTo("Unity Ads"));
            Assert.That(p["ad_format"], Is.EqualTo("rewarded"));
            Assert.That(p["ad_unit_name"], Is.EqualTo("unit1"));
            Assert.That(p["value"], Is.EqualTo(0.0123));
            Assert.That(p["currency"], Is.EqualTo("USD"));
            Assert.That(p["placement"], Is.EqualTo("revive"));
        }

        [Test]
        public void Purchase_IsLogged_OnlyWhenTheStandardEventIsOn()
        {
            var purchase = new PurchaseRecord
            {
                ProductId = "gems_100", ProductKind = PurchaseProductKind.Consumable, Price = 1.99m, Currency = "usd", TransactionId = "t1",
            };

            var off = new FakeBackend();
            Sink(off).TrackPurchase(purchase);
            Assert.That(off.Events, Is.Empty, "Firebase already logs in_app_purchase");

            var on = new FakeBackend();
            Sink(on, logPurchase: true).TrackPurchase(purchase);
            Assert.That(on.Events, Has.Count.EqualTo(1));
            var (name, p) = on.Events[0];
            Assert.That(name, Is.EqualTo("purchase"));
            Assert.That(p["currency"], Is.EqualTo("USD"));
            Assert.That(p["value"], Is.EqualTo(1.99).Within(1e-9));
            Assert.That(p["transaction_id"], Is.EqualTo("t1"));
            var items = (IDictionary<string, object>[])p["items"];
            Assert.That(items[0]["item_id"], Is.EqualTo("gems_100"));
            Assert.That(items[0]["item_category"], Is.EqualTo("consumable"));
        }

        [Test]
        public void Consent_FollowsTheCmpAnswer()
        {
            Assert.That(FirebaseConsent.From(new ConsentState()), Is.Null, "nothing sent before the CMP reports");

            FirebaseConsent denied = FirebaseConsent.From(new ConsentState { IsResolved = true, GdprApplies = true, HasUserConsent = false }).Value;
            Assert.That(denied.AnalyticsStorage || denied.AdStorage || denied.AdUserData || denied.AdPersonalization, Is.False);

            FirebaseConsent granted = FirebaseConsent.From(new ConsentState { IsResolved = true, GdprApplies = true, HasUserConsent = true }).Value;
            Assert.That(granted.AnalyticsStorage && granted.AdStorage && granted.AdUserData && granted.AdPersonalization, Is.True);

            FirebaseConsent doNotSell = FirebaseConsent.From(new ConsentState { IsResolved = true, DoNotSell = true }).Value;
            Assert.That(doNotSell.AnalyticsStorage, Is.True);
            Assert.That(doNotSell.AdUserData, Is.False);
            Assert.That(doNotSell.AdPersonalization, Is.False);

            var backend = new FakeBackend();
            Sink(backend).ApplyConsent(new ConsentState { IsResolved = true, GdprApplies = true, HasUserConsent = true });
            Assert.That(backend.Consent?.AdStorage, Is.True);
        }
    }
}
