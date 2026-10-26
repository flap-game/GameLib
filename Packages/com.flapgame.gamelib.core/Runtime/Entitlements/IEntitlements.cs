using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlapGame.GameLib
{
    /// <summary>
    /// Durable things the player owns, keyed by string. Lets modules cooperate without referencing each other:
    /// IAP grants <see cref="Entitlements.NoAds"/> on purchase / restore, Ads reads it. Games may add their own keys
    /// (and grant no-ads from a cheat or a reward). Always registered by <see cref="GameLibBootstrap"/>.
    /// </summary>
    public interface IEntitlements
    {
        bool Has(string key);

        void Grant(string key);

        void Revoke(string key);

        /// <summary>Raised with (key, owned) when an entitlement actually changes.</summary>
        event Action<string, bool> Changed;
    }

    public static class Entitlements
    {
        /// <summary>Removes interstitial, banner and app open ads (rewarded ads stay).</summary>
        public const string NoAds = "no_ads";
    }

    /// <summary>Default <see cref="IEntitlements"/>, persisted in PlayerPrefs under <c>GameLib.Entitlement.&lt;key&gt;</c>.</summary>
    public sealed class PlayerPrefsEntitlements : IEntitlements
    {
        private const string KeyPrefix = "GameLib.Entitlement.";

        private readonly Dictionary<string, bool> cache = new();

        public event Action<string, bool> Changed;

        public bool Has(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            if (!cache.TryGetValue(key, out bool owned))
            {
                owned = PlayerPrefs.GetInt(KeyPrefix + key, 0) != 0;
                cache[key] = owned;
            }
            return owned;
        }

        public void Grant(string key) => Set(key, true);

        public void Revoke(string key) => Set(key, false);

        private void Set(string key, bool owned)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Entitlement key is empty.", nameof(key));
            if (Has(key) == owned)
                return;

            cache[key] = owned;
            if (owned)
                PlayerPrefs.SetInt(KeyPrefix + key, 1);
            else
                PlayerPrefs.DeleteKey(KeyPrefix + key);
            PlayerPrefs.Save();

            Action<string, bool> handlers = Changed;
            if (handlers == null)
                return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try { ((Action<string, bool>)handler)(key, owned); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
