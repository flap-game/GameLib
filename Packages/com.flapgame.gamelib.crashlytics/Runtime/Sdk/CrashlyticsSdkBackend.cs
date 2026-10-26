using System;
using System.Threading;
using System.Threading.Tasks;
using FlapGame.GameLib.Firebase;
using UnityEngine;
using UnityEngine.Scripting;
using FC = global::Firebase.Crashlytics;

namespace FlapGame.GameLib.Crashlytics
{
    /// <summary>The real <see cref="ICrashlyticsBackend"/>. Compiled only with the Firebase Crashlytics package.</summary>
    public sealed class CrashlyticsSdkBackend : ICrashlyticsBackend
    {
        public async Task InitializeAsync(bool reportUncaughtExceptionsAsFatal, CancellationToken cancellationToken)
        {
            // Creating the default app is what starts Crashlytics.
            await FirebaseStartup.EnsureReadyAsync(cancellationToken);
            FC.Crashlytics.ReportUncaughtExceptionsAsFatal = reportUncaughtExceptionsAsFatal;
            FC.Crashlytics.IsCrashlyticsCollectionEnabled = true;
        }

        public void Log(string message) => FC.Crashlytics.Log(message);

        public void LogException(Exception exception) => FC.Crashlytics.LogException(exception);

        public void SetCustomKey(string key, string value) => FC.Crashlytics.SetCustomKey(key, value);

        public void SetUserId(string userId) => FC.Crashlytics.SetUserId(userId);

        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register() => CrashlyticsBackend.Current = new CrashlyticsSdkBackend();
    }
}
