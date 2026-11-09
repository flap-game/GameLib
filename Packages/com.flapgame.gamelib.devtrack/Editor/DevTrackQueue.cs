using System;
using System.Collections.Generic;

namespace FlapGame.GameLib.DevTrack
{
    /// <summary>
    /// The pending-send queue, and with it the retry buffer: a bounded FIFO over the list in
    /// <see cref="DevTrackState"/>, so it survives a domain reload. The cap drops the oldest event, a failed event waits
    /// out a growing backoff, and after <see cref="MaxAttempts"/> failures it is given up on - a server that is down
    /// must not fill the queue, log a wall of errors or slow the Editor down.
    /// </summary>
    public sealed class DevTrackQueue
    {
        public const int DefaultCapacity = 50;

        /// <summary>POSTs one event gets before it is dropped.</summary>
        public const int DefaultMaxAttempts = 4;

        /// <summary>First backoff, doubled per failed attempt: 5 s, 10 s, 20 s.</summary>
        public const double BaseRetrySeconds = 5;

        public const double MaxRetrySeconds = 120;

        private readonly List<DevTrackPending> _items;

        public DevTrackQueue(List<DevTrackPending> items, int capacity = DefaultCapacity, int maxAttempts = DefaultMaxAttempts)
        {
            _items = items ?? throw new ArgumentNullException(nameof(items));
            Capacity = Math.Max(1, capacity);
            MaxAttempts = Math.Max(1, maxAttempts);
        }

        public int Capacity { get; }
        public int MaxAttempts { get; }
        public int Count => _items.Count;

        /// <summary>Events dropped because the queue was full. Not reported anywhere; read by the menu and the tests.</summary>
        public int DroppedCount { get; private set; }

        /// <summary>Events given up on after <see cref="MaxAttempts"/> failed POSTs.</summary>
        public int GaveUpCount { get; private set; }

        public IReadOnlyList<DevTrackPending> Items => _items;

        /// <summary>Appends the event; at the cap the oldest one goes. Returns false when an event had to be dropped.</summary>
        public bool Enqueue(DevTrackPending item)
        {
            if (item == null) return true;

            bool dropped = false;
            while (_items.Count >= Capacity)
            {
                _items.RemoveAt(0);
                DroppedCount++;
                dropped = true;
            }

            _items.Add(item);
            return !dropped;
        }

        /// <summary>
        /// The head when it may be sent now, else null (nothing queued, or the head is waiting out its backoff). Only
        /// the head is ever sent, so the server sees a session's events in order.
        /// </summary>
        public DevTrackPending Due(double now)
        {
            if (_items.Count == 0) return null;
            DevTrackPending head = _items[0];
            return head.nextAttempt > now ? null : head;
        }

        /// <summary>The head was accepted (2xx): it leaves the queue.</summary>
        public void Succeeded()
        {
            if (_items.Count > 0) _items.RemoveAt(0);
        }

        /// <summary>
        /// The head's POST failed. Returns true while it is still worth retrying; false once it has been given up on,
        /// which also removes it. Only the first give-up of an Editor session is worth a warning
        /// (<see cref="DevTrackState.warned"/>).
        /// </summary>
        public bool Failed(double now)
        {
            if (_items.Count == 0) return false;

            DevTrackPending head = _items[0];
            head.attempts++;
            if (head.attempts >= MaxAttempts)
            {
                _items.RemoveAt(0);
                GaveUpCount++;
                return false;
            }

            head.nextAttempt = now + RetryDelay(head.attempts);
            return true;
        }

        /// <summary>5 s, 10 s, 20 s, ... capped at <see cref="MaxRetrySeconds"/>.</summary>
        public static double RetryDelay(int attempts)
        {
            if (attempts <= 1) return BaseRetrySeconds;
            double delay = BaseRetrySeconds * Math.Pow(2, Math.Min(attempts - 1, 16));
            return Math.Min(delay, MaxRetrySeconds);
        }

        public void Clear() => _items.Clear();
    }
}
