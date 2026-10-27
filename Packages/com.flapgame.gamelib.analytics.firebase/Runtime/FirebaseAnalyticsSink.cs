using System;
using System.Collections.Generic;

namespace FlapGame.GameLib.Analytics.Firebase
{
    /// <summary>
    /// Live Firebase Analytics calls. <see cref="FirebaseAnalyticsModule"/> wraps it in a
    /// <see cref="BufferedAnalyticsSink{TTarget}"/> so nothing tracked before Firebase is up is lost.
    /// <list type="bullet">
    /// <item>Events, parameters and user properties are fitted to Firebase limits by <see cref="FirebaseAnalyticsSanitizer"/>.</item>
    /// <item><see cref="TrackAdRevenue"/> logs GA4's <c>ad_impression</c> (<c>ad_platform</c>, <c>ad_source</c>,
    /// <c>ad_format</c>, <c>ad_unit_name</c>, <c>value</c>, <c>currency</c>) plus <c>placement</c>, for the platforms
    /// Firebase does not measure itself. AdMob impressions are skipped unless <see cref="LogAdMobImpressions"/>: with the
    /// AdMob app linked to Firebase, the Firebase SDK logs their <c>ad_impression</c> automatically, and a second one
    /// would count the revenue twice.</item>
    /// <item><see cref="TrackPurchase"/> logs the standard <c>purchase</c> only when <see cref="LogStandardPurchaseEvent"/>
    /// is on: on device Firebase already auto-logs <c>in_app_purchase</c> for store purchases, so both would count the same
    /// revenue twice in GA4.</item>
    /// </list>
    /// </summary>
    public sealed class FirebaseAnalyticsSink : IAnalyticsSink
    {
        public const string AdImpressionEvent = "ad_impression";
        public const string PurchaseEvent = "purchase";

        private readonly IFirebaseAnalyticsBackend backend;
        private readonly FirebaseAnalyticsSanitizer sanitizer;

        public FirebaseAnalyticsSink(IFirebaseAnalyticsBackend backend, bool logStandardPurchaseEvent, FirebaseAnalyticsSanitizer sanitizer = null,
                                     bool logAdMobImpressions = false)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            LogStandardPurchaseEvent = logStandardPurchaseEvent;
            LogAdMobImpressions = logAdMobImpressions;
            this.sanitizer = sanitizer ?? new FirebaseAnalyticsSanitizer();
        }

        public bool LogStandardPurchaseEvent { get; }

        /// <summary>Also log <c>ad_impression</c> for AdMob-served ads (only when the AdMob app is NOT linked to Firebase).</summary>
        public bool LogAdMobImpressions { get; }

        /// <summary><see cref="AdRevenue.Platform"/> of AdMob-served ads.</summary>
        public const string AdMobPlatform = "AdMob";

        public void Track(AnalyticsEvent analyticsEvent)
        {
            AnalyticsEvent? clean = sanitizer.Sanitize(analyticsEvent);
            if (clean.HasValue) backend.LogEvent(clean.Value.Name, clean.Value.Parameters);
        }

        public void SetUserProperty(string name, string value)
        {
            string cleanName = sanitizer.SanitizeUserPropertyName(name);
            if (cleanName != null) backend.SetUserProperty(cleanName, sanitizer.SanitizeUserPropertyValue(cleanName, value));
        }

        public void SetUserId(string userId) => backend.SetUserId(sanitizer.SanitizeUserId(userId));

        public void TrackAdRevenue(AdRevenue revenue)
        {
            if (revenue == null) return;
            if (!LogAdMobImpressions && string.Equals(revenue.Platform, AdMobPlatform, StringComparison.OrdinalIgnoreCase)) return;
            Track(BuildAdImpression(revenue));
        }

        public void TrackPurchase(PurchaseRecord purchase)
        {
            if (!LogStandardPurchaseEvent || purchase == null) return;
            Track(BuildPurchase(purchase));
        }

        public void ApplyConsent(ConsentState state)
        {
            FirebaseConsent? consent = FirebaseConsent.From(state);
            if (consent.HasValue) backend.SetConsent(consent.Value);
        }

        public static AnalyticsEvent BuildAdImpression(AdRevenue revenue)
        {
            var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["ad_platform"] = revenue.Platform ?? string.Empty,
                ["ad_source"] = revenue.Network ?? string.Empty,
                ["ad_format"] = FormatName(revenue.Format),
                ["ad_unit_name"] = revenue.AdUnitId ?? string.Empty,
                ["value"] = revenue.Value,
                ["currency"] = string.IsNullOrEmpty(revenue.Currency) ? "USD" : revenue.Currency,
            };
            if (!string.IsNullOrEmpty(revenue.Placement)) parameters["placement"] = revenue.Placement;
            if (!string.IsNullOrEmpty(revenue.Precision)) parameters["precision"] = revenue.Precision;
            return new AnalyticsEvent(AdImpressionEvent, parameters);
        }

        /// <summary>
        /// GA4 <c>purchase</c>: <c>value</c> only together with a 3-letter currency (GA4 discards value without currency),
        /// <c>transaction_id</c>, one item (<c>item_id</c>, <c>item_category</c>, <c>price</c>, <c>quantity</c>).
        /// </summary>
        public static AnalyticsEvent BuildPurchase(PurchaseRecord purchase)
        {
            var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
            string currency = purchase.Currency?.Trim().ToUpperInvariant();
            bool hasCurrency = currency != null && currency.Length == 3;
            if (hasCurrency)
            {
                parameters["currency"] = currency;
                parameters["value"] = (double)purchase.Price;
            }
            if (!string.IsNullOrEmpty(purchase.TransactionId)) parameters["transaction_id"] = purchase.TransactionId;
            if (!string.IsNullOrEmpty(purchase.Placement)) parameters["placement"] = purchase.Placement;

            if (!string.IsNullOrEmpty(purchase.ProductId))
            {
                var item = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["item_id"] = purchase.ProductId,
                    ["item_category"] = ProductKindName(purchase.ProductKind),
                    ["quantity"] = 1L,
                };
                if (hasCurrency) item["price"] = (double)purchase.Price;
                parameters["items"] = new IDictionary<string, object>[] { item };
            }

            return new AnalyticsEvent(PurchaseEvent, parameters);
        }

        public static string FormatName(AdFormatKind format) => format switch
        {
            AdFormatKind.Interstitial => "interstitial",
            AdFormatKind.Rewarded => "rewarded",
            AdFormatKind.Banner => "banner",
            AdFormatKind.Mrec => "mrec",
            AdFormatKind.AppOpen => "app_open",
            AdFormatKind.RewardedInterstitial => "rewarded_interstitial",
            _ => "unknown",
        };

        private static string ProductKindName(PurchaseProductKind kind) => kind switch
        {
            PurchaseProductKind.Consumable => "consumable",
            PurchaseProductKind.NonConsumable => "non_consumable",
            PurchaseProductKind.Subscription => "subscription",
            _ => "unknown",
        };
    }
}
