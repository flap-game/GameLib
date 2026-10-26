using System;
using System.Threading.Tasks;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
using AOT;
#endif

namespace FlapGame.GameLib.Att
{
    /// <summary>iOS App Tracking Transparency: the status, and the system prompt. Other platforms: not applicable.</summary>
    public interface IAttPrompt
    {
        AttStatus Status { get; }

        /// <summary>Shows the system prompt when the status is not determined; completes with the resulting status.</summary>
        Task<AttStatus> RequestAsync();
    }

    /// <summary>
    /// <see cref="IAttPrompt"/> over <c>GameLibAtt.mm</c> (ATTrackingManager). The Info.plist usage text and the weak
    /// AppTrackingTransparency link are added at build time by the ATT module's iOS post-processor (AttIosPostProcessor).
    /// </summary>
    public sealed class AttPrompt : IAttPrompt
    {
#if UNITY_IOS && !UNITY_EDITOR
        private delegate void StatusCallback(int status);

        [DllImport("__Internal")] private static extern int _GameLibAtt_GetStatus();
        [DllImport("__Internal")] private static extern void _GameLibAtt_Request(StatusCallback callback);

        private static TaskCompletionSource<AttStatus> pending;

        public AttStatus Status => FromNative(_GameLibAtt_GetStatus());

        public Task<AttStatus> RequestAsync()
        {
            if (pending != null) return pending.Task;
            pending = new TaskCompletionSource<AttStatus>();
            _GameLibAtt_Request(OnStatus);
            return pending.Task;
        }

        // The native side calls back on the main thread.
        [MonoPInvokeCallback(typeof(StatusCallback))]
        private static void OnStatus(int status)
        {
            TaskCompletionSource<AttStatus> request = pending;
            pending = null;
            request?.TrySetResult(FromNative(status));
        }
#else
        public AttStatus Status => AttStatus.NotApplicable;

        public Task<AttStatus> RequestAsync() => Task.FromResult(AttStatus.NotApplicable);
#endif

        /// <summary>ATTrackingManagerAuthorizationStatus (0 not determined, 1 restricted, 2 denied, 3 authorized; -1 = before iOS 14).</summary>
        public static AttStatus FromNative(int status) => status switch
        {
            0 => AttStatus.NotDetermined,
            1 => AttStatus.Restricted,
            2 => AttStatus.Denied,
            3 => AttStatus.Authorized,
            _ => AttStatus.NotApplicable,
        };
    }
}
