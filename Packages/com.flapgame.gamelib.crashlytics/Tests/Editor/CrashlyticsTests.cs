using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace FlapGame.GameLib.Crashlytics.Tests
{
    public class CrashlyticsTests
    {
        private sealed class FakeBackend : ICrashlyticsBackend
        {
            public readonly List<string> Calls = new();
            public bool? Fatal;

            public Task InitializeAsync(bool reportUncaughtExceptionsAsFatal, CancellationToken cancellationToken)
            {
                Fatal = reportUncaughtExceptionsAsFatal;
                return Task.CompletedTask;
            }

            public void Log(string message) => Calls.Add("log:" + message);
            public void LogException(Exception exception) => Calls.Add("exception:" + exception.Message);
            public void SetCustomKey(string key, string value) => Calls.Add($"key:{key}={value}");
            public void SetUserId(string userId) => Calls.Add("user:" + userId);
        }

        [Test]
        public void Breadcrumb_EventWithoutParameters_IsItsName()
        {
            Assert.AreEqual("level_start", CrashBreadcrumbFormatter.Format(new AnalyticsEvent("level_start")));
        }

        [Test]
        public void Breadcrumb_Parameters_AreSortedByName_AndInvariant()
        {
            var e = new AnalyticsEvent("level_complete", new Dictionary<string, object>
            {
                ["stars"] = 3,
                ["level"] = "p1_l03",
                ["time"] = 12.5f,
                ["won"] = true,
            });
            Assert.AreEqual("level_complete level=p1_l03 stars=3 time=12.5 won=true", CrashBreadcrumbFormatter.Format(e));
        }

        [Test]
        public void Breadcrumb_Numbers_AreInvariant_AndTheBuilderIsReused()
        {
            var e = new AnalyticsEvent("score", new Dictionary<string, object>
            {
                ["int"] = -42,
                ["long"] = long.MinValue,
                ["double"] = 0.1,
                ["decimal"] = 1.5m,
            });
            const string expected = "score decimal=1.5 double=0.1 int=-42 long=-9223372036854775808";
            Assert.AreEqual(expected, CrashBreadcrumbFormatter.Format(e));
            Assert.AreEqual("single n=7", CrashBreadcrumbFormatter.Format(new AnalyticsEvent("single", new Dictionary<string, object> { ["n"] = 7 })));
            Assert.AreEqual(expected, CrashBreadcrumbFormatter.Format(e), "same text on a reused builder");
        }

        [Test]
        public void Breadcrumb_LongLine_IsCutAtMaxLength()
        {
            var e = new AnalyticsEvent("x", new Dictionary<string, object> { ["a"] = new string('z', 1000) });
            string line = CrashBreadcrumbFormatter.Format(e);
            Assert.AreEqual(CrashBreadcrumbFormatter.MaxLength, line.Length);
            StringAssert.EndsWith("…", line);
        }

        [Test]
        public void Breadcrumb_ListAndNullValues_DoNotThrow()
        {
            var e = new AnalyticsEvent("purchase", new Dictionary<string, object>
            {
                ["items"] = new List<IDictionary<string, object>> { new Dictionary<string, object> { ["id"] = "a" } },
                ["note"] = null,
            });
            Assert.AreEqual("purchase items=[…] note=null", CrashBreadcrumbFormatter.Format(e));
        }

        [Test]
        public async Task Reporter_BuffersUntilInitialized_InOrder()
        {
            var backend = new FakeBackend();
            var reporter = new CrashlyticsReporter(backend, breadcrumbsFromAnalytics: true, reportUncaughtExceptionsAsFatal: true);

            reporter.Track(new AnalyticsEvent("level_start"));
            reporter.Log("opened shop");
            reporter.RecordException(new InvalidOperationException("boom"));
            reporter.SetCustomKey("level", "3");
            reporter.SetUserId("u1");
            reporter.SetUserProperty("tier", "vip");
            Assert.That(backend.Calls, Is.Empty);

            await reporter.InitializeAsync(TimeSpan.FromSeconds(5), expectedFailure: true);

            Assert.That(backend.Fatal, Is.True);
            Assert.That(backend.Calls, Is.EqualTo(new[]
            {
                "user:u1", "key:tier=vip", "log:level_start", "log:opened shop", "exception:boom", "key:level=3",
            }));
        }

        [Test]
        public async Task Reporter_WithoutBreadcrumbs_KeepsIdentity_ButLogsNoEvents()
        {
            var backend = new FakeBackend();
            var reporter = new CrashlyticsReporter(backend, breadcrumbsFromAnalytics: false);
            await reporter.InitializeAsync(TimeSpan.FromSeconds(5), expectedFailure: true);

            reporter.Track(new AnalyticsEvent("level_start"));
            reporter.TrackAdRevenue(new AdRevenue { Placement = "inter" });
            reporter.SetUserId("u1");
            reporter.Log("manual");

            Assert.That(backend.Calls, Is.EqualTo(new[] { "user:u1", "log:manual" }));
        }
    }
}
