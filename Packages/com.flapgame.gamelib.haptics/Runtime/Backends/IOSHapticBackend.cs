#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;

namespace FlapGame.GameLib.Haptics
{
    /// <summary>
    /// iOS backend backed by the native <c>GameLibHaptics.mm</c> plugin.
    /// Semantic presets use <c>UIFeedbackGenerator</c> (Taptic Engine, iOS 10+); custom
    /// <see cref="Vibrate"/> calls use Core Haptics (<c>CHHapticEngine</c>, iOS 13+) for real
    /// duration/intensity/count control, gracefully degrading to Taptic on older devices.
    /// </summary>
    public sealed class IOSHapticBackend : IHapticBackend
    {
        [DllImport("__Internal")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool _GameLibHaptics_IsSupported();
        [DllImport("__Internal")] private static extern void _GameLibHaptics_Vibrate(float durationMs, float intensity, int count, float gapMs);
        [DllImport("__Internal")] private static extern void _GameLibHaptics_Impact(int style);
        [DllImport("__Internal")] private static extern void _GameLibHaptics_Notification(int type);
        [DllImport("__Internal")] private static extern void _GameLibHaptics_Selection();
        [DllImport("__Internal")] private static extern void _GameLibHaptics_Cancel();
        [DllImport("__Internal")] private static extern void _GameLibHaptics_Prepare();

        // UIImpactFeedbackStyle ordinals matched in the .mm: light, medium, heavy, soft, rigid.
        private const int StyleLight = 0;
        private const int StyleMedium = 1;
        private const int StyleHeavy = 2;
        private const int StyleSoft = 3;
        private const int StyleRigid = 4;

        // UINotificationFeedbackType ordinals.
        private const int NotifySuccess = 0;
        private const int NotifyWarning = 1;
        private const int NotifyError = 2;

        /// <summary>
        /// Warms the Taptic Engine once at startup: the native side creates its UIFeedbackGenerators
        /// and calls prepare() on each, so the first Play() has no cold-start latency.
        /// </summary>
        public IOSHapticBackend()
        {
            if (_GameLibHaptics_IsSupported())
                _GameLibHaptics_Prepare();
        }

        /// <summary>True only on hardware with a Taptic Engine (CHHapticEngine.capabilitiesForHardware; iPad = false).</summary>
        public bool IsSupported => _GameLibHaptics_IsSupported();

        public void Vibrate(float durationMs, float intensity, int count, float gapMs)
            => _GameLibHaptics_Vibrate(durationMs, intensity, count, gapMs);

        public void Play(HapticType type)
        {
            switch (type)
            {
                case HapticType.Selection:    _GameLibHaptics_Selection(); break;
                case HapticType.LightImpact:  _GameLibHaptics_Impact(StyleLight); break;
                case HapticType.MediumImpact: _GameLibHaptics_Impact(StyleMedium); break;
                case HapticType.HeavyImpact:  _GameLibHaptics_Impact(StyleHeavy); break;
                case HapticType.RigidImpact:  _GameLibHaptics_Impact(StyleRigid); break;
                case HapticType.SoftImpact:   _GameLibHaptics_Impact(StyleSoft); break;
                case HapticType.Success:      _GameLibHaptics_Notification(NotifySuccess); break;
                case HapticType.Warning:      _GameLibHaptics_Notification(NotifyWarning); break;
                case HapticType.Error:        _GameLibHaptics_Notification(NotifyError); break;
            }
        }

        public void Cancel() => _GameLibHaptics_Cancel();
    }
}
#endif
