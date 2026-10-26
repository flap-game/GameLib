using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FlapGame.GameLib.Ads.Tests
{
    internal sealed class FakeIabStore : IIabPrivacyStore
    {
        public readonly Dictionary<string, string> Values = new();

        public FakeIabStore Set(string key, string value)
        {
            Values[key] = value;
            return this;
        }

        public string Get(string key) => Values.TryGetValue(key, out string v) ? v : null;
    }

    /// <summary>0.6.1 review fixes: consent from the TCF purposes, routes without a unit, late provider init.</summary>
    public class AdsConsentRouteTests
    {
        // ---------- consent ----------

        private static FakeIabStore Tcf(string gdprApplies, string purposes) =>
            new FakeIabStore().Set(IabPrivacy.KeyGdprApplies, gdprApplies).Set(IabPrivacy.KeyPurposeConsents, purposes);

        [Test]
        public void Consent_RefusedInUmp_IsNoConsent_ThoughStatusIsObtained()
        {
            ConsentState s = IabPrivacy.Map(ConsentStatus.Obtained, true, Tcf("1", "0000000000"));
            Assert.That(s.CanRequestAds, Is.True, "UMP still allows (non-personalised) ads");
            Assert.That(s.GdprApplies, Is.True);
            Assert.That(s.HasUserConsent, Is.False);
        }

        [Test]
        public void Consent_NeedsPurposes134()
        {
            Assert.That(IabPrivacy.Map(ConsentStatus.Obtained, true, Tcf("1", "1111111111")).HasUserConsent, Is.True);
            Assert.That(IabPrivacy.Map(ConsentStatus.Obtained, true, Tcf("1", "1011")).HasUserConsent, Is.True);
            Assert.That(IabPrivacy.Map(ConsentStatus.Obtained, true, Tcf("1", "1100000000")).HasUserConsent, Is.False, "purpose 3 missing");
            Assert.That(IabPrivacy.Map(ConsentStatus.Obtained, true, Tcf("1", "1")).HasUserConsent, Is.False, "string too short");
        }

        [Test]
        public void Consent_WithoutTcf_FallsBackOnStatus_NeverClaimingConsent()
        {
            var empty = new FakeIabStore();
            ConsentState obtained = IabPrivacy.Map(ConsentStatus.Obtained, true, empty);
            Assert.That(obtained.GdprApplies, Is.True);
            Assert.That(obtained.HasUserConsent, Is.False, "Obtained alone cannot tell consent from refusal");

            ConsentState notRequired = IabPrivacy.Map(ConsentStatus.NotRequired, true, empty);
            Assert.That(notRequired.GdprApplies, Is.False);
            Assert.That(notRequired.HasUserConsent, Is.True);

            ConsentState unknown = IabPrivacy.Map(ConsentStatus.Unknown, true, empty);
            Assert.That(unknown.GdprApplies, Is.False);
            Assert.That(unknown.HasUserConsent, Is.False, "nothing asked yet");
        }

        [Test]
        public void Consent_TcfGdprApplies_WinsOverStatus()
        {
            // Offline launch: UMP Update failed (Unknown) but last session's TCF string says GDPR applies.
            ConsentState s = IabPrivacy.Map(ConsentStatus.Unknown, true, Tcf("1", "1111111111"));
            Assert.That(s.GdprApplies, Is.True);
            Assert.That(s.HasUserConsent, Is.True);

            // A CMP that ran outside the EEA writes gdprApplies = 0.
            s = IabPrivacy.Map(ConsentStatus.Unknown, true, Tcf("0", null));
            Assert.That(s.GdprApplies, Is.False);
            Assert.That(s.HasUserConsent, Is.True);
        }

        [Test]
        public void Consent_UsPrivacyString_SetsDoNotSell()
        {
            Assert.That(IabPrivacy.Parse(null, null, "1YYN").DoNotSell, Is.True);
            Assert.That(IabPrivacy.Parse(null, null, "1YNN").DoNotSell, Is.False);
            Assert.That(IabPrivacy.Parse(null, null, "1---").DoNotSell, Is.Null);
            Assert.That(IabPrivacy.Parse(null, null, null).DoNotSell, Is.Null);
        }

        [Test]
        public void Installer_MapConsent_ReadsTcf()
        {
            var ump = new FakeConsent("ump", null) { StatusValue = ConsentStatus.Obtained, CanRequest = true };
            Assert.That(AdsModule.MapConsent(ump, Tcf("1", "0000000000")).HasUserConsent, Is.False);
            Assert.That(AdsModule.MapConsent(ump, Tcf("1", "1111111111")).HasUserConsent, Is.True);
            Assert.That(AdsModule.MapConsent(null, new FakeIabStore()).HasUserConsent, Is.True, "no CMP = not required");
        }

        // ---------- routes / init ----------

        private FakeAdsAdapter adMob;
        private FakeAdsAdapter max;
        private AdsModule settings;

        [SetUp]
        public void SetUp()
        {
            adMob    = new FakeAdsAdapter(AdProviderIds.AdMob);
            max      = new FakeAdsAdapter(AdProviderIds.Max);
            settings = ScriptableObject.CreateInstance<AdsModule>();
            settings.Placements.Add(new AdPlacement
            {
                Id = "inter", Format = AdFormat.Interstitial,
                Routes = { new AdRoute("max", "m1", "mi1"), new AdRoute("admob", "a1", "ai1") },
            });
            settings.Placements.Add(new AdPlacement
            {
                // MAX has an iOS unit only: on this (non-iOS) platform it must not be the primary route.
                Id = "ios_only_max", Format = AdFormat.Interstitial,
                Routes = { new AdRoute("max", null, "mi2"), new AdRoute("admob", "a2", "ai2") },
            });
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(settings);

        private CompositeAdsService Create() => new(new IAdsProviderAdapter[] { adMob, max }, settings);

        [Test]
        public void Route_WithoutUnitForThisPlatform_IsSkipped_SoTheNextRouteLoads()
        {
            AdsTestPlatform.RequireAndroidUnitIds();
            using CompositeAdsService ads = Create();
            ads.InitializeAsync().AsTask().GetAwaiter().GetResult();

            Assert.That(ads.GetEffectiveRoutes("ios_only_max"), Is.EqualTo(new[] { "admob" }));
            Assert.That(adMob.Active, Does.Contain("ios_only_max"), "admob is the primary route here");
            Assert.That(max.Active, Has.No.Member("ios_only_max"));
            Assert.That(ads.GetEffectiveRoutes("inter"), Is.EqualTo(new[] { "max", "admob" }), "other placements unchanged");

            adMob.MarkReady("ios_only_max");
            Assert.That(ads.CanShow("ios_only_max", out AdBlockReason reason), Is.True, reason.ToString());
        }

        [Test]
        public async Task Init_TimedOutAdapter_JoinsWhenItFinishesLate()
        {
            max.InitGate = new TaskCompletionSource<bool>();
            using CompositeAdsService ads = Create();
            ads.InitializeTimeout = TimeSpan.FromMilliseconds(50);
            LogAssert.Expect(LogType.Error, new Regex("max failed to initialize: timed out"));

            await ads.InitializeAsync();
            Assert.That(ads.IsProviderInitialized("max"), Is.False);
            Assert.That(adMob.Active, Does.Contain("inter"), "admob covers while max is out");

            max.InitGate.SetResult(true);
            var watch = Stopwatch.StartNew();
            while (!ads.IsProviderInitialized("max") && watch.ElapsedMilliseconds < 2000) await Task.Delay(10);

            Assert.That(ads.IsProviderInitialized("max"), Is.True, "late init admitted");
            Assert.That(ads.GetEffectiveRoutes("inter"), Is.EqualTo(new[] { "max", "admob" }));
            Assert.That(max.Active, Does.Contain("inter"), "max is the primary route again");
        }

        [Test]
        public async Task Init_TimedOutAdapter_ThatFails_StaysDropped()
        {
            max.InitGate = new TaskCompletionSource<bool>();
            using CompositeAdsService ads = Create();
            ads.InitializeTimeout = TimeSpan.FromMilliseconds(50);
            LogAssert.Expect(LogType.Error, new Regex("max failed to initialize: timed out"));
            await ads.InitializeAsync();

            max.InitGate.SetException(new InvalidOperationException("late boom"));
            await Task.Delay(100);
            Assert.That(ads.IsProviderInitialized("max"), Is.False);
            Assert.That(ads.GetEffectiveRoutes("inter"), Is.EqualTo(new[] { "admob" }));
        }
    }
}
