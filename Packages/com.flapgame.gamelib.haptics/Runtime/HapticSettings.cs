using System;
using UnityEngine;

namespace FlapGame.GameLib.Haptics
{
    [Serializable]
    public sealed class HapticSettings
    {
        [Tooltip("Whether haptics are on by default (before the player overrides via a settings toggle).")]
        public bool DefaultEnabled = true;

        [Tooltip("Persist the enabled state across sessions (via PlayerPrefs). When off, IsEnabled resets to DefaultEnabled each launch.")]
        public bool Persistent = true;

        [Tooltip("Multiplier applied to every vibration's intensity (0..1). Use to globally tone haptics up/down.")]
        [Range(0f, 1f)] public float GlobalIntensityScale = 1f;

        [Tooltip("Maximum single-buzz duration allowed by Vibrate(), in milliseconds (safety clamp).")]
        [Range(1f, 10000f)] public float MaxDurationMs = 3000f;
    }
}
