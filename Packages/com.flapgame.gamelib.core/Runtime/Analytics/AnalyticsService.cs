using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace FlapGame.GameLib
{
    /// <summary>
    /// Fan-out <see cref="IAnalytics"/>. Sinks added after user id / properties were set receive them immediately,
    /// so module order does not matter.
    /// </summary>
    public sealed class AnalyticsService : IAnalytics, IAnalyticsSinkRegistry
    {
        private readonly object gate = new();
        private readonly Dictionary<string, string> userProperties = new();
        // Copy-on-write so calls can iterate without locking while a sink is being added (any thread).
        private IAnalyticsSink[] sinks = Array.Empty<IAnalyticsSink>();
        private string userId;

        public IReadOnlyList<IAnalyticsSink> Sinks => Volatile.Read(ref sinks);

        public void AddSink(IAnalyticsSink sink)
        {
            if (sink == null) return;

            string id;
            KeyValuePair<string, string>[] properties;
            lock (gate)
            {
                if (Array.IndexOf(sinks, sink) >= 0) return;
                var next = new IAnalyticsSink[sinks.Length + 1];
                Array.Copy(sinks, next, sinks.Length);
                next[sinks.Length] = sink;
                Volatile.Write(ref sinks, next);

                id = userId;
                properties = new KeyValuePair<string, string>[userProperties.Count];
                ((ICollection<KeyValuePair<string, string>>)userProperties).CopyTo(properties, 0);
            }

            // Replay identity so a late-installed sink is not missing it.
            if (id != null) Safe(sink, s => s.SetUserId(id));
            foreach (KeyValuePair<string, string> kv in properties)
                Safe(sink, s => s.SetUserProperty(kv.Key, kv.Value));
        }

        public void Track(AnalyticsEvent analyticsEvent) => ForEach(s => s.Track(analyticsEvent));

        public void SetUserProperty(string name, string value)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (gate)
            {
                if (value == null) userProperties.Remove(name);
                else userProperties[name] = value;
            }

            ForEach(s => s.SetUserProperty(name, value));
        }

        public void SetUserId(string id)
        {
            lock (gate) userId = id;
            ForEach(s => s.SetUserId(id));
        }

        public void TrackAdRevenue(AdRevenue revenue)
        {
            if (revenue == null) return;
            ForEach(s => s.TrackAdRevenue(revenue));
        }

        public void TrackPurchase(PurchaseRecord purchase)
        {
            if (purchase == null) return;
            ForEach(s => s.TrackPurchase(purchase));
        }

        private void ForEach(Action<IAnalyticsSink> call)
        {
            IAnalyticsSink[] current = Volatile.Read(ref sinks);
            for (int i = 0; i < current.Length; i++)
                Safe(current[i], call);
        }

        // One misbehaving SDK must never break tracking for the others.
        private static void Safe(IAnalyticsSink sink, Action<IAnalyticsSink> call)
        {
            try { call(sink); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>
    /// Terse tracking helpers so call sites do not build dictionaries by hand:
    /// <code>analytics.Track("level_complete", ("level", 3), ("stars", 2));</code>
    /// </summary>
    public static class AnalyticsExtensions
    {
        public static void Track(this IAnalytics analytics, string eventName)
        {
            analytics?.Track(new AnalyticsEvent(eventName));
        }

        public static void Track(this IAnalytics analytics, string eventName, params (string key, object value)[] parameters)
        {
            if (analytics == null) return;
            if (parameters == null || parameters.Length == 0)
            {
                analytics.Track(new AnalyticsEvent(eventName));
                return;
            }

            var dict = new Dictionary<string, object>(parameters.Length);
            foreach ((string key, object value) in parameters)
            {
                if (!string.IsNullOrEmpty(key))
                    dict[key] = value;
            }

            analytics.Track(new AnalyticsEvent(eventName, dict));
        }

        public static void Track(this IAnalytics analytics, string eventName, IReadOnlyDictionary<string, object> parameters)
        {
            analytics?.Track(new AnalyticsEvent(eventName, parameters));
        }
    }
}
