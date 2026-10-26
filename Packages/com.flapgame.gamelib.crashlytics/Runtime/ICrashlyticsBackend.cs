using System;
using System.Threading;
using System.Threading.Tasks;

namespace FlapGame.GameLib.Crashlytics
{
    /// <summary>
    /// The Crashlytics calls GameLib makes, behind an interface so the reporter is testable without the SDK. The real one
    /// lives in <c>FlapGame.GameLib.Crashlytics.Sdk</c> and registers itself in <see cref="CrashlyticsBackend.Current"/>
    /// when the Firebase Crashlytics package is installed.
    /// </summary>
    public interface ICrashlyticsBackend
    {
        /// <summary>Starts Firebase (shared start) and turns collection on; throws when Firebase cannot run.</summary>
        Task InitializeAsync(bool reportUncaughtExceptionsAsFatal, CancellationToken cancellationToken);

        void Log(string message);

        void LogException(Exception exception);

        void SetCustomKey(string key, string value);

        void SetUserId(string userId);
    }

    public static class CrashlyticsBackend
    {
        /// <summary>Set before the first scene loads when the SDK is compiled in; null otherwise.</summary>
        public static ICrashlyticsBackend Current { get; set; }
    }
}
