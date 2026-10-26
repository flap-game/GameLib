using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlapGame.GameLib.Firebase;
using UnityEngine;

namespace FlapGame.GameLib.Crashlytics
{
    /// <summary>
    /// Registers <see cref="ICrashReporter"/> (Firebase Crashlytics) and adds it as an analytics sink, then starts Firebase
    /// in the background. Installs first among the modules: a crash before Firebase is up is not reported.
    /// </summary>
    [CreateAssetMenu(menuName = "GameLib/Modules/Crashlytics", fileName = "CrashlyticsModule")]
    public sealed class CrashlyticsModule : GameLibModule
    {
        [Tooltip("Seconds Firebase may take to start before a warning (0 = never warn). Calls stay buffered and are sent when it starts later.")]
        [Min(0f)] public float InitTimeoutSeconds = 30f;

        [Tooltip("Log every analytics event, ad revenue and purchase as a Crashlytics breadcrumb, so a report shows what the player did just before.")]
        public bool BreadcrumbsFromAnalytics = true;

        [Tooltip("Report uncaught C# exceptions as fatal crashes. OFF: they show as non-fatal issues (the game usually keeps running).")]
        public bool ReportUncaughtExceptionsAsFatal;

        public override int Order => GameLibModuleOrder.Firebase;

        public override ValueTask InstallAsync(IServiceRegistry services, CancellationToken cancellationToken = default)
        {
            ICrashlyticsBackend backend = CrashlyticsBackend.Current;
            if (backend == null)
            {
                Debug.LogWarning($"{CrashlyticsReporter.LogTag} The Firebase Crashlytics package is not installed - no crash reporting.");
                return default;
            }

            var reporter = new CrashlyticsReporter(backend, BreadcrumbsFromAnalytics, ReportUncaughtExceptionsAsFatal);
            services.Register<ICrashReporter>(reporter);
            // The sink also carries the shared user id and user properties (custom keys), so add it even without breadcrumbs.
            services.Resolve<IAnalyticsSinkRegistry>().AddSink(reporter);

            TimeSpan timeout = InitTimeoutSeconds > 0f ? TimeSpan.FromSeconds(InitTimeoutSeconds) : Timeout.InfiniteTimeSpan;
            _ = reporter.InitializeAsync(timeout, expectedFailure: Application.isEditor, cancellationToken);
            return default;
        }

        public override void Validate(ICollection<GameLibIssue> issues)
        {
            FirebaseProjectChecks.Validate(issues, "Crashlytics",
                "Firebase.Crashlytics.Crashlytics, Firebase.Crashlytics", "Firebase Crashlytics (com.google.firebase.crashlytics)");
        }
    }
}
