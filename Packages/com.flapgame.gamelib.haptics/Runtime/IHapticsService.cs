
namespace FlapGame.GameLib.Haptics
{
    /// <summary>
    /// Game-facing haptics (vibration) control. Works on Android and iOS; a no-op everywhere else.
    /// Resolve from <c>GameLibBootstrap.Services</c>.
    /// </summary>
    /// <remarks>
    /// Two ways to trigger feedback:
    /// <list type="bullet">
    /// <item><see cref="Play"/> for semantic presets — easiest, recommended for UI/gameplay feedback.</item>
    /// <item><see cref="Vibrate"/> for explicit control over duration, intensity and repeat count.</item>
    /// </list>
    /// All calls are silently ignored when <see cref="IsEnabled"/> is <c>false</c> or the device
    /// has no vibrator / haptics hardware (<see cref="IsSupported"/>).
    /// </remarks>
    public interface IHapticsService
    {
        /// <summary>True when the current device exposes a usable vibrator / haptic engine.</summary>
        bool IsSupported { get; }

        /// <summary>
        /// Global on/off switch for all haptics, persisted across sessions. Defaults to the value in
        /// <c>HapticSettings</c>. Set to <c>false</c> to honor a player "Vibration" settings toggle.
        /// </summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// Triggers a custom vibration with explicit control. No-op if disabled/unsupported.
        /// </summary>
        /// <param name="durationMs">Length of a single buzz, in milliseconds (clamped to a sane range).</param>
        /// <param name="intensity">Strength in 0..1 (scaled by the global intensity in settings). On
        /// devices without amplitude control this only affects whether it buzzes (>0) or not.</param>
        /// <param name="count">Number of buzzes (>=1). Values &gt;1 produce a pattern of buzzes.</param>
        /// <param name="gapMs">Silent gap between buzzes when <paramref name="count"/> &gt; 1, in milliseconds.</param>
        void Vibrate(float durationMs, float intensity = 1f, int count = 1, float gapMs = 60f);

        /// <summary>Triggers a semantic preset, mapped to the best native feedback. No-op if disabled/unsupported.</summary>
        void Play(HapticType type);

        /// <summary>Stops any ongoing vibration immediately.</summary>
        void Cancel();
    }
}
