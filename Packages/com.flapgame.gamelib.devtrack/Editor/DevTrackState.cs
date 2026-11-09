using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FlapGame.GameLib.DevTrack
{
    /// <summary>One event waiting to be sent, with what the retry needs. Persisted with the rest of the state.</summary>
    [Serializable]
    public sealed class DevTrackPending
    {
        /// <summary><c>play_start</c>, <c>heartbeat</c> or <c>play_stop</c> (<see cref="DevTrackPayload"/>).</summary>
        public string ev;
        public string sessionId;

        /// <summary>Seconds in Play mode, or <see cref="DevTrackPayload.NoDuration"/> on a play_start.</summary>
        public int durationSeconds = DevTrackPayload.NoDuration;

        /// <summary>Failed POSTs so far; at <see cref="DevTrackQueue.DefaultMaxAttempts"/> the event is given up on.</summary>
        public int attempts;

        /// <summary>Editor clock reading before which the next attempt must not be made (the backoff).</summary>
        public double nextAttempt;
    }

    /// <summary>
    /// Everything the hook must not lose to a domain reload: the open session, the queue, the context it collected and
    /// whether the one warning about an unreachable server has already been logged. Entering and leaving Play both
    /// reload the domain, so the statics go and this comes back from <see cref="DevTrackSessionStateStore"/>.
    /// </summary>
    [Serializable]
    public sealed class DevTrackState
    {
        /// <summary>GUID of the Play session currently open; empty when none is.</summary>
        public string sessionId = string.Empty;

        /// <summary>Editor clock reading when Play started.</summary>
        public double startedAt;

        /// <summary>Editor clock reading of the last heartbeat (or of the start, before the first one).</summary>
        public double lastHeartbeat;

        /// <summary>True between a play_start and its play_stop, across any number of domain reloads.</summary>
        public bool open;

        public List<DevTrackPending> pending = new List<DevTrackPending>();

        public DevTrackContext context;

        /// <summary>
        /// Set once the background probe has filled the context. Nothing is sent before that: an event without the git
        /// and machine fields is not worth a row on the server.
        /// </summary>
        public bool contextReady;

        /// <summary>One warning per Editor session about a server that cannot be reached - this is how it is remembered.</summary>
        public bool warned;
    }

    /// <summary>Where <see cref="DevTrackState"/> lives between domain reloads.</summary>
    public interface IDevTrackStateStore
    {
        /// <summary>The stored JSON, or null when nothing is stored yet.</summary>
        string Read();

        void Write(string json);
    }

    /// <summary>
    /// <c>UnityEditor.SessionState</c>: survives a domain reload, dies with the Editor process - exactly the lifetime of
    /// an open Play session. Nothing is written to disk, so a stale session cannot outlive the Editor that opened it.
    /// </summary>
    public sealed class DevTrackSessionStateStore : IDevTrackStateStore
    {
        public const string Key = "GameLib.DevTrack.State";

        public string Read()
        {
            string json = SessionState.GetString(Key, string.Empty);
            return string.IsNullOrEmpty(json) ? null : json;
        }

        public void Write(string json) => SessionState.SetString(Key, json ?? string.Empty);
    }

    /// <summary>Reads and writes <see cref="DevTrackState"/> as JSON. Damaged content is replaced, never thrown over.</summary>
    public static class DevTrackStateJson
    {
        public static DevTrackState Load(IDevTrackStateStore store)
        {
            string json = store?.Read();
            if (string.IsNullOrEmpty(json)) return new DevTrackState();

            try
            {
                DevTrackState state = JsonUtility.FromJson<DevTrackState>(json);
                if (state == null) return new DevTrackState();
                if (state.pending == null) state.pending = new List<DevTrackPending>();
                return state;
            }
            catch (Exception)
            {
                return new DevTrackState();
            }
        }

        public static void Save(IDevTrackStateStore store, DevTrackState state)
        {
            if (store == null || state == null) return;
            try { store.Write(JsonUtility.ToJson(state)); }
            catch (Exception) { }
        }
    }
}
