#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;

namespace FlapGame.GameLib.Haptics
{
    /// <summary>
    /// Android backend driving the platform <c>Vibrator</c> via JNI.
    /// <list type="bullet">
    /// <item>API 26+ (Oreo): <c>VibrationEffect</c> for amplitude control and multi-buzz waveforms.</item>
    /// <item>API 31+ (S): semantic presets use <c>VibrationEffect.createPredefined</c> (CLICK / TICK / ...)
    /// when <c>Vibrator.areAllEffectsSupported</c> reports <c>VIBRATION_EFFECT_SUPPORT_YES</c>; otherwise the
    /// tuned waveform in <see cref="HapticPreset"/> is used.</item>
    /// <item>Pre-26: deprecated timed <c>vibrate(long, AudioAttributes)</c> (no amplitude).</item>
    /// </list>
    /// Every call passes explicit usage attributes: <c>VibrationAttributes.USAGE_MEDIA</c> on API 33+ and
    /// <c>AudioAttributes.USAGE_GAME</c> below (the framework maps USAGE_GAME to USAGE_MEDIA). Without them
    /// Android classifies any short effect as USAGE_TOUCH and silences it when the user turns off
    /// "Touch feedback" in system settings — the game's own haptics toggle must be the only gate.
    /// Requires the <c>android.permission.VIBRATE</c> permission in the app manifest.
    /// </summary>
    public sealed class AndroidHapticBackend : IHapticBackend
    {
        private const int ApiOreo = 26;     // VibrationEffect, vibrate(VibrationEffect, AudioAttributes)
        private const int ApiS = 31;        // areAllEffectsSupported (API 30) + explicit usage honoured for predefined effects
        private const int ApiTiramisu = 33; // VibrationAttributes.createForUsage, vibrate(VibrationEffect, VibrationAttributes)

        private const int VibrationEffectSupportYes = 1; // Vibrator.VIBRATION_EFFECT_SUPPORT_YES

        private readonly AndroidJavaObject _vibrator;
        private readonly bool _hasVibrator;
        private readonly bool _hasAmplitudeControl;
        private readonly int _apiLevel;

        // Immutable attribute objects, built once. Either may be null when construction failed;
        // the vibrate calls degrade to the attribute-less overload in that case.
        private readonly AndroidJavaObject _vibrationAttributes; // android.os.VibrationAttributes (API 33+)
        private readonly AndroidJavaObject _audioAttributes;     // android.media.AudioAttributes  (API 21+)

        // Predefined effect ids (VibrationEffect.EFFECT_*), read reflectively so a missing constant
        // disables the predefined path instead of vibrating the wrong effect.
        private readonly bool _predefinedSupported;
        private readonly int _effectTick;
        private readonly int _effectClick;
        private readonly int _effectHeavyClick;
        private readonly int _effectDoubleClick;

        public AndroidHapticBackend()
        {
            try
            {
                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                _apiLevel = version.GetStatic<int>("SDK_INT");

                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");

                // VIBRATOR_SERVICE works through current API levels; simpler than VibratorManager (API 31+).
                _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");

                if (_vibrator != null)
                {
                    _hasVibrator = _vibrator.Call<bool>("hasVibrator");
                    if (_apiLevel >= ApiOreo)
                        _hasAmplitudeControl = _vibrator.Call<bool>("hasAmplitudeControl");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Haptics] Android vibrator init failed: {e.Message}");
                _vibrator = null;
                _hasVibrator = false;
            }

            if (!IsSupported) return;

            _audioAttributes = TryCreateAudioAttributes();
            if (_apiLevel >= ApiTiramisu)
                _vibrationAttributes = TryCreateVibrationAttributes();

            if (_apiLevel >= ApiS)
            {
                _predefinedSupported = TryQueryPredefinedSupport(
                    out _effectTick, out _effectClick, out _effectHeavyClick, out _effectDoubleClick);
            }
        }

        public bool IsSupported => _hasVibrator && _vibrator != null;

        public void Vibrate(float durationMs, float intensity, int count, float gapMs)
        {
            if (!IsSupported) return;

            long dur = (long)durationMs;
            long gap = (long)gapMs;

            try
            {
                if (_apiLevel >= ApiOreo)
                    VibrateWithEffect(dur, intensity, count, gap);
                else
                    VibrateLegacy(dur, count, gap);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Haptics] Android vibrate failed: {e.Message}");
            }
        }

        public void Play(HapticType type)
        {
            if (!IsSupported) return;

            if (_predefinedSupported && TryMapPredefined(type, out int effectId))
            {
                try
                {
                    using var effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                    using AndroidJavaObject effect = effectClass.CallStatic<AndroidJavaObject>("createPredefined", effectId);
                    VibrateEffect(effect);
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Haptics] Android predefined effect failed, using waveform: {e.Message}");
                }
            }

            HapticPreset p = HapticPreset.For(type);
            Vibrate(p.DurationMs, p.Intensity, p.Count, p.GapMs);
        }

        public void Cancel()
        {
            if (!IsSupported) return;
            try { _vibrator.Call("cancel"); }
            catch (Exception e) { Debug.LogWarning($"[Haptics] Android cancel failed: {e.Message}"); }
        }

        // ── Attributes ──────────────────────────────────────────────────────────

        /// <summary>AudioAttributes(USAGE_GAME, CONTENT_TYPE_SONIFICATION) — API 21+.</summary>
        private static AndroidJavaObject TryCreateAudioAttributes()
        {
            try
            {
                using var attrClass = new AndroidJavaClass("android.media.AudioAttributes");
                int usageGame = attrClass.GetStatic<int>("USAGE_GAME");
                int contentSonification = attrClass.GetStatic<int>("CONTENT_TYPE_SONIFICATION");

                using var builder = new AndroidJavaObject("android.media.AudioAttributes$Builder");
                using AndroidJavaObject b1 = builder.Call<AndroidJavaObject>("setUsage", usageGame);
                using AndroidJavaObject b2 = b1.Call<AndroidJavaObject>("setContentType", contentSonification);
                return b2.Call<AndroidJavaObject>("build");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Haptics] Android AudioAttributes unavailable: {e.Message}");
                return null;
            }
        }

        /// <summary>VibrationAttributes.createForUsage(USAGE_MEDIA) — API 33+. USAGE_MEDIA is what the framework
        /// maps AudioAttributes.USAGE_GAME to; VibrationAttributes has no game-specific usage.</summary>
        private static AndroidJavaObject TryCreateVibrationAttributes()
        {
            try
            {
                using var attrClass = new AndroidJavaClass("android.os.VibrationAttributes");
                int usageMedia = attrClass.GetStatic<int>("USAGE_MEDIA");
                return attrClass.CallStatic<AndroidJavaObject>("createForUsage", usageMedia);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Haptics] Android VibrationAttributes unavailable: {e.Message}");
                return null;
            }
        }

        /// <summary>Dispatches to the most specific vibrate overload available for this API level.</summary>
        private void VibrateEffect(AndroidJavaObject effect)
        {
            if (_vibrationAttributes != null)
                _vibrator.Call("vibrate", effect, _vibrationAttributes);      // API 33+
            else if (_audioAttributes != null)
                _vibrator.Call("vibrate", effect, _audioAttributes);          // API 26–32 (deprecated in 33, still present)
            else
                _vibrator.Call("vibrate", effect);
        }

        // ── Predefined effects (API 31+) ────────────────────────────────────────

        private bool TryQueryPredefinedSupport(out int tick, out int click, out int heavyClick, out int doubleClick)
        {
            tick = click = heavyClick = doubleClick = 0;
            try
            {
                using var effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                tick = effectClass.GetStatic<int>("EFFECT_TICK");
                click = effectClass.GetStatic<int>("EFFECT_CLICK");
                heavyClick = effectClass.GetStatic<int>("EFFECT_HEAVY_CLICK");
                doubleClick = effectClass.GetStatic<int>("EFFECT_DOUBLE_CLICK");

                // areAllEffectsSupported(int... effectIds) — varargs marshal as one int[] argument.
                // Wrapped in object[] on purpose: passing the int[] bare binds to the void
                // Call<T>(string, params T[]) overload instead of the int-returning one.
                object[] args = { new[] { tick, click, heavyClick, doubleClick } };
                int support = _vibrator.Call<int>("areAllEffectsSupported", args);
                return support == VibrationEffectSupportYes;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Haptics] Android predefined effect query failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Presets with a clear platform analogue. Warning / Error have no predefined counterpart
        /// (multi-buzz at distinct strengths) and keep the <see cref="HapticPreset"/> waveform.
        /// </summary>
        private bool TryMapPredefined(HapticType type, out int effectId)
        {
            switch (type)
            {
                case HapticType.Selection:    effectId = _effectTick;        return true;
                case HapticType.LightImpact:  effectId = _effectTick;        return true;
                case HapticType.SoftImpact:   effectId = _effectTick;        return true;
                case HapticType.MediumImpact: effectId = _effectClick;       return true;
                case HapticType.RigidImpact:  effectId = _effectClick;       return true;
                case HapticType.HeavyImpact:  effectId = _effectHeavyClick;  return true;
                case HapticType.Success:      effectId = _effectDoubleClick; return true;
                default:                      effectId = 0;                  return false;
            }
        }

        // ── API 26+ : VibrationEffect ───────────────────────────────────────────

        private void VibrateWithEffect(long durationMs, float intensity, int count, long gapMs)
        {
            // Amplitude 1..255; DEFAULT_AMPLITUDE (-1) when the device can't scale strength.
            int amplitude = _hasAmplitudeControl
                ? Mathf.Clamp(Mathf.RoundToInt(intensity * 255f), 1, 255)
                : -1; // VibrationEffect.DEFAULT_AMPLITUDE

            AndroidJavaObject effect = count <= 1
                ? CreateOneShot(durationMs, amplitude)
                : CreateWaveform(durationMs, amplitude, count, gapMs);

            using (effect)
                VibrateEffect(effect);
        }

        private static AndroidJavaObject CreateOneShot(long durationMs, int amplitude)
        {
            using var effectClass = new AndroidJavaClass("android.os.VibrationEffect");
            return effectClass.CallStatic<AndroidJavaObject>("createOneShot", durationMs, amplitude);
        }

        private static AndroidJavaObject CreateWaveform(long durationMs, int amplitude, int count, long gapMs)
        {
            // timings/amplitudes: [wait, on, off, on, off, ..., on]; length = 2*count.
            int n = count * 2;
            long[] timings = new long[n];
            int[] amplitudes = new int[n];

            timings[0] = 0;          // no initial delay
            amplitudes[0] = 0;
            timings[1] = durationMs; // first buzz
            amplitudes[1] = amplitude;

            for (int i = 1; i < count; i++)
            {
                int off = i * 2;
                timings[off] = gapMs;          // silence
                amplitudes[off] = 0;
                timings[off + 1] = durationMs; // buzz
                amplitudes[off + 1] = amplitude;
            }

            using var effectClass = new AndroidJavaClass("android.os.VibrationEffect");
            // createWaveform(long[] timings, int[] amplitudes, int repeat = -1)
            return effectClass.CallStatic<AndroidJavaObject>("createWaveform", timings, amplitudes, -1);
        }

        // ── Pre-26 : deprecated timed vibrate (no amplitude) ────────────────────

        private void VibrateLegacy(long durationMs, int count, long gapMs)
        {
            if (count <= 1)
            {
                // vibrate(long milliseconds, AudioAttributes attributes) — API 21+
                if (_audioAttributes != null) _vibrator.Call("vibrate", durationMs, _audioAttributes);
                else _vibrator.Call("vibrate", durationMs);
                return;
            }

            int n = count * 2;
            long[] pattern = new long[n];
            pattern[0] = 0;
            pattern[1] = durationMs;
            for (int i = 1; i < count; i++)
            {
                pattern[i * 2] = gapMs;
                pattern[i * 2 + 1] = durationMs;
            }
            // vibrate(long[] pattern, int repeat = -1, AudioAttributes attributes) — API 21+
            if (_audioAttributes != null) _vibrator.Call("vibrate", pattern, -1, _audioAttributes);
            else _vibrator.Call("vibrate", pattern, -1);
        }
    }
}
#endif
