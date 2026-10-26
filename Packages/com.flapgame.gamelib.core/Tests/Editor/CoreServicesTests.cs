using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FlapGame.GameLib.Tests
{
    public class AppResumeTrackerTests
    {
        [Test]
        public void Resume_WithoutPause_IsIgnored()
        {
            var tracker = new AppResumeTracker();
            Assert.IsFalse(tracker.TryResume(10, out _));
        }

        [Test]
        public void PlainReturn_IsNotExternal_AndMeasuresTimeAway()
        {
            var tracker = new AppResumeTracker();
            tracker.OnPaused(10);
            Assert.IsTrue(tracker.TryResume(25, out AppResumeInfo info));
            Assert.IsFalse(info.FromExternalActivity);
            Assert.AreEqual(15, info.TimeAway.TotalSeconds, 1e-6);
        }

        [Test]
        public void PauseInsideScope_IsExternal()
        {
            var tracker = new AppResumeTracker();
            using (tracker.BeginExternalActivity("ad"))
                tracker.OnPaused(0);
            tracker.TryResume(5, out AppResumeInfo info);
            Assert.IsTrue(info.FromExternalActivity);
            Assert.AreEqual("ad", info.ExternalReason);
        }

        [Test]
        public void ScopeOpenAtResume_IsExternal()
        {
            var tracker = new AppResumeTracker();
            tracker.OnPaused(0);
            IDisposable scope = tracker.BeginExternalActivity("purchase");
            tracker.TryResume(5, out AppResumeInfo info);
            scope.Dispose();
            Assert.IsTrue(info.FromExternalActivity);
        }

        [Test]
        public void Mark_AppliesToNextReturnOnly()
        {
            var tracker = new AppResumeTracker();
            tracker.MarkExternalActivity("link");
            tracker.OnPaused(0);
            tracker.TryResume(1, out AppResumeInfo first);
            tracker.OnPaused(2);
            tracker.TryResume(3, out AppResumeInfo second);
            Assert.IsTrue(first.FromExternalActivity);
            Assert.IsFalse(second.FromExternalActivity);
        }

        [Test]
        public void TimedMark_CountsForPauseRightAfter()
        {
            var tracker = new AppResumeTracker();
            tracker.MarkExternalActivity("link", 10);
            tracker.OnPaused(11);
            tracker.TryResume(20, out AppResumeInfo info);
            Assert.IsTrue(info.FromExternalActivity);
            Assert.AreEqual("link", info.ExternalReason);
        }

        [Test]
        public void TimedMark_ExpiresWhenAppDidNotLeave()
        {
            // Ad click that never backgrounded the app; the player's Home-button trip later is a real return.
            var tracker = new AppResumeTracker();
            tracker.MarkExternalActivity("ad_click", 10);
            tracker.OnPaused(10 + AppResumeTracker.MarkLifetimeSeconds + 1);
            tracker.TryResume(100, out AppResumeInfo info);
            Assert.IsFalse(info.FromExternalActivity);
            Assert.IsNull(info.ExternalReason);
        }

        [Test]
        public void ClosedScope_DoesNotLeak()
        {
            var tracker = new AppResumeTracker();
            tracker.BeginExternalActivity("ad").Dispose();
            tracker.OnPaused(0);
            tracker.TryResume(1, out AppResumeInfo info);
            Assert.IsFalse(info.FromExternalActivity);
        }
    }

    public class ConsentStateStoreTests
    {
        [Test]
        public void Default_IsUnresolvedAndPermissive()
        {
            ConsentState state = new ConsentStateStore().Current;
            Assert.IsFalse(state.IsResolved);
            Assert.IsTrue(state.CanRequestAds);
        }

        [Test]
        public void Update_MarksResolved_RaisesOnceForSameState()
        {
            var store = new ConsentStateStore();
            int raised = 0;
            store.Changed += _ => raised++;

            store.Update(new ConsentState { GdprApplies = true, HasUserConsent = false });
            store.Update(new ConsentState { GdprApplies = true, HasUserConsent = false });

            Assert.AreEqual(1, raised);
            Assert.IsTrue(store.Current.IsResolved);
            Assert.IsFalse(store.Current.HasUserConsent);
        }

        [Test]
        public void Current_IsACopy()
        {
            var store = new ConsentStateStore();
            store.Current.CanRequestAds = false;
            Assert.IsTrue(store.Current.CanRequestAds);
        }
    }

    public class PlayerPrefsEntitlementsTests
    {
        private const string Key = "gamelib_test_entitlement";

        [SetUp, TearDown]
        public void Clean() => PlayerPrefs.DeleteKey("GameLib.Entitlement." + Key);

        [Test]
        public void Grant_PersistsAcrossInstances_AndRaisesOnChangeOnly()
        {
            var first = new PlayerPrefsEntitlements();
            var changes = new List<bool>();
            first.Changed += (_, owned) => changes.Add(owned);

            first.Grant(Key);
            first.Grant(Key);

            Assert.IsTrue(new PlayerPrefsEntitlements().Has(Key));
            CollectionAssert.AreEqual(new[] { true }, changes);
        }

        [Test]
        public void Revoke_RemovesIt()
        {
            var entitlements = new PlayerPrefsEntitlements();
            entitlements.Grant(Key);
            entitlements.Revoke(Key);
            Assert.IsFalse(new PlayerPrefsEntitlements().Has(Key));
        }
    }

    public class AnalyticsServiceTests
    {
        [Test]
        public void LateSink_GetsUserIdAndProperties()
        {
            var service = new AnalyticsService();
            service.SetUserId("u1");
            service.SetUserProperty("ab", "b");

            var sink = new RecordingSink();
            service.AddSink(sink);

            Assert.AreEqual("u1", sink.UserId);
            Assert.AreEqual("b", sink.Properties["ab"]);
        }

        [Test]
        public void ThrowingSink_DoesNotStopOthers()
        {
            var service = new AnalyticsService();
            var sink = new RecordingSink();
            service.AddSink(new ThrowingSink());
            service.AddSink(sink);

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            service.Track("level_start", ("level", 1));
            service.TrackAdRevenue(new AdRevenue { Value = 0.01 });
            service.TrackPurchase(new PurchaseRecord { ProductId = "gems" });
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual("level_start", sink.Events[0].Name);
            Assert.AreEqual(1, sink.Events[0].Parameters["level"]);
            Assert.AreEqual(1, sink.AdRevenues);
            Assert.AreEqual(1, sink.Purchases);
        }

        private sealed class RecordingSink : IAnalyticsSink
        {
            public readonly List<AnalyticsEvent> Events = new();
            public readonly Dictionary<string, string> Properties = new();
            public string UserId;
            public int AdRevenues;
            public int Purchases;

            public void Track(AnalyticsEvent analyticsEvent) => Events.Add(analyticsEvent);
            public void SetUserProperty(string name, string value) => Properties[name] = value;
            public void SetUserId(string userId) => UserId = userId;
            public void TrackAdRevenue(AdRevenue revenue) => AdRevenues++;
            public void TrackPurchase(PurchaseRecord purchase) => Purchases++;
        }

        private sealed class ThrowingSink : IAnalyticsSink
        {
            public void Track(AnalyticsEvent analyticsEvent) => throw new InvalidOperationException();
            public void SetUserProperty(string name, string value) => throw new InvalidOperationException();
            public void SetUserId(string userId) => throw new InvalidOperationException();
            public void TrackAdRevenue(AdRevenue revenue) => throw new InvalidOperationException();
            public void TrackPurchase(PurchaseRecord purchase) => throw new InvalidOperationException();
        }
    }
}
