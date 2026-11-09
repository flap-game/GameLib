using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Analytics.Local
{
    /// <summary>
    /// The advertising id of this device, as aframework sent it: the IDFA on iOS (through Unity, after the ATT answer) and
    /// the Google advertising id on Android (through Play Services, which Unity 6 no longer wraps). Null when the platform
    /// has no id or the player opted out - <see cref="LocalAnalyticsSink"/> then falls back to
    /// <c>SystemInfo.deviceUniqueIdentifier</c>, so the rows of one device still join.
    /// </summary>
    public sealed class AdvertisingDeviceIdSource : ILocalDeviceIdSource
    {
        /// <summary>What both platforms return once the player opted out of ad tracking.</summary>
        public const string ZeroAdvertisingId = "00000000-0000-0000-0000-000000000000";

        /// <summary>How long the platform may take to answer before the fallback id is used.</summary>
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        public async Task<string> GetAsync(CancellationToken cancellationToken)
        {
            string id = await ReadAsync(cancellationToken);
            return string.IsNullOrEmpty(id) || id == ZeroAdvertisingId ? null : id;
        }

#if UNITY_IOS && !UNITY_EDITOR
        private static async Task<string> ReadAsync(CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool supported = Application.RequestAdvertisingIdentifierAsync((string advertisingId, bool trackingEnabled, string error) =>
            {
                if (!string.IsNullOrEmpty(error))
                    Debug.LogWarning($"{LocalAnalyticsModule.LogTag} The advertising id request answered with an error: {error}.");
                completion.TrySetResult(advertisingId);
            });

            // iOS and UWP only (Unity removed it for Android); on anything else nothing ever calls back.
            if (!supported) return null;

            using CancellationTokenRegistration registration = cancellationToken.Register(() => completion.TrySetCanceled());
            Task finished = await Task.WhenAny(completion.Task, Task.Delay(Timeout, cancellationToken));
            if (finished != completion.Task)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Debug.LogWarning($"{LocalAnalyticsModule.LogTag} No advertising id after {Timeout.TotalSeconds:0}s.");
                return null;
            }

            return await completion.Task;
        }
#elif UNITY_ANDROID && !UNITY_EDITOR
        private static Task<string> ReadAsync(CancellationToken cancellationToken) => Task.Run(ReadAndroidAdvertisingId, cancellationToken);

        /// <summary>
        /// Play Services' <c>AdvertisingIdClient.getAdvertisingIdInfo</c>. It throws when it is called on the main thread
        /// (and can block for a second), so it runs on a worker thread - which has to attach itself to the JVM first.
        /// </summary>
        private static string ReadAndroidAdvertisingId()
        {
            AndroidJNI.AttachCurrentThread();
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var client = new AndroidJavaClass("com.google.android.gms.ads.identifier.AdvertisingIdClient");
                using AndroidJavaObject info = client.CallStatic<AndroidJavaObject>("getAdvertisingIdInfo", activity);
                if (info == null) return null;
                if (info.Call<bool>("isLimitAdTrackingEnabled")) return null;
                return info.Call<string>("getId");
            }
            catch (Exception e)
            {
                // No Play Services, or the app does not depend on the ads identifier library.
                Debug.LogWarning($"{LocalAnalyticsModule.LogTag} No Google advertising id ({e.GetType().Name}: {e.Message}).");
                return null;
            }
            finally
            {
                AndroidJNI.DetachCurrentThread();
            }
        }
#else
        private static Task<string> ReadAsync(CancellationToken cancellationToken)
        {
            // Editor and the desktop players: there is no advertising id, the fallback device id is used.
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string>(null);
        }
#endif
    }
}
