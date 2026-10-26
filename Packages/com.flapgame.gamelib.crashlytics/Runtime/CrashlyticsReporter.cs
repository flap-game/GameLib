using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace FlapGame.GameLib.Crashlytics
{
    /// <summary>
    /// Crashlytics as <see cref="ICrashReporter"/> and as an analytics sink: the shared user id, user properties as custom
    /// keys and - when <see cref="BreadcrumbsFromAnalytics"/> is on - every event, ad revenue and purchase as one log line,
    /// so a report shows what the player did just before.
    /// <para>Crashlytics captures uncaught exceptions and native crashes by itself once Firebase is up. Until then every
    /// call is buffered and flushed in order; if Firebase cannot start the reporter turns itself off with one log line.</para>
    /// </summary>
    public sealed class CrashlyticsReporter : ICrashReporter, IAnalyticsSink
    {
        public const string LogTag = "[Crashlytics]";

        private readonly ICrashlyticsBackend backend;
        private readonly BufferedAnalyticsSink<Target> buffer;

        public CrashlyticsReporter(ICrashlyticsBackend backend, bool breadcrumbsFromAnalytics = true, bool reportUncaughtExceptionsAsFatal = false)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            BreadcrumbsFromAnalytics = breadcrumbsFromAnalytics;
            ReportUncaughtExceptionsAsFatal = reportUncaughtExceptionsAsFatal;
            buffer = new BufferedAnalyticsSink<Target>(new Target(backend, breadcrumbsFromAnalytics), LogTag);
        }

        public bool BreadcrumbsFromAnalytics { get; }

        public bool ReportUncaughtExceptionsAsFatal { get; }

        public BufferedSinkState State => buffer.State;

        /// <summary>Starts Crashlytics. Never throws: any failure ends in <see cref="BufferedSinkState.Failed"/>.</summary>
        public Task InitializeAsync(TimeSpan timeout, bool expectedFailure, CancellationToken cancellationToken = default) =>
            buffer.InitializeAsync(ct => backend.InitializeAsync(ReportUncaughtExceptionsAsFatal, ct), timeout, expectedFailure, cancellationToken);

        // ── ICrashReporter ───────────────────────────────────────────────

        public void Log(string message)
        {
            if (!string.IsNullOrEmpty(message)) buffer.Run(t => t.Backend.Log(message));
        }

        public void RecordException(Exception exception)
        {
            if (exception != null) buffer.Run(t => t.Backend.LogException(exception));
        }

        public void SetCustomKey(string key, string value)
        {
            if (!string.IsNullOrEmpty(key)) buffer.Run(t => t.Backend.SetCustomKey(key, value ?? string.Empty));
        }

        // ── IAnalyticsSink ───────────────────────────────────────────────

        public void Track(AnalyticsEvent analyticsEvent) => buffer.Track(analyticsEvent);

        public void SetUserProperty(string name, string value) => buffer.SetUserProperty(name, value);

        public void SetUserId(string userId) => buffer.SetUserId(userId);

        public void TrackAdRevenue(AdRevenue revenue) => buffer.TrackAdRevenue(revenue);

        public void TrackPurchase(PurchaseRecord purchase) => buffer.TrackPurchase(purchase);

        /// <summary>Live Crashlytics calls behind the buffer.</summary>
        private sealed class Target : IAnalyticsSink
        {
            private readonly bool breadcrumbs;

            public Target(ICrashlyticsBackend backend, bool breadcrumbs)
            {
                Backend = backend;
                this.breadcrumbs = breadcrumbs;
            }

            public ICrashlyticsBackend Backend { get; }

            public void Track(AnalyticsEvent analyticsEvent)
            {
                if (breadcrumbs) Backend.Log(CrashBreadcrumbFormatter.Format(analyticsEvent));
            }

            public void SetUserProperty(string name, string value) => Backend.SetCustomKey(name, value ?? string.Empty);

            public void SetUserId(string userId) => Backend.SetUserId(userId ?? string.Empty);

            public void TrackAdRevenue(AdRevenue revenue)
            {
                if (breadcrumbs && revenue != null)
                    Backend.Log($"ad_revenue format={revenue.Format} placement={revenue.Placement} network={revenue.Network} " +
                                $"value={revenue.Value.ToString(CultureInfo.InvariantCulture)} {revenue.Currency}");
            }

            public void TrackPurchase(PurchaseRecord purchase)
            {
                if (breadcrumbs && purchase != null)
                    Backend.Log($"purchase product={purchase.ProductId} price={purchase.Price.ToString(CultureInfo.InvariantCulture)} {purchase.Currency}");
            }
        }
    }
}
