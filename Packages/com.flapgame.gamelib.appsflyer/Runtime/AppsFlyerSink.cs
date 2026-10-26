using System;
using System.Collections.Generic;
using System.Globalization;

namespace FlapGame.GameLib.AppsFlyer
{
    /// <summary>How purchases reach AppsFlyer.</summary>
    public enum AppsFlyerPurchaseMode
    {
        /// <summary>GameLib logs <c>af_purchase</c> for every <see cref="IAnalytics.TrackPurchase"/> (no receipt validation).</summary>
        Manual = 0,
        /// <summary>AppsFlyer's ROI360 Purchase Connector validates and logs store purchases itself (paid AppsFlyer plan).</summary>
        PurchaseConnector = 1,
        /// <summary>No purchase revenue to AppsFlyer.</summary>
        None = 2,
    }

    /// <summary>
    /// Live AppsFlyer calls. <see cref="AppsFlyerModule"/> wraps it in a <see cref="BufferedAnalyticsSink{TTarget}"/> that
    /// holds everything until the SDK started.
    /// <list type="bullet">
    /// <item>Ad revenue: <c>logAdRevenue</c> with the network, the mediation platform, currency and value, plus
    /// <c>ad_unit</c>, <c>ad_type</c>, <c>placement</c> and <c>country</c>.</item>
    /// <item>Purchases (<see cref="AppsFlyerPurchaseMode.Manual"/>): <c>af_purchase</c> with <c>af_revenue</c>,
    /// <c>af_currency</c>, <c>af_content_id</c>, <c>af_content_type</c>, <c>af_order_id</c>, <c>af_quantity</c>.</item>
    /// <item>Game events: only those listed (or all, when <see cref="ForwardAllEvents"/>), values as invariant strings.</item>
    /// <item>The user id becomes the customer user id; user properties are not sent (AppsFlyer has none).</item>
    /// </list>
    /// </summary>
    public sealed class AppsFlyerSink : IAnalyticsSink
    {
        public const string PurchaseEvent = "af_purchase";

        private readonly IAppsFlyerBackend backend;
        private readonly HashSet<string> forwardedEvents;

        public AppsFlyerSink(IAppsFlyerBackend backend, AppsFlyerPurchaseMode purchaseMode, bool forwardAllEvents, IEnumerable<string> forwardedEvents)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            PurchaseMode = purchaseMode;
            ForwardAllEvents = forwardAllEvents;
            this.forwardedEvents = new HashSet<string>(forwardedEvents ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        public AppsFlyerPurchaseMode PurchaseMode { get; }

        public bool ForwardAllEvents { get; }

        public void Track(AnalyticsEvent analyticsEvent)
        {
            if (!ForwardAllEvents && !forwardedEvents.Contains(analyticsEvent.Name)) return;
            backend.LogEvent(analyticsEvent.Name, ToStrings(analyticsEvent.Parameters));
        }

        public void SetUserProperty(string name, string value)
        {
        }

        public void SetUserId(string userId) => backend.SetCustomerUserId(userId ?? string.Empty);

        public void TrackAdRevenue(AdRevenue revenue)
        {
            if (revenue == null) return;
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ad_unit"] = revenue.AdUnitId ?? string.Empty,
                ["ad_type"] = AdTypeName(revenue.Format),
            };
            if (!string.IsNullOrEmpty(revenue.Placement)) values["placement"] = revenue.Placement;
            if (!string.IsNullOrEmpty(revenue.CountryCode)) values["country"] = revenue.CountryCode;

            backend.LogAdRevenue(string.IsNullOrEmpty(revenue.Network) ? "unknown" : revenue.Network, Mediation(revenue.Platform),
                                 string.IsNullOrEmpty(revenue.Currency) ? "USD" : revenue.Currency, revenue.Value, values);
        }

        public void TrackPurchase(PurchaseRecord purchase)
        {
            if (purchase == null || PurchaseMode != AppsFlyerPurchaseMode.Manual) return;
            backend.LogEvent(PurchaseEvent, BuildPurchaseValues(purchase));
        }

        public void ApplyConsent(ConsentState state)
        {
            AppsFlyerConsentData? consent = AppsFlyerConsentData.From(state);
            if (consent.HasValue) backend.SetConsent(consent.Value);
        }

        public static Dictionary<string, string> BuildPurchaseValues(PurchaseRecord purchase)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["af_revenue"] = purchase.Price.ToString(CultureInfo.InvariantCulture),
                ["af_currency"] = purchase.Currency?.Trim().ToUpperInvariant() ?? string.Empty,
                ["af_content_id"] = purchase.ProductId ?? string.Empty,
                ["af_content_type"] = purchase.ProductKind switch
                {
                    PurchaseProductKind.Consumable => "consumable",
                    PurchaseProductKind.NonConsumable => "non_consumable",
                    PurchaseProductKind.Subscription => "subscription",
                    _ => "unknown",
                },
                ["af_quantity"] = "1",
            };
            if (!string.IsNullOrEmpty(purchase.TransactionId)) values["af_order_id"] = purchase.TransactionId;
            if (!string.IsNullOrEmpty(purchase.Placement)) values["placement"] = purchase.Placement;
            return values;
        }

        /// <summary>The mediation platform from <see cref="AdRevenue.Platform"/> (AppLovin, AdMob, ironSource, ...).</summary>
        public static AdMediation Mediation(string platform)
        {
            switch (platform?.Trim().ToLowerInvariant())
            {
                case "applovin": case "max": case "applovin max": return AdMediation.ApplovinMax;
                case "admob": case "google admob": case "google": return AdMediation.GoogleAdMob;
                case "ironsource": case "levelplay": return AdMediation.IronSource;
                default: return AdMediation.Custom;
            }
        }

        public static string AdTypeName(AdFormatKind format) => format switch
        {
            AdFormatKind.Interstitial => "interstitial",
            AdFormatKind.Rewarded => "rewarded",
            AdFormatKind.Banner => "banner",
            AdFormatKind.Mrec => "mrec",
            AdFormatKind.AppOpen => "app_open",
            _ => "unknown",
        };

        private static Dictionary<string, string> ToStrings(IReadOnlyDictionary<string, object> parameters)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (parameters == null) return result;
            foreach (KeyValuePair<string, object> kv in parameters)
            {
                result[kv.Key] = kv.Value switch
                {
                    null => string.Empty,
                    string s => s,
                    bool b => b ? "true" : "false",
                    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                    _ => kv.Value.ToString(),
                };
            }
            return result;
        }
    }
}
