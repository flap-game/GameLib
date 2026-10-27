using System;
using System.Collections.Generic;

namespace FlapGame.GameLib
{
    public readonly struct AnalyticsEvent
    {
        public AnalyticsEvent(string name, IReadOnlyDictionary<string, object> parameters = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Parameters = parameters;
        }

        public string Name { get; }

        public IReadOnlyDictionary<string, object> Parameters { get; }
    }

    /// <summary>
    /// Game-facing tracking, always registered by <see cref="GameLibBootstrap"/>. Every call fans out to the installed
    /// sinks (Firebase, AppsFlyer, the debug console). With no tracking module installed the calls only reach the console.
    /// Terse overloads live in <see cref="AnalyticsExtensions"/>: <c>analytics.Track("level_complete", ("level", 3))</c>.
    /// </summary>
    public interface IAnalytics
    {
        void Track(AnalyticsEvent analyticsEvent);

        /// <summary>Sets a persistent user property (segment, ab_group, ...). Pass null to clear.</summary>
        void SetUserProperty(string name, string value);

        /// <summary>Sets the user id shared across sinks. Pass null to clear.</summary>
        void SetUserId(string userId);

        /// <summary>Paid ad impression. Reported by the ads modules; each sink maps it to its own format
        /// (Firebase <c>ad_impression</c>, AppsFlyer <c>logAdRevenue</c>).</summary>
        void TrackAdRevenue(AdRevenue revenue);

        /// <summary>Completed purchase. Reported by the IAP module; each sink maps it to its own format.</summary>
        void TrackPurchase(PurchaseRecord purchase);
    }

    /// <summary>A tracking backend (Firebase, AppsFlyer, console). Register with <see cref="IAnalyticsSinkRegistry"/>.</summary>
    public interface IAnalyticsSink
    {
        void Track(AnalyticsEvent analyticsEvent);

        void SetUserProperty(string name, string value);

        void SetUserId(string userId);

        void TrackAdRevenue(AdRevenue revenue);

        void TrackPurchase(PurchaseRecord purchase);
    }

    public interface IAnalyticsSinkRegistry
    {
        /// <summary>Adds a sink; the user id and user properties already set are replayed to it.</summary>
        void AddSink(IAnalyticsSink sink);
    }

    /// <summary>Revenue of one paid ad impression, in the shape every ad SDK reports it.</summary>
    public sealed class AdRevenue
    {
        /// <summary>Mediation platform, e.g. <c>AppLovin</c>, <c>AdMob</c> (Firebase <c>ad_platform</c>).</summary>
        public string Platform { get; set; }

        /// <summary>Network that served the ad, e.g. <c>Facebook</c>, <c>Unity Ads</c> (Firebase <c>ad_source</c>).</summary>
        public string Network { get; set; }

        public AdFormatKind Format { get; set; }

        /// <summary>Ad unit id (Firebase <c>ad_unit_name</c>).</summary>
        public string AdUnitId { get; set; }

        /// <summary>The game's placement id that showed the ad.</summary>
        public string Placement { get; set; }

        /// <summary>Revenue in <see cref="Currency"/> units (not micros).</summary>
        public double Value { get; set; }

        /// <summary>ISO 4217 currency code reported by the SDK.</summary>
        public string Currency { get; set; } = "USD";

        /// <summary>SDK precision, e.g. <c>publisher_defined</c>, <c>exact</c>, <c>estimated</c>. Optional.</summary>
        public string Precision { get; set; }

        /// <summary>Two-letter country code when the SDK reports it. Optional.</summary>
        public string CountryCode { get; set; }

        public override string ToString() =>
            $"{Platform}/{Network} {Format} placement={Placement} unit={AdUnitId} {Value} {Currency}";
    }

    public enum AdFormatKind
    {
        Unknown = 0,
        Interstitial = 1,
        Rewarded = 2,
        Banner = 3,
        Mrec = 4,
        AppOpen = 5,
        RewardedInterstitial = 6,
    }

    /// <summary>One completed (and granted) purchase.</summary>
    public sealed class PurchaseRecord
    {
        public string ProductId { get; set; }

        public PurchaseProductKind ProductKind { get; set; }

        /// <summary>Localized price paid, in <see cref="Currency"/> units.</summary>
        public decimal Price { get; set; }

        /// <summary>ISO 4217 currency code from the store.</summary>
        public string Currency { get; set; }

        public string TransactionId { get; set; }

        /// <summary>Where the purchase was started (shop, offer popup, ...). Optional.</summary>
        public string Placement { get; set; }

        /// <summary>Store receipt, for sinks that validate purchases (AppsFlyer Purchase Connector). Optional.</summary>
        public string Receipt { get; set; }

        /// <summary>True when the store marked the purchase as a sandbox / test purchase.</summary>
        public bool IsTest { get; set; }

        public override string ToString() => $"{ProductId} ({ProductKind}) {Price} {Currency} tx={TransactionId}";
    }

    public enum PurchaseProductKind
    {
        Consumable = 0,
        NonConsumable = 1,
        Subscription = 2,
    }

    /// <summary>
    /// Crash reporting (e.g. Firebase Crashlytics). Uncaught exceptions and native crashes are reported by the SDK itself;
    /// this is for what the game catches and still wants to see, plus context for the next report.
    /// Registered only when a crash reporting module is installed - resolve it with <c>TryResolve</c>.
    /// </summary>
    public interface ICrashReporter
    {
        /// <summary>Adds a line to the log attached to the next crash or non-fatal report.</summary>
        void Log(string message);

        /// <summary>Reports a caught exception as a non-fatal issue.</summary>
        void RecordException(Exception exception);

        /// <summary>Sets a key shown with every later report (level, mode, ...). Null clears it.</summary>
        void SetCustomKey(string key, string value);
    }
}
