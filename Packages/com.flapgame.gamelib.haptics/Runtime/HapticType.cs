namespace FlapGame.GameLib.Haptics
{
    /// <summary>
    /// Semantic haptic presets, mapped to the closest native feedback on each platform
    /// (iOS UIFeedbackGenerator / Core Haptics, Android predefined VibrationEffects).
    /// Use these for common UI/gameplay feedback; use <c>IHapticsService.Vibrate</c> when you
    /// need explicit control over duration, intensity and count.
    /// </summary>
    public enum HapticType
    {
        /// <summary>Subtle tick for selection changes (pickers, toggles). Lightest feedback.</summary>
        Selection = 0,

        /// <summary>Light impact — minor collisions, small UI taps.</summary>
        LightImpact = 1,

        /// <summary>Medium impact — standard button press / confirm.</summary>
        MediumImpact = 2,

        /// <summary>Heavy impact — strong hits, important events.</summary>
        HeavyImpact = 3,

        /// <summary>Sharp/snappy impact (iOS 13+ rigid; approximated elsewhere).</summary>
        RigidImpact = 4,

        /// <summary>Soft/dull impact (iOS 13+ soft; approximated elsewhere).</summary>
        SoftImpact = 5,

        /// <summary>Notification: success (e.g. level complete, reward granted).</summary>
        Success = 6,

        /// <summary>Notification: warning (e.g. low health, invalid action).</summary>
        Warning = 7,

        /// <summary>Notification: error (e.g. failed purchase, blocked move).</summary>
        Error = 8,
    }
}
