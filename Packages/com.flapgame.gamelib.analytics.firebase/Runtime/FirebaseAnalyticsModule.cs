using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlapGame.GameLib.Firebase;
using UnityEngine;

namespace FlapGame.GameLib.Analytics.Firebase
{
    /// <summary>
    /// Adds the Firebase Analytics sink to <see cref="IAnalytics"/> at once and starts Firebase in the background, so
    /// bootstrap never waits on the dependency check. Everything tracked meanwhile is buffered and flushed in order; if
    /// Firebase cannot start (no config file) the sink turns itself off for the session with one log line; a slow start
    /// only warns and still flushes.
    /// Consent mode follows <see cref="IConsentState"/>. Once Firebase is up its app instance id goes to
    /// <see cref="ITrackerIds"/> (<see cref="TrackerIds.Firebase"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "GameLib/Modules/Firebase Analytics", fileName = "FirebaseAnalyticsModule")]
    public sealed class FirebaseAnalyticsModule : GameLibModule
    {
        public const string LogTag = "[FirebaseAnalytics]";

        [Tooltip("Seconds Firebase may take to start before a warning (0 = never warn). Events stay buffered and are sent when it starts later.")]
        [Min(0f)] public float InitTimeoutSeconds = 30f;

        [Tooltip("Also log GA4's standard 'purchase' event for every purchase. OFF by default: Firebase already auto-logs " +
                 "'in_app_purchase' on Android/iOS, so turning this on counts revenue twice unless auto-collection is off.")]
        public bool LogStandardPurchaseEvent;

        [Tooltip("Send Google consent mode (analytics_storage, ad_storage, ad_user_data, ad_personalization) from the CMP answer.")]
        public bool FollowConsent = true;

        public override int Order => GameLibModuleOrder.Analytics;

        public override ValueTask InstallAsync(IServiceRegistry services, CancellationToken cancellationToken = default)
        {
            IFirebaseAnalyticsBackend backend = FirebaseAnalyticsBackend.Current;
            if (backend == null)
            {
                Debug.LogWarning($"{LogTag} The Firebase Analytics package is not installed - events only reach the other sinks.");
                return default;
            }

            var buffer = new BufferedAnalyticsSink<FirebaseAnalyticsSink>(new FirebaseAnalyticsSink(backend, LogStandardPurchaseEvent), LogTag);
            services.Resolve<IAnalyticsSinkRegistry>().AddSink(buffer);

            if (FollowConsent && services.TryResolve(out IConsentState consent))
            {
                void Apply(ConsentState state) => buffer.RunLatest("consent", sink => sink.ApplyConsent(state));
                Apply(consent.Current);
                consent.Changed += Apply;
                services.RegisterDisposable(Disposable.Create(() => consent.Changed -= Apply));
            }

            TimeSpan timeout = InitTimeoutSeconds > 0f ? TimeSpan.FromSeconds(InitTimeoutSeconds) : Timeout.InfiniteTimeSpan;
            services.TryResolve(out ITrackerIds ids);
            async Task Initialize(CancellationToken ct)
            {
                await backend.InitializeAsync(ct);
                if (ids != null) _ = PublishInstanceIdAsync(backend, ids);
            }
            _ = buffer.InitializeAsync(Initialize, timeout, expectedFailure: Application.isEditor, cancellationToken);
            return default;
        }

        /// <summary>Reads the app instance id once Firebase runs and hands it to <paramref name="ids"/>; a failure only logs.</summary>
        public static async Task PublishInstanceIdAsync(IFirebaseAnalyticsBackend backend, ITrackerIds ids)
        {
            try
            {
                string id = await backend.GetAppInstanceIdAsync();
                if (!string.IsNullOrEmpty(id)) ids.Set(TrackerIds.Firebase, id);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogTag} App instance id unavailable: {e.Message}");
            }
        }

        public override void Validate(ICollection<GameLibIssue> issues)
        {
            FirebaseProjectChecks.Validate(issues, "Firebase Analytics",
                "Firebase.Analytics.FirebaseAnalytics, Firebase.Analytics", "Firebase Analytics (com.google.firebase.analytics)");
        }
    }
}
