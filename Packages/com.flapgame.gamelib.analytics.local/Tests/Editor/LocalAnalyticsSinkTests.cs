using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FlapGame.GameLib.Analytics.Local;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FlapGame.GameLib.Tests
{
    public class LocalAnalyticsSinkTests
    {
        private const string Endpoint = "https://example.test/sdk/v1/tracking";

        private sealed class FakeTransport : ILocalTrackingTransport
        {
            public readonly List<string> Posted = new();
            public volatile bool Succeed = true;
            public int Attempts;

            public Task<bool> PostAsync(string endpoint, string body, TimeSpan timeout, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Attempts++;
                if (!Succeed) return Task.FromResult(false);
                Posted.Add(body);
                return Task.FromResult(true);
            }
        }

        private sealed class FakeDeviceId : ILocalDeviceIdSource
        {
            public string Id = "device-1";

            public Task<string> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Id);
        }

        private sealed class FakeAtt : IAttService
        {
            public AttStatus Status { get; set; } = AttStatus.NotDetermined;

            public Task<AttStatus> RequestAsync(CancellationToken cancellationToken = default) => Task.FromResult(Status);
        }

        private static LocalTrackingOptions Options() => new()
        {
            Endpoint = Endpoint,
            BundleId = "com.flapgame.test",
            AppVersion = "1.0.0",
            RetryDelay = TimeSpan.FromMilliseconds(5),
            RequestTimeout = TimeSpan.FromSeconds(1),
            MaxConsecutiveFailures = 3,
            MaxQueuedEvents = 3,
            AttWait = TimeSpan.FromMilliseconds(50),
        };

        private static async Task<bool> WaitUntil(Func<bool> condition, int milliseconds = 2000)
        {
            for (int waited = 0; waited < milliseconds; waited += 10)
            {
                if (condition()) return true;
                await Task.Delay(10);
            }
            return condition();
        }

        [Test]
        public void Track_NumbersTheEvents_WithEventCount()
        {
            var sink = new LocalAnalyticsSink(Options(), new FakeTransport(), new FakeDeviceId());

            sink.Track(new AnalyticsEvent("first"));
            sink.Track(new AnalyticsEvent("second", new Dictionary<string, object> { ["level"] = 2 }));

            Assert.That(sink.QueuedEventCount, Is.EqualTo(2));
            Assert.That(sink.TrackedEventCount, Is.EqualTo(2));
        }

        [Test]
        public void Track_DoesNotTouchTheCallersDictionary()
        {
            var sink = new LocalAnalyticsSink(Options(), new FakeTransport(), new FakeDeviceId());
            var parameters = new Dictionary<string, object> { ["level"] = 1 };

            sink.Track(new AnalyticsEvent("e", parameters));

            Assert.That(parameters.ContainsKey(LocalAnalyticsSink.EventCountParameter), Is.False);
            Assert.That(parameters.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task RunAsync_PostsEveryEventInOrder_WithTheServerBody()
        {
            var transport = new FakeTransport();
            var sink = new LocalAnalyticsSink(Options(), transport, new FakeDeviceId());
            using var cancellation = new CancellationTokenSource();

            sink.Track(new AnalyticsEvent("a"));
            sink.Track(new AnalyticsEvent("b", new Dictionary<string, object> { ["level"] = 2 }));
            Task run = sink.RunAsync(null, cancellation.Token);

            Assert.That(await WaitUntil(() => transport.Posted.Count == 2), Is.True, "the queue did not drain");
            cancellation.Cancel();
            await run;

            Assert.That(sink.DeviceId, Is.EqualTo("device-1"));
            Assert.That(transport.Posted[0], Is.EqualTo(
                "{\"bundleId\":\"com.flapgame.test\",\"appVersion\":\"1.0.0\",\"idfa\":\"device-1\",\"eventName\":\"a\",\"params\":{\"eventCount\":0}}"));
            Assert.That(transport.Posted[1], Is.EqualTo(
                "{\"bundleId\":\"com.flapgame.test\",\"appVersion\":\"1.0.0\",\"idfa\":\"device-1\",\"eventName\":\"b\",\"params\":{\"level\":2,\"eventCount\":1}}"));
            Assert.That(sink.QueuedEventCount, Is.Zero);
        }

        [Test]
        public async Task RunAsync_KeepsTheEvent_UntilThePostSucceeds()
        {
            var transport = new FakeTransport { Succeed = false };
            LocalTrackingOptions options = Options();
            // Far above the retries this test watches: the give-up path is the next test's subject, and reaching it
            // here would clear the queue between two polls.
            options.MaxConsecutiveFailures = 1000;
            var sink = new LocalAnalyticsSink(options, transport, new FakeDeviceId());
            using var cancellation = new CancellationTokenSource();

            sink.Track(new AnalyticsEvent("a"));
            Task run = sink.RunAsync(null, cancellation.Token);

            Assert.That(await WaitUntil(() => transport.Attempts >= 2), Is.True, "the failed post was not retried");
            Assert.That(sink.QueuedEventCount, Is.EqualTo(1));

            transport.Succeed = true;
            Assert.That(await WaitUntil(() => transport.Posted.Count == 1), Is.True, "the retry did not land");
            cancellation.Cancel();
            await run;

            Assert.That(sink.IsEnabled, Is.True);
        }

        [Test]
        public async Task RunAsync_GivesUpAfterTheFailureLimit_AndDropsTheQueue()
        {
            var transport = new FakeTransport { Succeed = false };
            var sink = new LocalAnalyticsSink(Options(), transport, new FakeDeviceId());
            using var cancellation = new CancellationTokenSource();

            sink.Track(new AnalyticsEvent("a"));
            LogAssert.Expect(LogType.Warning, new Regex(@"3 posts to .* failed in a row"));
            Task run = sink.RunAsync(null, cancellation.Token);

            Assert.That(await WaitUntil(() => !sink.IsEnabled), Is.True, "local tracking stayed on");
            await run;

            Assert.That(transport.Attempts, Is.EqualTo(3));
            Assert.That(sink.QueuedEventCount, Is.Zero);

            sink.Track(new AnalyticsEvent("later"));
            Assert.That(sink.QueuedEventCount, Is.Zero, "a disabled sink must ignore later calls");
        }

        [Test]
        public void Track_WhenTheQueueIsFull_DropsTheOldest()
        {
            var sink = new LocalAnalyticsSink(Options(), new FakeTransport(), new FakeDeviceId());

            LogAssert.Expect(LogType.Warning, new Regex("The queue is full"));
            for (int i = 0; i < 5; i++) sink.Track(new AnalyticsEvent("e" + i));

            Assert.That(sink.QueuedEventCount, Is.EqualTo(3));
            Assert.That(sink.DroppedEventCount, Is.EqualTo(2));
            Assert.That(sink.TrackedEventCount, Is.EqualTo(5), "eventCount keeps counting, so the gap shows on the server");
        }

        [Test]
        public async Task RunAsync_WithoutAttConsent_StaysOff()
        {
            LocalTrackingOptions options = Options();
            options.RequireAttConsent = true;
            var transport = new FakeTransport();
            var sink = new LocalAnalyticsSink(options, transport, new FakeDeviceId());

            sink.Track(new AnalyticsEvent("a"));
            LogAssert.Expect(LogType.Warning, new Regex("App Tracking Transparency is Denied"));
            await sink.RunAsync(new FakeAtt { Status = AttStatus.Denied });

            Assert.That(sink.IsEnabled, Is.False);
            Assert.That(transport.Attempts, Is.Zero);
            Assert.That(sink.QueuedEventCount, Is.Zero);
        }

        [Test]
        public async Task RunAsync_WithAttConsent_Posts()
        {
            LocalTrackingOptions options = Options();
            options.RequireAttConsent = true;
            var transport = new FakeTransport();
            var sink = new LocalAnalyticsSink(options, transport, new FakeDeviceId { Id = "idfa-2" });
            using var cancellation = new CancellationTokenSource();

            sink.Track(new AnalyticsEvent("a"));
            Task run = sink.RunAsync(new FakeAtt { Status = AttStatus.Authorized }, cancellation.Token);

            Assert.That(await WaitUntil(() => transport.Posted.Count == 1), Is.True);
            cancellation.Cancel();
            await run;

            Assert.That(transport.Posted[0], Does.Contain("\"idfa\":\"idfa-2\""));
        }

        [Test]
        public async Task RunAsync_WhenTheAttPromptIsNeverAnswered_StaysOff()
        {
            LocalTrackingOptions options = Options();
            options.RequireAttConsent = true;
            var sink = new LocalAnalyticsSink(options, new FakeTransport(), new FakeDeviceId());

            LogAssert.Expect(LogType.Warning, new Regex("App Tracking Transparency not answered"));
            LogAssert.Expect(LogType.Warning, new Regex("App Tracking Transparency is NotDetermined"));
            await sink.RunAsync(new FakeAtt { Status = AttStatus.NotDetermined });

            Assert.That(sink.IsEnabled, Is.False);
        }

        [Test]
        public async Task RunAsync_WithoutAnAdvertisingId_FallsBackToTheDeviceId()
        {
            var transport = new FakeTransport();
            var sink = new LocalAnalyticsSink(Options(), transport, new FakeDeviceId { Id = null });
            using var cancellation = new CancellationTokenSource();

            sink.Track(new AnalyticsEvent("a"));
            Task run = sink.RunAsync(null, cancellation.Token);

            Assert.That(await WaitUntil(() => transport.Posted.Count == 1), Is.True);
            cancellation.Cancel();
            await run;

            Assert.That(sink.DeviceId, Is.EqualTo(SystemInfo.deviceUniqueIdentifier));
        }

        [Test]
        public void AdRevenueAndPurchases_BecomeEvents()
        {
            var sink = new LocalAnalyticsSink(Options(), new FakeTransport(), new FakeDeviceId());

            sink.TrackAdRevenue(new AdRevenue
            {
                Platform = "AppLovin", Network = "Unity Ads", Format = AdFormatKind.Rewarded,
                AdUnitId = "unit-1", Placement = "double_coins", Value = 0.0123, Currency = "USD",
            });
            sink.TrackPurchase(new PurchaseRecord
            {
                ProductId = "no_ads", ProductKind = PurchaseProductKind.NonConsumable,
                Price = 4.99m, Currency = "USD", TransactionId = "tx-1", IsTest = true,
            });

            Assert.That(sink.QueuedEventCount, Is.EqualTo(2));

            Dictionary<string, object> impression = LocalAnalyticsSink.BuildAdImpressionParameters(new AdRevenue
            {
                Platform = "AdMob", Format = AdFormatKind.Native, Value = 0.5, Currency = null,
            });
            Assert.That(impression["ad_platform"], Is.EqualTo("AdMob"));
            Assert.That(impression["ad_format"], Is.EqualTo("native"));
            Assert.That(impression["currency"], Is.EqualTo("USD"), "a missing currency defaults to USD");

            Dictionary<string, object> purchase = LocalAnalyticsSink.BuildPurchaseParameters(new PurchaseRecord
            {
                ProductId = "gems", ProductKind = PurchaseProductKind.Consumable, Price = 1.99m, Currency = "VND",
            });
            Assert.That(purchase["product_id"], Is.EqualTo("gems"));
            Assert.That(purchase["product_kind"], Is.EqualTo("consumable"));
            Assert.That(purchase["value"], Is.EqualTo(1.99d));
            Assert.That(purchase.ContainsKey("is_test"), Is.False);
        }

        [Test]
        public void UserIdAndUserProperties_AreIgnored()
        {
            var sink = new LocalAnalyticsSink(Options(), new FakeTransport(), new FakeDeviceId());

            sink.SetUserId("u1");
            sink.SetUserProperty("tier", "vip");

            Assert.That(sink.QueuedEventCount, Is.Zero);
        }

        [Test]
        public void AnEmptyEndpoint_IsRejected()
        {
            LocalTrackingOptions options = Options();
            options.Endpoint = " ";

            Assert.That(() => new LocalAnalyticsSink(options), Throws.ArgumentException);
        }
    }
}
