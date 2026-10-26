namespace FlapGame.GameLib.Haptics
{
    /// <summary>
    /// Raw parameters a <see cref="HapticType"/> maps to when a backend has no dedicated native
    /// preset (e.g. Android, or iOS Core Haptics fallback). Durations/intensities are tuned to
    /// roughly match the feel of the iOS UIFeedbackGenerator equivalents.
    /// </summary>
    public readonly struct HapticPreset
    {
        public readonly float DurationMs;
        public readonly float Intensity;
        public readonly int Count;
        public readonly float GapMs;

        public HapticPreset(float durationMs, float intensity, int count, float gapMs)
        {
            DurationMs = durationMs;
            Intensity = intensity;
            Count = count;
            GapMs = gapMs;
        }

        /// <summary>Returns the raw vibration parameters approximating the given semantic preset.</summary>
        public static HapticPreset For(HapticType type)
        {
            switch (type)
            {
                case HapticType.Selection:   return new HapticPreset(10f, 0.4f, 1, 0f);
                case HapticType.LightImpact: return new HapticPreset(15f, 0.45f, 1, 0f);
                case HapticType.MediumImpact:return new HapticPreset(25f, 0.7f, 1, 0f);
                case HapticType.HeavyImpact: return new HapticPreset(40f, 1f, 1, 0f);
                case HapticType.RigidImpact: return new HapticPreset(18f, 0.9f, 1, 0f);
                case HapticType.SoftImpact:  return new HapticPreset(35f, 0.5f, 1, 0f);
                case HapticType.Success:     return new HapticPreset(20f, 0.6f, 2, 90f);
                case HapticType.Warning:     return new HapticPreset(35f, 0.85f, 2, 120f);
                case HapticType.Error:       return new HapticPreset(45f, 1f, 3, 100f);
                default:                     return new HapticPreset(25f, 0.7f, 1, 0f);
            }
        }
    }
}
