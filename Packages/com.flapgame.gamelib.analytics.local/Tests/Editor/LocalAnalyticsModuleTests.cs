using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlapGame.GameLib.Analytics.Local;
using FlapGame.GameLib.Editor;
using NUnit.Framework;
using UnityEngine;

namespace FlapGame.GameLib.Tests
{
    /// <summary>Stands in for an online sink module: records what the suppression contract told it at install time.</summary>
    public sealed class SuppressionProbeModule : GameLibModule
    {
        public bool Installed;
        public bool SuppressOnline;
        public bool SuppressAttribution;

        public override int Order => GameLibModuleOrder.Analytics;

        public override ValueTask InstallAsync(IServiceRegistry services, CancellationToken cancellationToken = default)
        {
            Installed = true;
            SuppressOnline = LocalTracking.SuppressesOnline(services);
            SuppressAttribution = LocalTracking.SuppressesAttribution(services);
            return default;
        }
    }

    public class LocalAnalyticsModuleTests
    {
        // Nothing listens on port 9 (the discard port), so a post can only fail - the test never leaves the machine.
        private const string UnreachableEndpoint = "http://127.0.0.1:9/sdk/v1/tracking";

        private LocalAnalyticsModule module;
        private SuppressionProbeModule probe;

        [SetUp]
        public void SetUp()
        {
            GameLibBootstrap.Reset();
            module = ScriptableObject.CreateInstance<LocalAnalyticsModule>();
            module.Endpoint = UnreachableEndpoint;
            module.LanAddressPrefixes = new[] { LocalAnalyticsModule.DefaultLanAddressPrefix };
            probe = ScriptableObject.CreateInstance<SuppressionProbeModule>();
        }

        [TearDown]
        public void TearDown()
        {
            // Disposes the registry, which cancels the sink's post loop.
            GameLibBootstrap.Reset();
            Object.DestroyImmediate(module);
            Object.DestroyImmediate(probe);
        }

        private async Task<IServiceRegistry> Bootstrap() =>
            await GameLibBootstrap.InitializeAsync(new GameLibModule[] { probe, module }, logAnalytics: false);

        [Test]
        public async Task OnATestDevice_TheOnlineSinksAreToldToStayOff_AndTheLocalSinkIsRegistered()
        {
            module.TrackInEditor = true;

            IServiceRegistry services = await Bootstrap();

            Assert.That(module.IsActive, Is.True);
            Assert.That(probe.Installed, Is.True, "the probe module must still install");
            Assert.That(probe.SuppressOnline, Is.True, "an online sink must see the suppression - so local installs first");
            Assert.That(probe.SuppressAttribution, Is.False, "attribution keeps running by default, as in aframework");
            Assert.That(services.Resolve<IAnalytics>(), Is.TypeOf<AnalyticsService>());
            Assert.That(((AnalyticsService)services.Resolve<IAnalytics>()).Sinks.OfType<LocalAnalyticsSink>().Count(), Is.EqualTo(1));
            Assert.That(services.Resolve<ILocalTracking>(), Is.SameAs(module));
        }

        [Test]
        public async Task WithSuppressAttribution_TheAttributionSdksAreToldToStayOffToo()
        {
            module.TrackInEditor = true;
            module.SuppressAttribution = true;

            await Bootstrap();

            Assert.That(probe.SuppressOnline, Is.True);
            Assert.That(probe.SuppressAttribution, Is.True);
        }

        [Test]
        public async Task WithSuppressOnlineTrackingOff_BothTheLocalAndTheOnlineSinksRun()
        {
            module.TrackInEditor = true;
            module.SuppressOnlineTracking = false;

            IServiceRegistry services = await Bootstrap();

            Assert.That(module.IsActive, Is.True);
            Assert.That(((AnalyticsService)services.Resolve<IAnalytics>()).Sinks.OfType<LocalAnalyticsSink>().Count(), Is.EqualTo(1));
            Assert.That(probe.SuppressOnline, Is.False, "the online sink must install as well");
        }

        [Test]
        public async Task OffATestDevice_NothingChanges()
        {
            module.TrackInEditor = false; // the Editor branch with the toggle off: not a test device

            IServiceRegistry services = await Bootstrap();

            Assert.That(module.IsActive, Is.False);
            Assert.That(probe.SuppressOnline, Is.False);
            Assert.That(probe.SuppressAttribution, Is.False);
            Assert.That(((AnalyticsService)services.Resolve<IAnalytics>()).Sinks.OfType<LocalAnalyticsSink>(), Is.Empty);
            Assert.That(services.IsRegistered<ILocalTracking>(), Is.False, "nothing is registered in a normal build");
        }

        [Test]
        public async Task WithoutAnEndpoint_TheModuleStaysOff_WithAnError()
        {
            module.TrackInEditor = true;
            module.Endpoint = string.Empty;

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Endpoint is empty"));
            await Bootstrap();

            Assert.That(module.IsActive, Is.False);
            Assert.That(probe.SuppressOnline, Is.False);
        }

        [Test]
        public void Validate_ReportsABadSetup()
        {
            var issues = new List<GameLibIssue>();
            module.Endpoint = string.Empty;
            module.Validate(issues);
            Assert.That(issues.Any(i => i.Severity == GameLibIssueSeverity.Error && i.Message.Contains("Endpoint is empty")), Is.True);

            issues.Clear();
            module.Endpoint = "tools.puzzleclassic.com/sdk/v1/tracking"; // no scheme
            module.Validate(issues);
            Assert.That(issues.Any(i => i.Severity == GameLibIssueSeverity.Error && i.Message.Contains("not an http(s) URL")), Is.True);

            issues.Clear();
            module.Endpoint = LocalAnalyticsModule.DefaultEndpoint;
            module.LanAddressPrefixes = new string[0];
            module.TrackInEditor = false;
            module.Validate(issues);
            Assert.That(issues.Any(i => i.Severity == GameLibIssueSeverity.Warning && i.Message.Contains("can never turn on")), Is.True);

            issues.Clear();
            module.LanAddressPrefixes = new[] { LocalAnalyticsModule.DefaultLanAddressPrefix };
            module.Validate(issues);
            Assert.That(issues, Is.Empty, "the defaults are a valid setup");
        }

        [Test]
        public void TheDefaults_AreTheOnesAframeworkUsed()
        {
            var fresh = ScriptableObject.CreateInstance<LocalAnalyticsModule>();
            try
            {
                Assert.That(fresh.Endpoint, Is.EqualTo("https://tools.puzzleclassic.com/sdk/v1/tracking"));
                Assert.That(fresh.LanAddressPrefixes, Is.EqualTo(new[] { "192.25.20" }));
                Assert.That(fresh.TrackInEditor, Is.False);
                Assert.That(fresh.SuppressOnlineTracking, Is.True);
                Assert.That(fresh.SuppressAttribution, Is.False);
                Assert.That(fresh.MaxConsecutiveFailures, Is.EqualTo(10));
                Assert.That(fresh.Order, Is.LessThan(GameLibModuleOrder.Analytics), "must install before the online sinks");
            }
            finally
            {
                Object.DestroyImmediate(fresh);
            }
        }

        [Test]
        public void TheSetupCatalogEntry_PointsAtThisModule()
        {
            GameLibModuleInfo info = GameLibModuleCatalog.Find("analytics.local");

            Assert.That(info, Is.Not.Null, "GameLib > Setup must list the module");
            Assert.That(info.ModuleTypeName, Is.EqualTo(typeof(LocalAnalyticsModule).FullName));
            Assert.That(info.PackageName, Is.EqualTo("com.flapgame.gamelib.analytics.local"));
            Assert.That(info.Sdks.Select(s => s.PackageName),
                Is.EquivalentTo(new[] { "com.unity.modules.unitywebrequest", "com.unity.modules.androidjni" }));
        }
    }
}
