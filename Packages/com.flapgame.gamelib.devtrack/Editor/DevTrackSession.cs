using System;

namespace FlapGame.GameLib.DevTrack
{
    /// <summary>
    /// The state machine: a Play session opens, heartbeats and closes, and every step turns into one queued event.
    /// It knows nothing about the Editor - it takes the clock reading and the session id as arguments - so the whole
    /// lifecycle, a domain reload in the middle included, is unit tested. <see cref="DevTrackHook"/> is the thin
    /// adapter that calls it from <c>EditorApplication</c>.
    /// </summary>
    public sealed class DevTrackSession
    {
        /// <summary>A heartbeat every minute while Play runs.</summary>
        public const double HeartbeatSeconds = 60;

        private readonly IDevTrackStateStore _store;

        public DevTrackSession(IDevTrackStateStore store, int queueCapacity = DevTrackQueue.DefaultCapacity, int maxAttempts = DevTrackQueue.DefaultMaxAttempts)
        {
            _store = store;
            State = DevTrackStateJson.Load(store);
            Queue = new DevTrackQueue(State.pending, queueCapacity, maxAttempts);
        }

        public DevTrackState State { get; }
        public DevTrackQueue Queue { get; }

        public bool IsOpen => State.open;

        /// <summary>Seconds this session has been in Play at <paramref name="now"/>; 0 when none is open.</summary>
        public int ElapsedSeconds(double now) => State.open ? Seconds(now - State.startedAt) : 0;

        /// <summary>
        /// Play was entered: a new session id, and a <c>play_start</c>. A session still open here was left behind by an
        /// Editor that never reported its stop (a crash in Play, a reload that ate the edit-mode callback); it is closed
        /// with its own <c>play_stop</c> first, so the server never sees two starts on one id.
        /// </summary>
        public DevTrackPending PlayStarted(string sessionId, double now)
        {
            if (State.open) PlayStopped(now);

            State.sessionId = string.IsNullOrEmpty(sessionId) ? Guid.NewGuid().ToString("D") : sessionId;
            State.startedAt = now;
            State.lastHeartbeat = now;
            State.open = true;

            DevTrackPending start = Enqueue(DevTrackPayload.PlayStart, State.sessionId, DevTrackPayload.NoDuration);
            Save();
            return start;
        }

        /// <summary>
        /// Enqueues a heartbeat when one is due. Called from the pump every Editor frame, so the common answer is "not
        /// yet" and nothing is written. Null when no session is open or the minute is not up.
        /// </summary>
        public DevTrackPending Heartbeat(double now)
        {
            if (!State.open) return null;
            if (now - State.lastHeartbeat < HeartbeatSeconds) return null;

            State.lastHeartbeat = now;
            DevTrackPending beat = Enqueue(DevTrackPayload.Heartbeat, State.sessionId, ElapsedSeconds(now));
            Save();
            return beat;
        }

        /// <summary>
        /// Play was left (or the Editor is quitting with a session still open): a <c>play_stop</c> with the elapsed
        /// seconds, and the session closes. Null when none was open - a stop without a start reports nothing.
        /// </summary>
        public DevTrackPending PlayStopped(double now)
        {
            if (!State.open) return null;

            DevTrackPending stop = Enqueue(DevTrackPayload.PlayStop, State.sessionId, ElapsedSeconds(now));
            State.open = false;
            State.sessionId = string.Empty;
            State.startedAt = 0;
            State.lastHeartbeat = 0;
            Save();
            return stop;
        }

        /// <summary>The context the background probe collected; from here on events may be sent.</summary>
        public void SetContext(DevTrackContext context)
        {
            State.context = context;
            State.contextReady = context != null;
            Save();
        }

        /// <summary>The head was accepted by the server.</summary>
        public void Sent()
        {
            Queue.Succeeded();
            Save();
        }

        /// <summary>
        /// The head's POST failed. Returns the text of the one warning this Editor session gets, or null - a server
        /// that is down is otherwise silent.
        /// </summary>
        public string SendFailed(double now, string detail)
        {
            bool retrying = Queue.Failed(now);
            string warning = null;

            if (!retrying && !State.warned)
            {
                State.warned = true;
                warning = $"{DevTrackConfig.LogTag} Could not report this Play session ({detail}). Nothing else will be " +
                          "logged about it this Editor session; dev tracking stays out of the way.";
            }

            Save();
            return warning;
        }

        public void Save() => DevTrackStateJson.Save(_store, State);

        /// <summary>Forgets the open session and the queue (the menu's off switch).</summary>
        public void Clear()
        {
            State.open = false;
            State.sessionId = string.Empty;
            State.startedAt = 0;
            State.lastHeartbeat = 0;
            Queue.Clear();
            Save();
        }

        private DevTrackPending Enqueue(string eventName, string sessionId, int durationSeconds)
        {
            var pending = new DevTrackPending
            {
                ev = eventName,
                sessionId = sessionId,
                durationSeconds = durationSeconds,
            };
            Queue.Enqueue(pending);
            return pending;
        }

        private static int Seconds(double value) => value <= 0 ? 0 : (int)Math.Round(value);
    }
}
