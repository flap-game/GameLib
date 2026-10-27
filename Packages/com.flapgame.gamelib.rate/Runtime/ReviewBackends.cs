using System;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Rate
{
    /// <summary>The store's own review sheet.</summary>
    public interface IReviewBackend
    {
        /// <summary>True when the sheet was requested; false when it is not available (the store page opens instead).</summary>
        Task<bool> RequestReviewAsync();
    }

    public static class ReviewBackends
    {
        /// <summary>StoreKit on iOS, Play In-App Review on Android, none elsewhere (the Editor logs).</summary>
        public static IReviewBackend ForPlatform()
        {
#if UNITY_EDITOR
            return new EditorReviewBackend();
#elif UNITY_IOS
            return new IosReviewBackend();
#elif UNITY_ANDROID
            return new AndroidReviewBackend();
#else
            return null;
#endif
        }
    }

    internal sealed class EditorReviewBackend : IReviewBackend
    {
        public Task<bool> RequestReviewAsync()
        {
            Debug.Log("[Rate] Editor: the store review sheet would open here.");
            return Task.FromResult(true);
        }
    }

#if UNITY_IOS && !UNITY_EDITOR
    internal sealed class IosReviewBackend : IReviewBackend
    {
        public Task<bool> RequestReviewAsync() => Task.FromResult(UnityEngine.iOS.Device.RequestStoreReview());
    }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// Google Play In-App Review through JNI (com.google.android.play:review, added to Gradle by the Rate editor
    /// package). Not available (no Play Store, library missing): false, and the store page opens.
    /// </summary>
    internal sealed class AndroidReviewBackend : IReviewBackend
    {
        public Task<bool> RequestReviewAsync()
        {
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var factory = new AndroidJavaClass("com.google.android.play.core.review.ReviewManagerFactory");
                var manager = factory.CallStatic<AndroidJavaObject>("create", activity);
                using AndroidJavaObject request = manager.Call<AndroidJavaObject>("requestReviewFlow");
                using AndroidJavaObject listening = request.Call<AndroidJavaObject>("addOnCompleteListener", new CompleteListener(task =>
                {
                    if (!task.Call<bool>("isSuccessful"))
                    {
                        Debug.LogWarning("[Rate] Play review flow not available.");
                        done.TrySetResult(false);
                        return;
                    }
                    using AndroidJavaObject info = task.Call<AndroidJavaObject>("getResult");
                    using AndroidJavaObject flow = manager.Call<AndroidJavaObject>("launchReviewFlow", activity, info);
                    // Completes when the sheet closed - or at once when Play decided not to show it (quota).
                    using AndroidJavaObject closing = flow.Call<AndroidJavaObject>("addOnCompleteListener", new CompleteListener(_ => done.TrySetResult(true)));
                }, () => done.TrySetResult(false)));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Rate] Play In-App Review unavailable: {e.Message}");
                done.TrySetResult(false);
            }
            return done.Task;
        }

        private sealed class CompleteListener : AndroidJavaProxy
        {
            private readonly Action<AndroidJavaObject> completed;
            private readonly Action onError;

            public CompleteListener(Action<AndroidJavaObject> onComplete, Action onError = null)
                : base("com.google.android.gms.tasks.OnCompleteListener")
            {
                completed = onComplete;
                this.onError = onError;
            }

            // Called by Play on the Android UI thread.
            public void onComplete(AndroidJavaObject task)
            {
                try { completed(task); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Rate] Play review callback failed: {e.Message}");
                    onError?.Invoke();
                }
            }
        }
    }
#endif
}
