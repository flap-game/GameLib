using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;

namespace FlapGame.GameLib.Firebase
{
    /// <summary>
    /// The one <c>FirebaseApp.CheckAndFixDependenciesAsync</c> of the app (Firebase refuses every other call while a
    /// check runs). A check that ended without <c>Available</c>, or failed, is retried by the next caller.
    /// </summary>
    public static class FirebaseDependencies
    {
        private static readonly object Gate = new();
        private static Task<global::Firebase.DependencyStatus> check;

        public static Task<global::Firebase.DependencyStatus> CheckAndFixAsync()
        {
            lock (Gate)
            {
                Task<global::Firebase.DependencyStatus> current = check;
                if (current == null || current.IsFaulted || current.IsCanceled ||
                    (current.IsCompleted && current.Result != global::Firebase.DependencyStatus.Available))
                    check = current = global::Firebase.FirebaseApp.CheckAndFixDependenciesAsync();
                return current;
            }
        }

        /// <summary>Dependency check, then the default app (which throws without google-services.json / GoogleService-Info.plist).</summary>
        public static async Task EnsureAppAsync(CancellationToken cancellationToken)
        {
            global::Firebase.DependencyStatus status = await CheckAndFixAsync();
            if (status != global::Firebase.DependencyStatus.Available)
                throw new InvalidOperationException($"Firebase dependency check returned {status}");
            cancellationToken.ThrowIfCancellationRequested();

            if (global::Firebase.FirebaseApp.DefaultInstance == null)
                throw new InvalidOperationException("FirebaseApp.DefaultInstance is null");
        }

        // Enter Play Mode without a domain reload keeps statics; start every Play session from scratch.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlayMode()
        {
            lock (Gate)
                check = null;
        }

        // AfterAssembliesLoaded runs before GameLibAutoStart (BeforeSceneLoad), so every module sees the SDK.
        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register() => FirebaseStartup.Initializer = EnsureAppAsync;
    }
}
