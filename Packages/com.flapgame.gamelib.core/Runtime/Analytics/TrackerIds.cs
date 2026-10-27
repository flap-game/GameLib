using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlapGame.GameLib
{
    /// <summary>
    /// The ids each tracker gives this install (Firebase app instance id, AppsFlyer id), for joining rows across tools -
    /// e.g. an <c>af_firebase_id</c> parameter. The tracker modules fill it once their SDK answers; the last known values
    /// are kept on the device, so they are there from the first frame of every later launch.
    /// Always registered by <see cref="GameLibBootstrap"/>.
    /// </summary>
    public interface ITrackerIds
    {
        /// <summary>The id <paramref name="tracker"/> (<see cref="TrackerIds.Firebase"/>, <see cref="TrackerIds.AppsFlyer"/>...) gave this install, or null while it is unknown.</summary>
        string Get(string tracker);

        /// <summary>Called by a tracker module when its SDK reports the id. Null or empty is ignored.</summary>
        void Set(string tracker, string id);

        /// <summary>Raised with (tracker, id) when an id is new or changed.</summary>
        event Action<string, string> Changed;
    }

    public static class TrackerIds
    {
        /// <summary>Firebase Analytics app instance id (what GA4 / BigQuery call <c>user_pseudo_id</c>).</summary>
        public const string Firebase = "firebase";

        /// <summary>AppsFlyer id (<c>getAppsFlyerId</c>).</summary>
        public const string AppsFlyer = "appsflyer";

        public static string FirebaseAppInstanceId(this ITrackerIds ids) => ids?.Get(Firebase);

        public static string AppsFlyerId(this ITrackerIds ids) => ids?.Get(AppsFlyer);
    }

    /// <summary>Default <see cref="ITrackerIds"/>, kept in PlayerPrefs under <c>GameLib.TrackerId.&lt;tracker&gt;</c>.</summary>
    public sealed class PlayerPrefsTrackerIds : ITrackerIds
    {
        private const string KeyPrefix = "GameLib.TrackerId.";

        private readonly Dictionary<string, string> cache = new();

        public event Action<string, string> Changed;

        public string Get(string tracker)
        {
            if (string.IsNullOrEmpty(tracker)) return null;
            if (!cache.TryGetValue(tracker, out string id))
            {
                id = PlayerPrefs.GetString(KeyPrefix + tracker, string.Empty);
                if (id.Length == 0) id = null;
                cache[tracker] = id;
            }
            return id;
        }

        public void Set(string tracker, string id)
        {
            if (string.IsNullOrEmpty(tracker)) throw new ArgumentException("Tracker name is empty.", nameof(tracker));
            if (string.IsNullOrEmpty(id) || Get(tracker) == id) return;

            cache[tracker] = id;
            PlayerPrefs.SetString(KeyPrefix + tracker, id);
            PlayerPrefs.Save();

            Action<string, string> handlers = Changed;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try { ((Action<string, string>)handler)(tracker, id); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
