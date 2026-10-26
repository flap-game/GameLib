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
    /// at <see cref="MaxQueuedCalls"/>, oldest dropped first) are replayed into <typeparamref name="TTarget"/> when
    /// <see cref="InitializeAsync"/> succeeds. Any failure, timeout or cancellation drops the buffer and turns every later
    /// call into a no-op, with one log line (a Warning when the failure is expected, e.g. no config file in the Editor).
    /// <para><see cref="Run"/> queues SDK-specific calls (a Crashlytics log line) in the same order as the events.</para>
    /// </summary>
    public sealed class BufferedAnalyticsSink<TTarget> : IAnalyticsSink where TTarget : class, IAnalyticsSink
    {
        public const int DefaultMaxQueuedCalls = 200;

        private readonly object gate = new();
        private readonly TTarget target;
        private readonly string logTag;
        private readonly Queue<Action<TTarget>> pendingCalls = new();
        private readonly Dictionary<string, string> pendingUserProperties = new();
        private string pendingUserId;
        private bool pendingUserIdSet;
        private bool warnedQueueFull;
        private BufferedSinkState state = BufferedSinkState.Pending;

        public BufferedAnalyticsSink(TTarget target, string logTag, int maxQueuedCalls = DefaultMaxQueuedCalls)
        {
            this.target = target ?? throw new ArgumentNullException(nameof(target));
            this.logTag = string.IsNullOrEmpty(logTag) ? "[Analytics]" : logTag;
            MaxQueuedCalls = Math.Max(1, maxQueuedCalls);
        }

        public int MaxQueuedCalls { get; }

        public BufferedSinkState State { get { lock (gate) return state; } }

        public int QueuedCallCount { get { lock (gate) return pendingCalls.Count; } }

        /// <summary>Why initialization failed (null otherwise).</summary>
        public string FailureReason { get; private set; }

        /// <summary>
        /// Runs <paramref name="initialize"/> under <paramref name="timeout"/> (≤ 0 or infinite = none), then flushes.
        /// Never throws. Continuations stay on the caller's context (Unity main thread), so the flush does too.
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
                    // Observe the abandoned task so a late fault is not reported as unobserved.
                    _ = initTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    if (cancellationToken.IsCancellationRequested) MarkFailed("initialization cancelled", expected: true);
                    else MarkFailed($"initialization timed out after {timeout.TotalSeconds:0.#}s", expectedFailure);
                    return;
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

            MarkReady();
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
                while (pendingCalls.Count > 0)
                    Safe(pendingCalls.Dequeue());

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
        public void Run(Action<TTarget> call)
        {
            if (call == null) return;
            lock (gate)
            {
                if (state == BufferedSinkState.Failed) return;
                if (state == BufferedSinkState.Pending)
                {
                    Enqueue(call);
                    return;
                }
            }
            call(target);
        }

        public void Track(AnalyticsEvent analyticsEvent) => Run(t => t.Track(analyticsEvent));

        public void TrackAdRevenue(AdRevenue revenue) => Run(t => t.TrackAdRevenue(revenue));

        public void TrackPurchase(PurchaseRecord purchase) => Run(t => t.TrackPurchase(purchase));

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

        private void Enqueue(Action<TTarget> call)
        {
            if (pendingCalls.Count >= MaxQueuedCalls)
            {
                pendingCalls.Dequeue();
                if (!warnedQueueFull)
                {
                    warnedQueueFull = true;
                    Debug.LogWarning($"{logTag} More than {MaxQueuedCalls} calls made before initialization finished; dropping the oldest.");
                }
            }
            pendingCalls.Enqueue(call);
        }

        private void ClearBuffer()
        {
            pendingCalls.Clear();
            pendingUserProperties.Clear();
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
