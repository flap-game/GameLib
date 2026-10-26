
namespace FlapGame.GameLib.Haptics
{
    /// <summary>
    /// No-op backend used in the Editor and on platforms without vibration support.
    /// Keeps gameplay code branch-free — calls are simply ignored.
    /// </summary>
    public sealed class NullHapticBackend : IHapticBackend
    {
        public bool IsSupported => false;
        public void Vibrate(float durationMs, float intensity, int count, float gapMs) { }
        public void Play(HapticType type) { }
        public void Cancel() { }
    }
}
