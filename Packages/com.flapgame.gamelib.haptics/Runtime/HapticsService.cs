using UnityEngine;

namespace FlapGame.GameLib.Haptics
{
    /// <summary>
    /// Default <see cref="IHapticsService"/>. Owns the persisted enable toggle and global intensity
    /// scaling, then delegates the hardware calls to the platform <see cref="IHapticBackend"/>
    /// (Android / iOS / Null), selected at construction time.
    /// <para>
    /// Persistence: when <see cref="HapticSettings.Persistent"/> is on, <see cref="IsEnabled"/> is
    /// stored in <b>PlayerPrefs</b> under <c>"GameLib.Haptics.Enabled"</c> (int 0/1, saved immediately) -
    /// NOT through <c>ISaveService</c>, so it survives a save-data reset and is not covered by the
    /// File save backend. Delete that PlayerPrefs key to restore <see cref="HapticSettings.DefaultEnabled"/>.
    /// </para>
    /// </summary>
    public sealed class HapticsService : IHapticsService
    {
        private const string EnabledKey = "GameLib.Haptics.Enabled";

        private readonly IHapticBackend _backend;
        private readonly HapticSettings _settings;
        private bool _enabled;

        public HapticsService(HapticSettings settings = null, IHapticBackend backend = null)
        {
            _settings = settings ?? new HapticSettings();
            _backend = backend ?? CreateDefaultBackend();

            bool persisted = _settings.Persistent && PlayerPrefs.HasKey(EnabledKey);
            _enabled = persisted ? PlayerPrefs.GetInt(EnabledKey) != 0 : _settings.DefaultEnabled;
        }

        public bool IsSupported => _backend.IsSupported;

        public bool IsEnabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (_settings.Persistent)
                {
                    PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
                    PlayerPrefs.Save();
                }
                if (!value)
                    _backend.Cancel();
            }
        }

        public void Vibrate(float durationMs, float intensity = 1f, int count = 1, float gapMs = 60f)
        {
            if (!CanPlay()) return;

            float clampedDuration = Mathf.Clamp(durationMs, 1f, _settings.MaxDurationMs);
            float scaledIntensity = Mathf.Clamp01(intensity) * Mathf.Clamp01(_settings.GlobalIntensityScale);
            if (scaledIntensity <= 0f) return;

            int clampedCount = Mathf.Max(1, count);
            float clampedGap = Mathf.Max(0f, gapMs);

            _backend.Vibrate(clampedDuration, scaledIntensity, clampedCount, clampedGap);
        }

        /// <summary>
        /// Plays a preset. <see cref="HapticSettings.GlobalIntensityScale"/> applies here too:
        /// 0 mutes presets, 1 uses the platform's native effect (Android predefined / iOS feedback
        /// generator), anything in between plays the preset's waveform through <see cref="Vibrate"/> at
        /// scaled intensity - native predefined effects have no intensity parameter.
        /// </summary>
        public void Play(HapticType type)
        {
            if (!CanPlay()) return;

            float scale = Mathf.Clamp01(_settings.GlobalIntensityScale);
            if (scale <= 0f) return;
            if (scale >= 1f)
            {
                _backend.Play(type);
                return;
            }

            HapticPreset p = HapticPreset.For(type);
            Vibrate(p.DurationMs, p.Intensity, p.Count, p.GapMs);
        }

        public void Cancel() => _backend.Cancel();

        private bool CanPlay() => _enabled && _backend.IsSupported;

        private static IHapticBackend CreateDefaultBackend()
        {
#if UNITY_EDITOR
            return new NullHapticBackend();
#elif UNITY_ANDROID
            return new AndroidHapticBackend();
#elif UNITY_IOS
            return new IOSHapticBackend();
#else
            return new NullHapticBackend();
#endif
        }
    }
}
