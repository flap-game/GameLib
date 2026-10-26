using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib
{
    public enum BufferedSinkState { Pending, Ready, Failed }

    /// <summary>
    /// Holds calls for an SDK that initializes in the background (Firebase, AppsFlyer). Sinks are registered at once so
    /// nothing tracked during startup is lost: identity (last value wins) and every other call (in arrival order, capped
    /// at <see cref="MaxQueuedCalls"/>) are replayed into <typeparamref name="TTarget"/> when <see cref="InitializeAsync"/>
    /// succeeds. A full buffer drops its oldest event or <see cref="Run"/> call first; ad revenue and purchases go last.
    /// Any failure or cancellation drops the buffer and turns every later call into a no-op, with one log line (a Warning
    /// when the failure is expected, e.g. no config file in the Editor). A timeout is not a failure: it logs one warning
    /// and the buffer still flushes when the start lands later.
    /// <para><see cref="Run"/> queues SDK-specific calls (a Crashlytics log line) in the same order as the events;
    /// <see cref="RunLatest"/> keeps only the last call per key (consent) and is never dropped.</para>
    /// </summary>
    public sealed class BufferedAnalyticsSink<TTarget> : IAnalyticsSink where TTarget : class, IAnalyticsSink
    {
        public const int DefaultMaxQueuedCalls = 200;

        private readonly object gate = new();
        private readonly TTarget target;
        private readonly string logTag;
        private readonly Func<AnalyticsEvent, bool> bufferEvent;
        // Revenue = ad revenue or purchase: evicted only when nothing else is left.
        private readonly List<(Action<TTarget> Call, bool Revenue)> pendingCalls = new();
        private readonly Dictionary<string, string> pendingUserProperties = new();
        // Last call per key, flushed before the queued calls in first-set order.
        private readonly List<(string Key, Action<TTarget> Call)> pendingLatest = new();
        private string pendingUserId;
        private bool pendingUserIdSet;
        private bool warnedQueueFull;
        // Written under the lock; Ready and Failed are final, so a lock-free read of Ready is safe (the flush already ran).
        private volatile BufferedSinkState state = BufferedSinkState.Pending;

        /// <param name="bufferEvent">Which events are worth buffering (null = all), e.g. only those the SDK forwards, so
        /// events it would drop anyway do not take the room of ad revenue and purchases. Live calls are not filtered.</param>
        public BufferedAnalyticsSink(TTarget target, string logTag, int maxQueuedCalls = DefaultMaxQueuedCalls, Func<AnalyticsEvent, bool> bufferEvent = null)
        {
            this.target = target ?? throw new ArgumentNullException(nameof(target));
            this.logTag = string.IsNullOrEmpty(logTag) ? "[Analytics]" : logTag;
            MaxQueuedCalls = Math.Max(1, maxQueuedCalls);
            this.bufferEvent = bufferEvent;
        }

        public int MaxQueuedCalls { get; }

        public BufferedSinkState State { get { lock (gate) return state; } }

        public int QueuedCallCount { get { lock (gate) return pendingCalls.Count; } }

        /// <summary>Why initialization failed (null otherwise).</summary>
        public string FailureReason { get; private set; }

        /// <summary>
        /// Runs <paramref name="initialize"/>, then flushes. Past <paramref name="timeout"/> (≤ 0 or infinite = none) it logs
        /// one warning and keeps buffering: a late start still flushes, a late failure still disables. Completes once the
        /// sink left <see cref="BufferedSinkState.Pending"/>. Never throws. Continuations stay on the caller's context
        /// (Unity main thread), so the flush does too.
        /// </summary>
        public async Task InitializeAsync(Func<CancellationToken, Task> initialize, TimeSpan timeout, bool expectedFailure, CancellationToken cancellationToken = default)
        {
            if (initialize == null) throw new ArgumentNullException(nameof(initialize));
            if (State != BufferedSinkState.Pending) return;

            Task initTask;
            try
            {
                initTask = initialize(cancellationToken) ?? Task.CompletedTask;
            }
            catch (Exception e)
            {
                MarkFailed(Describe(e), expectedFailure);
                return;
            }

            bool timedOut = false;
            if (!initTask.IsCompleted && timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task delay = Task.Delay(timeout, timeoutCts.Token);
                Task winner;
                try { winner = await Task.WhenAny(initTask, delay); }
                catch (Exception e) { MarkFailed(Describe(e), expectedFailure); return; }
                timeoutCts.Cancel();

                if (winner != initTask)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        // Observe the abandoned task so a late fault is not reported as unobserved.
                        _ = initTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                        MarkFailed("initialization cancelled", expected: true);
                        return;
                    }

                    // Not a failure: a slow start (Firebase's Android dependency check) is still flushed when it lands.
                    timedOut = true;
                    Debug.LogWarning($"{logTag} Initialization is taking longer than {timeout.TotalSeconds:0.#}s; still buffering, everything is sent if it finishes later.");
                }
            }

            try
            {
                await initTask;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                MarkFailed("initialization cancelled", expected: true);
                return;
            }
            catch (Exception e)
            {
                MarkFailed(Describe(e), expectedFailure);
                return;
            }

            // A cancelled bootstrap (e.g. reset) is not an SDK problem: warn, never error.
            if (cancellationToken.IsCancellationRequested)
            {
                MarkFailed("initialization cancelled", expected: true);
                return;
            }

            int buffered = QueuedCallCount;
            if (MarkReady() && timedOut)
                Debug.Log($"{logTag} Initialized after the timeout; sent {buffered} buffered call(s).");
        }

        /// <summary>Flushes the buffer and forwards everything live from now on. False when not pending.</summary>
        public bool MarkReady()
        {
            lock (gate)
            {
                if (state != BufferedSinkState.Pending) return false;

                // Flush under the lock and flip to Ready last, so a call made concurrently can neither overtake the
                // buffer nor be enqueued after the drain.
                if (pendingUserIdSet) Safe(t => t.SetUserId(pendingUserId));
                foreach (KeyValuePair<string, string> kv in pendingUserProperties)
                    Safe(t => t.SetUserProperty(kv.Key, kv.Value));
                foreach ((string _, Action<TTarget> call) in pendingLatest)
                    Safe(call);
                for (int i = 0; i < pendingCalls.Count; i++)
                    Safe(pendingCalls[i].Call);

                ClearBuffer();
                state = BufferedSinkState.Ready;
                return true;
            }
        }

        /// <summary>Drops the buffer and ignores everything from now on. False when not pending.</summary>
        public bool MarkFailed(string reason, bool expected = false)
        {
            int dropped;
            lock (gate)
            {
                if (state != BufferedSinkState.Pending) return false;
                dropped = pendingCalls.Count;
                ClearBuffer();
                FailureReason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
                state = BufferedSinkState.Failed;
            }

            string message = $"{logTag} Disabled for this session: {FailureReason}. Dropped {dropped} buffered call(s); later calls are ignored.";
            if (expected) Debug.LogWarning(message);
            else Debug.LogError(message);
            return true;
        }

        /// <summary>Queues (before init) or runs (after) an SDK-specific call, in order with the events.</summary>
        public void Run(Action<TTarget> call) => Run(call, revenue: false);

        /// <summary>
        /// Like <see cref="Run"/>, but before init only the last call per <paramref name="key"/> is kept, it is never
        /// dropped by a full buffer, and it runs before the queued calls - for state the SDK must have before any event
        /// (consent).
        /// </summary>
        public void RunLatest(string key, Action<TTarget> call)
        {
            if (call == null) return;
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A key is required.", nameof(key));
            lock (gate)
            {
                if (state == BufferedSinkState.Failed) return;
                if (state == BufferedSinkState.Pending)
                {
                    int index = pendingLatest.FindIndex(c => c.Key == key);
                    if (index >= 0) pendingLatest[index] = (key, call);
                    else pendingLatest.Add((key, call));
                    return;
                }
            }
            call(target);
        }

        public void Track(AnalyticsEvent analyticsEvent)
        {
            if (state == BufferedSinkState.Ready)
            {
                target.Track(analyticsEvent);
                return;
            }
            if (bufferEvent != null && State == BufferedSinkState.Pending && !bufferEvent(analyticsEvent)) return;
            Run(t => t.Track(analyticsEvent), revenue: false);
        }

        public void TrackAdRevenue(AdRevenue revenue)
        {
            if (state == BufferedSinkState.Ready) target.TrackAdRevenue(revenue);
            else Run(t => t.TrackAdRevenue(revenue), revenue: true);
        }

        public void TrackPurchase(PurchaseRecord purchase)
        {
            if (state == BufferedSinkState.Ready) target.TrackPurchase(purchase);
            else Run(t => t.TrackPurchase(purchase), revenue: true);
        }

        public void SetUserProperty(string name, string value)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (gate)
            {
                if (state == BufferedSinkState.Failed) return;
                if (state == BufferedSinkState.Pending)
                {
                    // Last write wins; null clears, and clearing before init just removes the pending value.
                    if (value == null) pendingUserProperties.Remove(name);
                    else pendingUserProperties[name] = value;
                    return;
                }
            }
            target.SetUserProperty(name, value);
        }

        public void SetUserId(string userId)
        {
            lock (gate)
            {
                if (state == BufferedSinkState.Failed) return;
                if (state == BufferedSinkState.Pending)
                {
                    pendingUserId = userId;
                    pendingUserIdSet = true;
                    return;
                }
            }
            target.SetUserId(userId);
        }

        /// <summary>
        /// The user id set while pending, for an SDK that wants it before its own start (it is still replayed on
        /// <see cref="MarkReady"/>). False when none was set or the sink is no longer pending.
        /// </summary>
        public bool TryGetPendingUserId(out string userId)
        {
            lock (gate)
            {
                userId = pendingUserId;
                return state == BufferedSinkState.Pending && pendingUserIdSet;
            }
        }

        private void Run(Action<TTarget> call, bool revenue)
        {
            if (call == null) return;
            lock (gate)
            {
                if (state == BufferedSinkState.Failed) return;
                if (state == BufferedSinkState.Pending)
                {
                    Enqueue(call, revenue);
                    return;
                }
            }
            call(target);
        }

        private void Enqueue(Action<TTarget> call, bool revenue)
        {
            if (pendingCalls.Count >= MaxQueuedCalls)
            {
                // Oldest event first; ad revenue and purchases only when nothing else is left - and never for an event.
                int victim = pendingCalls.FindIndex(c => !c.Revenue);
                if (victim < 0 && !revenue)
                {
                    WarnQueueFull();
                    return;
                }
                pendingCalls.RemoveAt(victim >= 0 ? victim : 0);
                WarnQueueFull();
            }
            pendingCalls.Add((call, revenue));
        }

        private void WarnQueueFull()
        {
            if (warnedQueueFull) return;
            warnedQueueFull = true;
            Debug.LogWarning($"{logTag} More than {MaxQueuedCalls} calls made before initialization finished; dropping the oldest (ad revenue and purchases last).");
        }

        private void ClearBuffer()
        {
            pendingCalls.Clear();
            pendingUserProperties.Clear();
            pendingLatest.Clear();
            pendingUserId = null;
            pendingUserIdSet = false;
        }

        private static string Describe(Exception e)
        {
            // TypeInitializationException / AggregateException hide the useful message one level down.
            while ((e is TypeInitializationException || e is AggregateException) && e.InnerException != null)
                e = e.InnerException;
            return $"{e.GetType().Name}: {e.Message}";
        }

        // One bad buffered call must not abort the flush.
        private void Safe(Action<TTarget> call)
        {
            try { call(target); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
