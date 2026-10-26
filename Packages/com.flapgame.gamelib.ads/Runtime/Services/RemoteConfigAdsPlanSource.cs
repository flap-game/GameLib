using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// The <see cref="IAdsPlanSource"/> <see cref="AdsModule"/> uses when a remote config module is installed: the JSON
    /// <see cref="AdsPlan"/> under <see cref="AdsModule.PlanRemoteConfigKey"/>. At install it reads the value remote config
    /// already has (last session's fetch or the default); every <see cref="IRemoteConfig.Activated"/> with a different value
    /// raises <see cref="PlanChanged"/>. A malformed plan means no plan (warning); unknown provider ids are dropped (warning).
    /// </summary>
    public sealed class RemoteConfigAdsPlanSource : IAdsPlanSource, IDisposable
    {
        private readonly string key;
        private IRemoteConfig remoteConfig;
        private string lastJson;

        /// <param name="key">Remote config key. Null/empty = never a plan.</param>
        public RemoteConfigAdsPlanSource(string key, IRemoteConfig remoteConfig)
        {
            this.key = key;
            if (remoteConfig == null || string.IsNullOrEmpty(key)) return;
            this.remoteConfig = remoteConfig;
            remoteConfig.Activated += OnActivated;
        }

        /// <inheritdoc />
        public event Action<AdsPlan> PlanChanged;

        /// <inheritdoc />
        public bool TryGetCachedPlan(out AdsPlan plan)
        {
            plan = null;
            lastJson = Read();
            return TryParse(lastJson, out plan);
        }

        private string Read()
        {
            if (remoteConfig == null) return null;
            try
            {
                string json = remoteConfig.GetString(key, string.Empty);
                return string.IsNullOrWhiteSpace(json) ? null : json;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Ads] Reading '{key}' failed: {e.Message}");
                return lastJson;
            }
        }

        private void OnActivated()
        {
            string json = Read();
            if (json == lastJson) return; // unchanged (no value before and after counts as unchanged)
            lastJson = json;
            TryParse(json, out AdsPlan plan);
            PlanChanged?.Invoke(plan);
        }

        /// <summary>
        /// Parses a plan. False (and a warning) when the JSON is malformed; empty JSON is "no plan" without a warning.
        /// Unknown provider ids (neither an <see cref="AdProviderIds"/> constant nor a registered factory) are removed.
        /// Never throws.
        /// </summary>
        public static bool TryParse(string json, out AdsPlan plan)
        {
            plan = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                if (!RemoteConfigJson.TryDeserialize(json, out AdsPlan parsed) || parsed == null)
                {
                    Debug.LogWarning("[Ads] Ads plan is malformed - running without a plan.");
                    return false;
                }
                parsed.Providers = FilterKnown(parsed.Providers, "providers");
                if (parsed.Placements != null)
                    foreach (AdsPlanPlacement p in parsed.Placements)
                        if (p != null) p.Routes = FilterKnown(p.Routes, $"routes of '{p.Id}'");
                plan = parsed;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Ads] Ads plan could not be read ({e.Message}) - running without a plan.");
                plan = null;
                return false;
            }
        }

        private static List<string> FilterKnown(List<string> ids, string where)
        {
            if (ids == null) return null;
            var result = new List<string>(ids.Count);
            foreach (string raw in ids)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string id = AdProviderIds.Normalize(raw);
                if (IsKnown(id)) result.Add(id);
                else Debug.LogWarning($"[Ads] Ads plan {where}: unknown provider '{raw}' ignored.");
            }
            return result;
        }

        private static bool IsKnown(string id) =>
            id == AdProviderIds.None || id == AdProviderIds.AdMob || id == AdProviderIds.Max || id == AdProviderIds.LevelPlay
            || AdsProviderCatalog.Contains(id);

        /// <inheritdoc />
        public void Dispose()
        {
            if (remoteConfig != null) remoteConfig.Activated -= OnActivated;
            remoteConfig = null;
        }
    }
}
