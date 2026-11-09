using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Analytics.Local
{
    /// <summary>
    /// Posts every tracked event to the internal tracking server, one at a time and in order: an event is dequeued only
    /// after a 2xx answer, a failed post is retried, and <see cref="LocalTrackingOptions.MaxConsecutiveFailures"/> failures
    /// in a row turn local tracking off for the session (the queue is dropped, later calls are no-ops) - the same contract
    /// as aframework's <c>LocalAnalytics</c>. Every event carries <c>eventCount</c>, its sequence number in this session,
    /// so a gap in the rows is visible on the server.
    /// <para>User id and user properties are ignored: the server has no place for them, it joins rows on the device id.
    /// Ad revenue and purchases are flattened into the <c>ad_impression</c> and <c>purchase</c> events.</para>
    /// </summary>
    public sealed class LocalAnalyticsSink : IAnalyticsSink
    {
        public const string AdImpressionEvent = "ad_impression";
        public const string PurchaseEvent = "purchase";

        /// <summary>Parameter every event carries: its 0-based sequence number in this session.</summary>
        public const string EventCountParameter = "eventCount";

        private readonly LocalTrackingOptions options;
        private readonly ILocalTrackingTransport transport;
        private readonly ILocalDeviceIdSource deviceIds;
        private readonly object gate = new();
        private readonly Queue<LocalTrackingEvent> queue = new();
        private long eventCount;
        private int droppedEvents;
        private volatile bool enabled = true;

        public LocalAnalyticsSink(LocalTrackingOptions options, ILocalTrackingTransport transport = null, ILocalDeviceIdSource deviceIds = null)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.Endpoint)) throw new ArgumentException("Endpoint is empty.", nameof(options));
            this.transport = transport ?? new UnityWebRequestTransport();
            this.deviceIds = deviceIds ?? new AdvertisingDeviceIdSource();
        }

        /// <summary>False once local tracking gave up (no ATT consent, or the server kept failing). Calls then do nothing.</summary>
        public bool IsEnabled => enabled;

        /// <summary>The id sent as <c>idfa</c>; null until it has been resolved.</summary>
        public string DeviceId { get; private set; }

        public int QueuedEventCount { get { lock (gate) return queue.Count; } }

        /// <summary>Events accepted since the start of the session (the next one's <c>eventCount</c>).</summary>
        public long TrackedEventCount { get { lock (gate) return eventCount; } }

        /// <summary>Events thrown away because the queue was full while the server was unreachable.</summary>
        public int DroppedEventCount { get { lock (gate) return droppedEvents; } }

        public void Track(AnalyticsEvent analyticsEvent)
        {
            if (!enabled || string.IsNullOrEmpty(analyticsEvent.Name)) return;

            var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
            if (analyticsEvent.Parameters != null)
            {
                foreach (KeyValuePair<string, object> kv in analyticsEvent.Parameters)
                {
                    if (!string.IsNullOrEmpty(kv.Key)) parameters[kv.Key] = kv.Value;
                }
            }

            Enqueue(analyticsEvent.Name, parameters);
        }

        /// <summary>Ignored: the tracking server stores events only.</summary>
        public void SetUserProperty(string name, string value)
        {
        }

        /// <summary>Ignored: the server joins a device's rows on the device id sent with every event.</summary>
        public void SetUserId(string userId)
        {
        }

        public void TrackAdRevenue(AdRevenue revenue)
        {
            if (!enabled || revenue == null) return;
            Enqueue(AdImpressionEvent, BuildAdImpressionParameters(revenue));
        }

        public void TrackPurchase(PurchaseRecord purchase)
        {
            if (!enabled || purchase == null) return;
            Enqueue(PurchaseEvent, BuildPurchaseParameters(purchase));
        }

        /// <summary>Same parameter names as the Firebase sink's <c>ad_impression</c>, so the two can be compared row by row.</summary>
        public static Dictionary<string, object> BuildAdImpressionParameters(AdRevenue revenue)
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
            if (!string.IsNullOrEmpty(revenue.CountryCode)) parameters["country"] = revenue.CountryCode;
            return parameters;
        }

        /// <summary>Flat parameters (no nested <c>items</c> array): the server stores one level.</summary>
        public static Dictionary<string, object> BuildPurchaseParameters(PurchaseRecord purchase)
        {
            var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["product_id"] = purchase.ProductId ?? string.Empty,
                ["product_kind"] = ProductKindName(purchase.ProductKind),
                ["value"] = (double)purchase.Price,
                ["currency"] = purchase.Currency ?? string.Empty,
            };
            if (!string.IsNullOrEmpty(purchase.TransactionId)) parameters["transaction_id"] = purchase.TransactionId;
            if (!string.IsNullOrEmpty(purchase.Placement)) parameters["placement"] = purchase.Placement;
            if (purchase.IsTest) parameters["is_test"] = true;
            return parameters;
        }

        public static string FormatName(AdFormatKind format) => format switch
        {
            AdFormatKind.Interstitial => "interstitial",
            AdFormatKind.Rewarded => "rewarded",
            AdFormatKind.Banner => "banner",
            AdFormatKind.Mrec => "mrec",
            AdFormatKind.AppOpen => "app_open",
            AdFormatKind.RewardedInterstitial => "rewarded_interstitial",
            AdFormatKind.Native => "native",
            _ => "unknown",
        };

        private static string ProductKindName(PurchaseProductKind kind) => kind switch
        {
            PurchaseProductKind.Consumable => "consumable",
            PurchaseProductKind.NonConsumable => "non_consumable",
            PurchaseProductKind.Subscription => "subscription",
            _ => "unknown",
        };

        private void Enqueue(string name, Dictionary<string, object> parameters)
        {
            bool dropped;
            int totalDropped;
            lock (gate)
            {
                parameters[EventCountParameter] = eventCount++;
                queue.Enqueue(new LocalTrackingEvent(name, parameters));
                dropped = queue.Count > Math.Max(1, options.MaxQueuedEvents);
                if (dropped)
                {
                    queue.Dequeue();
                    droppedEvents++;
                }
                totalDropped = droppedEvents;
            }

            // Only the first drop is logged: once the server is unreachable every event would log the same line.
            if (dropped && totalDropped == 1)
            {
                Debug.LogWarning($"{LocalAnalyticsModule.LogTag} The queue is full ({options.MaxQueuedEvents} events) - dropping the oldest. " +
                                 "Is the tracking server reachable from this device?");
            }
        }

        /// <summary>
        /// Resolves the device id (on iOS after the ATT answer; no consent means no local tracking) and then drains the
        /// queue until <paramref name="cancellationToken"/> fires or local tracking gives up. Never throws.
        /// </summary>
        public async Task RunAsync(IAttService att, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!await TryResolveDeviceIdAsync(att, cancellationToken)) return;
                await PumpAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Shutdown (or Reset in the Editor): leave the rest of the queue where it is.
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Disable("an unexpected error");
            }
        }

        /// <summary>Stops local tracking for the session and drops the queue. Idempotent.</summary>
        public void Disable(string reason)
        {
            if (!enabled) return;
            enabled = false;
            lock (gate) queue.Clear();
            Debug.LogWarning($"{LocalAnalyticsModule.LogTag} Off for this session: {reason}.");
        }

        private async Task<bool> TryResolveDeviceIdAsync(IAttService att, CancellationToken cancellationToken)
        {
            // iOS only, like aframework: without ATT consent there is no IDFA, and the rows on the server would not join.
            if (att != null && options.RequireAttConsent)
            {
                AttStatus status = await WaitForAttAsync(att, cancellationToken);
                if (status != AttStatus.Authorized && status != AttStatus.NotApplicable)
                {
                    Disable($"App Tracking Transparency is {status}");
                    return false;
                }
            }

            string id = null;
            try
            {
                id = await deviceIds.GetAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LocalAnalyticsModule.LogTag} Could not read the advertising id ({e.GetType().Name}: {e.Message}).");
            }

            DeviceId = string.IsNullOrEmpty(id) ? SystemInfo.deviceUniqueIdentifier : id;
            return true;
        }

        private async Task<AttStatus> WaitForAttAsync(IAttService att, CancellationToken cancellationToken)
        {
            AttStatus status = att.Status;
            if (status != AttStatus.NotDetermined) return status;

            // The ATT module (or another SDK) shows the prompt; this only waits for the answer.
            var pollInterval = TimeSpan.FromMilliseconds(250);
            TimeSpan waited = TimeSpan.Zero;
            while ((status = att.Status) == AttStatus.NotDetermined)
            {
                if (options.AttWait > TimeSpan.Zero && waited >= options.AttWait)
                {
                    Debug.LogWarning($"{LocalAnalyticsModule.LogTag} App Tracking Transparency not answered after {options.AttWait.TotalSeconds:0}s.");
                    return status;
                }

                await Task.Delay(pollInterval, cancellationToken);
                waited += pollInterval;
            }

            return status;
        }

        private async Task PumpAsync(CancellationToken cancellationToken)
        {
            int failures = 0;
            while (enabled && !cancellationToken.IsCancellationRequested)
            {
                LocalTrackingEvent next;
                lock (gate) next = queue.Count > 0 ? queue.Peek() : null;
                if (next == null)
                {
                    await Task.Delay(options.RetryDelay, cancellationToken);
                    continue;
                }

                string body = LocalTrackingJson.BuildBody(options.BundleId, options.AppVersion, DeviceId, next);
                bool posted = await transport.PostAsync(options.Endpoint, body, options.RequestTimeout, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (posted)
                {
                    failures = 0;
                    lock (gate)
                    {
                        // Peek, post, then dequeue: a failed post keeps the event, so nothing is lost on a retry.
                        if (queue.Count > 0 && ReferenceEquals(queue.Peek(), next)) queue.Dequeue();
                    }
                    continue;
                }

                if (++failures >= Math.Max(1, options.MaxConsecutiveFailures))
                {
                    Disable($"{failures} posts to {options.Endpoint} failed in a row");
                    return;
                }

                await Task.Delay(options.RetryDelay, cancellationToken);
            }
        }
    }
}
