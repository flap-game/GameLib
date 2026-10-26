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
        public void FullQueue_DropsEventsBeforeRevenueAndPurchases()
        {
            var target = new RecordingSink();
            var sink = new BufferedAnalyticsSink<RecordingSink>(target, "[Test]", maxQueuedCalls: 3);

            LogAssert.Expect(LogType.Warning, new Regex("More than 3 calls"));
            sink.TrackAdRevenue(new AdRevenue { Placement = "inter" });
            sink.Track(new AnalyticsEvent("1"));
            sink.TrackPurchase(new PurchaseRecord { ProductId = "gems" });
            sink.Track(new AnalyticsEvent("2"));
            sink.Track(new AnalyticsEvent("3"));
            sink.MarkReady();

            Assert.That(target.Calls, Is.EqualTo(new[] { "revenue:inter", "purchase:gems", "event:3" }));
        }

        [Test]
        public void FullQueue_OfRevenueOnly_DropsTheOldestRevenue()
        {
            var target = new RecordingSink();
            var sink = new BufferedAnalyticsSink<RecordingSink>(target, "[Test]", maxQueuedCalls: 2);

            LogAssert.Expect(LogType.Warning, new Regex("More than 2 calls"));
            sink.TrackAdRevenue(new AdRevenue { Placement = "1" });
            sink.TrackAdRevenue(new AdRevenue { Placement = "2" });
            sink.TrackPurchase(new PurchaseRecord { ProductId = "3" });
            sink.MarkReady();

            Assert.That(target.Calls, Is.EqualTo(new[] { "revenue:2", "purchase:3" }));
        }

        [Test]
        public void BufferFilter_SkipsUnwantedEventsWhilePending_NotLive()
        {
            var target = new RecordingSink();
            var sink = new BufferedAnalyticsSink<RecordingSink>(target, "[Test]", bufferEvent: e => e.Name == "level_complete");

            sink.Track(new AnalyticsEvent("level_start"));
            sink.Track(new AnalyticsEvent("level_complete"));
            sink.TrackAdRevenue(new AdRevenue { Placement = "inter" });
            Assert.That(sink.QueuedCallCount, Is.EqualTo(2));

            sink.MarkReady();
            sink.Track(new AnalyticsEvent("level_start"));

            Assert.That(target.Calls, Is.EqualTo(new[] { "event:level_complete", "revenue:inter", "event:level_start" }));
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
        public async Task InitializeAsync_Hanging_TimesOut_ThenFlushesWhenItLandsLate()
        {
            var target = new RecordingSink();
            var sink = new BufferedAnalyticsSink<RecordingSink>(target, "[Test]");
            var late = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            sink.Track(new AnalyticsEvent("a"));

            LogAssert.Expect(LogType.Warning, new Regex("taking longer than"));
            CancellationToken initToken = default;
            Task init = sink.InitializeAsync(ct => { initToken = ct; return late.Task; }, TimeSpan.FromMilliseconds(50), expectedFailure: true);
            Assert.That(await CompletesWithin(init, 300), Is.False, "still waiting for the SDK after the timeout");

            Assert.That(sink.State, Is.EqualTo(BufferedSinkState.Pending), "a timeout is not a failure");
            Assert.That(initToken.IsCancellationRequested, Is.False, "the slow SDK start is left to finish");
            sink.Track(new AnalyticsEvent("b"));

            LogAssert.Expect(LogType.Log, new Regex("Initialized after the timeout; sent 2"));
            late.SetResult(true);
            Assert.That(await CompletesWithin(init, 2000), Is.True);

            Assert.That(sink.State, Is.EqualTo(BufferedSinkState.Ready));
            Assert.That(target.Calls, Is.EqualTo(new[] { "event:a", "event:b" }));
        }

        [Test]
        public async Task InitializeAsync_Hanging_ThenFaultingLate_Fails()
        {
            var sink = new BufferedAnalyticsSink<RecordingSink>(new RecordingSink(), "[Test]");
            var late = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            sink.Track(new AnalyticsEvent("a"));

            LogAssert.Expect(LogType.Warning, new Regex("taking longer than"));
            Task init = sink.InitializeAsync(_ => late.Task, TimeSpan.FromMilliseconds(50), expectedFailure: true);
            Assert.That(await CompletesWithin(init, 300), Is.False);
            Assert.That(sink.State, Is.EqualTo(BufferedSinkState.Pending));

            LogAssert.Expect(LogType.Warning, new Regex(@"Disabled for this session: InvalidOperationException: dependency check failed\. Dropped 1"));
            late.SetException(new InvalidOperationException("dependency check failed"));
            Assert.That(await CompletesWithin(init, 2000), Is.True);

            Assert.That(sink.State, Is.EqualTo(BufferedSinkState.Failed));
        }

        private static async Task<bool> CompletesWithin(Task task, int ms) => await Task.WhenAny(task, Task.Delay(ms)) == task;

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
