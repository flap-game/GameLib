using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FlapGame.GameLib.RemoteConfig
{
    /// <summary>The values of the last activated fetch, on disk, so the next launch starts with them before any network.</summary>
    public sealed class RemoteConfigCache
    {
        // v2 holds server values only; the 0.3/0.4 file also held the in-app defaults, which would override new defaults.
        public const string DefaultFileName = "gamelib_remote_config_v2.json";
        public const string LegacyFileName = "gamelib_remote_config.json";

        [Serializable]
        private sealed class CacheData
        {
            public List<CacheEntry> entries = new();
            public long fetchedAtUtcTicks;
        }

        [Serializable]
        private sealed class CacheEntry
        {
            public string key;
            public string value;
        }

        public RemoteConfigCache(string filePath) => FilePath = filePath;

        /// <summary>The cache under <c>Application.persistentDataPath</c>; removes the legacy file.</summary>
        public static RemoteConfigCache Default
        {
            get
            {
                try
                {
                    string legacy = Path.Combine(Application.persistentDataPath, LegacyFileName);
                    if (File.Exists(legacy)) File.Delete(legacy);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RemoteConfig] Could not remove the old cache: {e.Message}");
                }
                return new RemoteConfigCache(Path.Combine(Application.persistentDataPath, DefaultFileName));
            }
        }

        public string FilePath { get; }

        /// <summary>The cached values; empty when there is no cache or it cannot be read. Never throws.</summary>
        public Dictionary<string, string> Load()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath)) return result;
                CacheData data = JsonUtility.FromJson<CacheData>(File.ReadAllText(FilePath));
                if (data?.entries == null) return result;
                foreach (CacheEntry e in data.entries)
                    if (!string.IsNullOrEmpty(e.key))
                        result[e.key] = e.value ?? string.Empty;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoteConfig] Could not read the cache: {e.Message}");
            }
            return result;
        }

        /// <summary>Replaces the cache. Never throws.</summary>
        public void Save(IReadOnlyDictionary<string, string> values)
        {
            try
            {
                var data = new CacheData { fetchedAtUtcTicks = DateTime.UtcNow.Ticks };
                foreach (KeyValuePair<string, string> kv in values)
                    data.entries.Add(new CacheEntry { key = kv.Key, value = kv.Value });
                File.WriteAllText(FilePath, JsonUtility.ToJson(data));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoteConfig] Could not save the cache: {e.Message}");
            }
        }
    }
}
