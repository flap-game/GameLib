using System;
using UnityEditor;

namespace FlapGame.GameLib.DevTrack
{
    /// <summary>
    /// The thin adapter: it turns <c>EditorApplication</c> into calls on <see cref="DevTrackSession"/> and pumps
    /// <see cref="DevTrackSender"/>. All of the logic is in those two, which is what makes it testable without Play mode.
    ///
    /// Entering and leaving Play both reload the domain and wipe these statics; the session and the queue come back from
    /// <c>SessionState</c>, so a reload neither loses nor duplicates a session.
    /// </summary>
    [InitializeOnLoad]
    internal static class DevTrackHook
    {
        private static DevTrackSession _session;
        private static DevTrackSender _sender;
        private static string _endpoint;
        private static DevTrackDecision _decision;

        static DevTrackHook()
        {
            _decision = DevTrackConfig.Decide();
            _endpoint = DevTrackConfig.Endpoint();
            if (!_decision.On) return;

            _session = new DevTrackSession(new DevTrackSessionStateStore());
            _sender = new DevTrackSender();

            if (!_session.State.contextReady) DevTrackContextProbe.Begin();

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnUpdate;
            EditorApplication.quitting += OnQuitting;
        }

        internal static bool IsOn => _session != null;

        internal static DevTrackDecision Decision => _decision;

        internal static string Endpoint => _endpoint;

        internal static DevTrackSession Session => _session;

        /// <summary>The Editor clock: seconds since the Editor started, and it survives a domain reload.</summary>
        private static double Now => EditorApplication.timeSinceStartup;

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    _session.PlayStarted(Guid.NewGuid().ToString("D"), Now);
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    _session.PlayStopped(Now);
                    break;
            }
        }

        private static void OnUpdate()
        {
            if (!_session.State.contextReady && DevTrackContextProbe.TryTake(out DevTrackContext context))
                _session.SetContext(context);

            double now = Now;
            _session.Heartbeat(now);
            _sender.Pump(_session, _endpoint, now);
        }

        /// <summary>
        /// Quitting with a session still open: the play_stop cannot go through the pump (the Editor loop is stopping),
        /// so it is posted straight away with a short budget. Whatever is still queued dies with the Editor.
        /// </summary>
        private static void OnQuitting()
        {
            DevTrackPending stop = _session.PlayStopped(Now);
            if (stop == null) return;

            _sender.Dispose();
            if (!_session.State.contextReady) return;

            DevTrackSender.PostBlocking(_endpoint, DevTrackSender.Body(stop, _session.State.context), DevTrackConfig.QuitTimeoutMilliseconds);
        }

        /// <summary>
        /// The menu's test event: one <c>play_start</c> and one <c>play_stop</c> on a throwaway session id, through the
        /// normal queue, so the server end can be checked without entering Play. Returns the line to log.
        /// </summary>
        internal static string SendTestEvent()
        {
            if (_session == null)
                return $"{DevTrackConfig.LogTag} Off ({_decision.Reason}): nothing was sent.";

            string sessionId = Guid.NewGuid().ToString("D");
            _session.Queue.Enqueue(new DevTrackPending { ev = DevTrackPayload.PlayStart, sessionId = sessionId });
            _session.Queue.Enqueue(new DevTrackPending { ev = DevTrackPayload.PlayStop, sessionId = sessionId, durationSeconds = 0 });
            _session.Save();

            return $"{DevTrackConfig.LogTag} Test play_start + play_stop queued for session {sessionId} -> {_endpoint}" +
                   (_session.State.contextReady ? "." : " (they go out once the git / machine probe is done).");
        }
    }
}
