
namespace FlapGame.GameLib.Haptics
{
    /// <summary>
    /// Platform-specific vibration driver. One implementation per platform (Android, iOS, Null).
    /// <see cref="IHapticsService"/> owns the enable toggle and intensity scaling, then delegates the
    /// actual hardware calls here. Intensity values passed in are already scaled and clamped to 0..1.
    /// </summary>
    public interface IHapticBackend
    {
        bool IsSupported { get; }

        /// <param name="durationMs">Single-buzz length in ms (already clamped).</param>
        /// <param name="intensity">Final strength 0..1 (already scaled).</param>
        /// <param name="count">Buzz count (>=1).</param>
        /// <param name="gapMs">Gap between buzzes in ms.</param>
        void Vibrate(float durationMs, float intensity, int count, float gapMs);

        void Play(HapticType type);

        void Cancel();
    }
}
