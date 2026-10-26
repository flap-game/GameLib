using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FlapGame.GameLib.Tests
{
    public class BufferedAnalyticsSinkTests
    {
        private sealed class RecordingSink : IAnalyticsSink
        {
            public readonly List<string> Calls = new();

            public void Track(AnalyticsEvent analyticsEvent) => Calls.Add("event:" + analyticsEvent.Name);
            public void SetUserProperty(string name, string value) => Calls.Add($"prop:{name}={value}");
            public void SetUserId(string userId) => Calls.Add("user:" + userId);
            public void TrackAdRevenue(AdRevenue revenue) => Calls.Add("revenue:" + revenue.Placement);
            public void TrackPurchase(PurchaseRecord purchase) => Calls.Add("purchase:" + purchase.ProductId);
        }

        [Test]
        public void Pending_BuffersEverything_AndReadyFlushesIdentityFirstThenCallsInOrder()
        {
            var target = new RecordingSink();
            var sink = new BufferedAnalyticsSink<RecordingSink>(target, "[Test]");

            sink.Track(new AnalyticsEvent("a"));
            sink.SetUserId("u1");
            sink.SetUserProperty("tier", "free");
            sink.SetUserProperty("tier", "vip");
            sink.TrackAdRevenue(new AdRevenue { Placement = "inter" });
            sink.Run(t => t.Calls.Add("custom"));
            sink.TrackPurchase(new PurchaseRecord { ProductId = "gems" });
            Assert.That(target.Calls, Is.Empty);
            Assert.That(sink.QueuedCallCount, Is.EqualTo(4));

            Assert.That(sink.MarkReady(), Is.True);
            Assert.That(target.Calls, Is.EqualTo(new[] { "user:u1", "prop:tier=vip", "event:a", "revenue:inter", "custom", "purchase:gems" }));

            sink.Track(new AnalyticsEvent("live"));
            Assert.That(target.Calls[^1], Is.EqualTo("event:live"));
            Assert.That(sink.State, Is.EqualTo(BufferedSinkState.Ready));
        }

        [Test]
        public void Failed_DropsTheBuffer_AndIgnoresLaterCalls()
        {
            var target = new RecordingSink();
            var sink = new BufferedAnalyticsSink<RecordingSink>(target, "[Test]");
            sink.Track(new AnalyticsEvent("a"));

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Test\] Disabled for this session: no config\. Dropped 1"));
            Assert.That(sink.MarkFailed("no config", expected: true), Is.True);
            Assert.That(sink.MarkReady(), Is.False);

            sink.Track(new AnalyticsEvent("b"));
            sink.SetUserId("u");
            Assert.That(target.Calls, Is.Empty);
            Assert.That(sink.FailureReason, Is.EqualTo("no config"));
        }

        [Test]
        public void FullQueue_DropsOldestFirst_WithOneWarning()
        {
            var target = new RecordingSink();
            var sink = new BufferedAnalyticsSink<RecordingSink>(target, "[Test]", maxQueuedCalls: 2);

            LogAssert.Expect(LogType.Warning, new Regex("More than 2 calls"));
            sink.Track(new AnalyticsEvent("1"));
            sink.Track(new AnalyticsEvent("2"));
            sink.Track(new AnalyticsEvent("3"));
            sink.Track(new AnalyticsEvent("4"));
            sink.MarkReady();

            Assert.That(target.Calls, Is.EqualTo(new[] { "event:3", "event:4" }));
        }

        [Test]
        public async Task InitializeAsync_Success_Flushes()
        {
            var target = new RecordingSink();
            var sink = new BufferedAnalyticsSink<RecordingSink>(target, "[Test]");
            sink.Track(new AnalyticsEvent("a"));

            await sink.InitializeAsync(_ => Task.CompletedTask, TimeSpan.FromSeconds(5), expectedFailure: true);

            Assert.That(sink.State, Is.EqualTo(BufferedSinkState.Ready));
            Assert.That(target.Calls, Is.EqualTo(new[] { "event:a" }));
        }

        [Test]
        public async Task InitializeAsync_Throwing_FailsWithTheInnerMessage()
        {
            var sink = new BufferedAnalyticsSink<RecordingSink>(new RecordingSink(), "[Test]");

            LogAssert.Expect(LogType.Error, new Regex("InvalidOperationException: Firebase app creation failed"));
            await sink.InitializeAsync(_ => throw new TypeInitializationException("Firebase.FirebaseApp", new InvalidOperationException("Firebase app creation failed")),
                                       TimeSpan.FromSeconds(5), expectedFailure: false);

            Assert.That(sink.State, Is.EqualTo(BufferedSinkState.Failed));
        }

        [Test]
        public async Task InitializeAsync_Hanging_TimesOut()
        {
            var sink = new BufferedAnalyticsSink<RecordingSink>(new RecordingSink(), "[Test]");
            var never = new TaskCompletionSource<bool>();

            LogAssert.Expect(LogType.Warning, new Regex("timed out"));
            await sink.InitializeAsync(_ => never.Task, TimeSpan.FromMilliseconds(50), expectedFailure: true);

            Assert.That(sink.State, Is.EqualTo(BufferedSinkState.Failed));
        }

        [Test]
        public async Task InitializeAsync_Cancelled_IsAnExpectedFailure()
        {
            var sink = new BufferedAnalyticsSink<RecordingSink>(new RecordingSink(), "[Test]");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            LogAssert.Expect(LogType.Warning, new Regex("cancelled"));
            await sink.InitializeAsync(ct => Task.FromCanceled(ct), TimeSpan.FromSeconds(5), expectedFailure: false, cts.Token);

            Assert.That(sink.State, Is.EqualTo(BufferedSinkState.Failed));
        }

        [Test]
        public void Disposable_Create_RunsOnce()
        {
            int calls = 0;
            IDisposable d = Disposable.Create(() => calls++);
            d.Dispose();
            d.Dispose();
            Assert.That(calls, Is.EqualTo(1));
        }
    }
}
