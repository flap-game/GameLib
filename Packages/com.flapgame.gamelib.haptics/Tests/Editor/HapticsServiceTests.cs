using NUnit.Framework;
using UnityEngine;

namespace FlapGame.GameLib.Haptics.Tests
{
    /// <summary>
    /// Unit tests for <see cref="HapticsService"/> logic — the enable toggle, persistence and value
    /// clamping/scaling. The native vibration itself can't run in the Editor, so a recording
    /// <see cref="FakeHapticBackend"/> stands in for the platform backend and captures what the
    /// service would have driven.
    /// </summary>
    [TestFixture]
    public class HapticsServiceTests
    {
        private const string EnabledKey = "GameLib.Haptics.Enabled";

        private FakeHapticBackend _backend;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(EnabledKey);
            _backend = new FakeHapticBackend { IsSupported = true };
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(EnabledKey);
        }

        private static HapticSettings Settings(
            bool defaultEnabled = true,
            bool persistent = true,
            float globalIntensity = 1f,
            float maxDurationMs = 3000f)
        {
            var s = new HapticSettings();
            s.DefaultEnabled = defaultEnabled;
            s.Persistent = persistent;
            s.GlobalIntensityScale = globalIntensity;
            s.MaxDurationMs = maxDurationMs;
            return s;
        }

        private HapticsService Create(HapticSettings settings = null)
            => new HapticsService(settings ?? Settings(), _backend);

        // ── Constructor / IsSupported ─────────────────────────────────────────

        [Test]
        public void Constructor_NullSettings_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => new HapticsService(null, _backend));
        }

        [Test]
        public void IsSupported_ReflectsBackend()
        {
            _backend.IsSupported = false;
            HapticsService sut = Create();
            Assert.IsFalse(sut.IsSupported);
        }

        [Test]
        public void Constructor_DefaultEnabled_IsRespectedWhenNothingPersisted()
        {
            HapticsService enabled = new HapticsService(Settings(defaultEnabled: true), _backend);
            HapticsService disabled = new HapticsService(Settings(defaultEnabled: false), _backend);
            Assert.IsTrue(enabled.IsEnabled);
            Assert.IsFalse(disabled.IsEnabled);
        }

        // ── Toggle gating ─────────────────────────────────────────────────────

        [Test]
        public void Vibrate_WhenEnabled_CallsBackend()
        {
            HapticsService sut = Create();
            sut.Vibrate(40f, 1f, 1);
            Assert.AreEqual(1, _backend.VibrateCalls);
        }

        [Test]
        public void Vibrate_WhenDisabled_DoesNotCallBackend()
        {
            HapticsService sut = Create();
            sut.IsEnabled = false;
            sut.Vibrate(40f, 1f, 1);
            Assert.AreEqual(0, _backend.VibrateCalls);
        }

        [Test]
        public void Play_WhenDisabled_DoesNotCallBackend()
        {
            HapticsService sut = Create();
            sut.IsEnabled = false;
            sut.Play(HapticType.MediumImpact);
            Assert.AreEqual(0, _backend.PlayCalls);
        }

        [Test]
        public void Vibrate_WhenBackendUnsupported_DoesNotCallBackend()
        {
            _backend.IsSupported = false;
            HapticsService sut = Create();
            sut.Vibrate(40f, 1f, 1);
            Assert.AreEqual(0, _backend.VibrateCalls);
        }

        [Test]
        public void Disabling_CancelsOngoingVibration()
        {
            HapticsService sut = Create();
            sut.IsEnabled = false;
            Assert.AreEqual(1, _backend.CancelCalls);
        }

        [Test]
        public void Cancel_AlwaysForwardsToBackend()
        {
            HapticsService sut = Create();
            sut.Cancel();
            Assert.AreEqual(1, _backend.CancelCalls);
        }

        // ── Persistence ───────────────────────────────────────────────────────

        [Test]
        public void IsEnabled_Setter_PersistsToPlayerPrefs()
        {
            HapticsService sut = Create();
            sut.IsEnabled = false;
            Assert.IsTrue(PlayerPrefs.HasKey(EnabledKey));
            Assert.AreEqual(0, PlayerPrefs.GetInt(EnabledKey));
        }

        [Test]
        public void Constructor_LoadsPersistedEnabledState()
        {
            PlayerPrefs.SetInt(EnabledKey, 0);
            HapticsService sut = new HapticsService(Settings(defaultEnabled: true), _backend);
            Assert.IsFalse(sut.IsEnabled, "Persisted 'off' should override DefaultEnabled=true.");
        }

        [Test]
        public void NonPersistentSettings_DoNotWriteOrReadPlayerPrefs()
        {
            HapticSettings settings = Settings(defaultEnabled: true, persistent: false);
            HapticsService sut = new HapticsService(settings, _backend);

            sut.IsEnabled = false;
            Assert.IsFalse(PlayerPrefs.HasKey(EnabledKey), "Non-persistent service must not write PlayerPrefs.");

            // A fresh non-persistent service ignores any stored value and falls back to DefaultEnabled.
            PlayerPrefs.SetInt(EnabledKey, 0);
            HapticsService fresh = new HapticsService(Settings(defaultEnabled: true, persistent: false), _backend);
            Assert.IsTrue(fresh.IsEnabled);
        }

        // ── Clamping / scaling ────────────────────────────────────────────────

        [Test]
        public void Vibrate_IntensityAboveOne_ClampsToOne()
        {
            HapticsService sut = Create();
            sut.Vibrate(40f, 5f, 1);
            Assert.AreEqual(1f, _backend.LastIntensity, 0.001f);
        }

        [Test]
        public void Vibrate_GlobalIntensityScale_IsApplied()
        {
            HapticsService sut = Create(Settings(globalIntensity: 0.5f));
            sut.Vibrate(40f, 1f, 1);
            Assert.AreEqual(0.5f, _backend.LastIntensity, 0.001f);
        }

        [Test]
        public void Vibrate_ZeroEffectiveIntensity_DoesNotCallBackend()
        {
            HapticsService sut = Create(Settings(globalIntensity: 0f));
            sut.Vibrate(40f, 1f, 1);
            Assert.AreEqual(0, _backend.VibrateCalls, "Zero intensity should be a no-op (no buzz).");
        }

        [Test]
        public void Vibrate_DurationAboveMax_ClampsToMaxDuration()
        {
            HapticsService sut = Create(Settings(maxDurationMs: 1000f));
            sut.Vibrate(999999f, 1f, 1);
            Assert.AreEqual(1000f, _backend.LastDurationMs, 0.001f);
        }

        [Test]
        public void Vibrate_CountBelowOne_ClampsToOne()
        {
            HapticsService sut = Create();
            sut.Vibrate(40f, 1f, 0);
            Assert.AreEqual(1, _backend.LastCount);
        }

        [Test]
        public void Vibrate_NegativeGap_ClampsToZero()
        {
            HapticsService sut = Create();
            sut.Vibrate(40f, 1f, 2, -50f);
            Assert.AreEqual(0f, _backend.LastGapMs, 0.001f);
        }

        // ── Play (semantic presets) ───────────────────────────────────────────

        [Test]
        public void Play_WhenEnabled_ForwardsTypeToBackend()
        {
            HapticsService sut = Create();
            sut.Play(HapticType.Success);
            Assert.AreEqual(1, _backend.PlayCalls);
            Assert.AreEqual(HapticType.Success, _backend.LastType);
            Assert.AreEqual(0, _backend.VibrateCalls, "Play must reach the backend as a preset, not be flattened into Vibrate.");
        }

        [Test]
        public void Play_WhenBackendUnsupported_DoesNotCallBackend()
        {
            _backend.IsSupported = false;
            HapticsService sut = Create();
            sut.Play(HapticType.HeavyImpact);
            Assert.AreEqual(0, _backend.PlayCalls);
        }

        [Test]
        public void HapticPreset_EveryType_HasPlayableWaveform()
        {
            // Android (no predefined support / Warning / Error) and the iOS Core Haptics fallback
            // both drive the raw waveform from HapticPreset.For; a zero duration or count would be a silent no-op.
            foreach (HapticType type in System.Enum.GetValues(typeof(HapticType)))
            {
                HapticPreset p = HapticPreset.For(type);
                Assert.Greater(p.DurationMs, 0f, $"{type}: DurationMs");
                Assert.GreaterOrEqual(p.Count, 1, $"{type}: Count");
                Assert.That(p.Intensity, Is.InRange(0f, 1f), $"{type}: Intensity");
                Assert.GreaterOrEqual(p.GapMs, 0f, $"{type}: GapMs");
            }
        }

        [Test]
        public void HapticSettings_HasNoUnreadSerializedField()
        {
            // DefaultDurationMs was removed because no code path read it (presets carry their own durations,
            // Vibrate takes an explicit one). A public field on a settings asset is a promise to the Inspector;
            // this pins the decision so it does not creep back without a reader.
            Assert.IsNull(typeof(HapticSettings).GetField("DefaultDurationMs"),
                "HapticSettings.DefaultDurationMs reintroduced — wire it into a preset or drop it again.");
        }

        // ── Play + GlobalIntensityScale (review fix) ──────────────────────────

        [Test]
        public void Play_GlobalIntensityZero_PlaysNothing()
        {
            var sut = Create(Settings(globalIntensity: 0f));
            sut.Play(HapticType.HeavyImpact);
            Assert.AreEqual(0, _backend.PlayCalls);
            Assert.AreEqual(0, _backend.VibrateCalls);
        }

        [Test]
        public void Play_GlobalIntensityPartial_UsesScaledPresetWaveform()
        {
            var sut = Create(Settings(globalIntensity: 0.5f));
            sut.Play(HapticType.HeavyImpact);
            var preset = HapticPreset.For(HapticType.HeavyImpact);
            Assert.AreEqual(0, _backend.PlayCalls, "native predefined effects cannot be scaled");
            Assert.AreEqual(1, _backend.VibrateCalls);
            Assert.AreEqual(preset.Intensity * 0.5f, _backend.LastIntensity, 1e-4f);
            Assert.AreEqual(preset.Count, _backend.LastCount);
        }

        [Test]
        public void Play_GlobalIntensityFull_UsesNativePreset()
        {
            var sut = Create(Settings(globalIntensity: 1f));
            sut.Play(HapticType.Success);
            Assert.AreEqual(1, _backend.PlayCalls);
            Assert.AreEqual(0, _backend.VibrateCalls);
        }

        // ── Recording fake backend ────────────────────────────────────────────

        private sealed class FakeHapticBackend : IHapticBackend
        {
            public bool IsSupported { get; set; } = true;

            public int VibrateCalls;
            public int PlayCalls;
            public int CancelCalls;

            public float LastDurationMs;
            public float LastIntensity;
            public int LastCount;
            public float LastGapMs;
            public HapticType LastType;

            public void Vibrate(float durationMs, float intensity, int count, float gapMs)
            {
                VibrateCalls++;
                LastDurationMs = durationMs;
                LastIntensity = intensity;
                LastCount = count;
                LastGapMs = gapMs;
            }

            public void Play(HapticType type)
            {
                PlayCalls++;
                LastType = type;
            }

            public void Cancel() => CancelCalls++;
        }
    }
}
