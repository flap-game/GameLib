using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace FlapGame.GameLib
{
    /// <summary>Console sink for the Editor and development builds - lets you verify events without a dashboard.</summary>
    public sealed class DebugLogAnalyticsSink : IAnalyticsSink
    {
        private const string Prefix = "[Analytics] ";

        public void Track(AnalyticsEvent analyticsEvent)
        {
            var sb = new StringBuilder(Prefix).Append(analyticsEvent.Name);
            if (analyticsEvent.Parameters != null && analyticsEvent.Parameters.Count > 0)
            {
                sb.Append(" {");
                bool first = true;
                foreach (KeyValuePair<string, object> kv in analyticsEvent.Parameters)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(kv.Key).Append('=').Append(kv.Value ?? "null");
                }
                sb.Append('}');
            }
            Debug.Log(sb.ToString());
        }

        public void SetUserProperty(string name, string value) =>
            Debug.Log($"{Prefix}user_property {name}={value ?? "null"}");

        public void SetUserId(string userId) =>
            Debug.Log($"{Prefix}user_id={userId ?? "null"}");

        public void TrackAdRevenue(AdRevenue revenue) =>
            Debug.Log($"{Prefix}ad_revenue {revenue}");

        public void TrackPurchase(PurchaseRecord purchase) =>
            Debug.Log($"{Prefix}purchase {purchase}");
    }
}
