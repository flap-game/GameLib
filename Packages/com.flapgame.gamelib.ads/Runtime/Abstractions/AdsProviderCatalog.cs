using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Process-wide list of ad providers compiled into this build. Provider assemblies register their factory at
    /// startup; the ads installer picks from it. Registering the same id again replaces the earlier factory.
    /// </summary>
    public static class AdsProviderCatalog
    {
        private static readonly Dictionary<string, IAdsProviderFactory> factories = new();

        public static void Register(IAdsProviderFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            string id = AdProviderIds.Normalize(factory.ProviderId);
            lock (factories)
            {
                if (factories.ContainsKey(id))
                    Debug.Log($"[Ads] Provider '{id}' registered again - replacing the earlier factory.");
                factories[id] = factory;
            }
        }

        public static bool Unregister(string providerId)
        {
            lock (factories) return factories.Remove(AdProviderIds.Normalize(providerId));
        }

        public static bool TryGet(string providerId, out IAdsProviderFactory factory)
        {
            lock (factories) return factories.TryGetValue(AdProviderIds.Normalize(providerId), out factory);
        }

        public static bool Contains(string providerId)
        {
            lock (factories) return factories.ContainsKey(AdProviderIds.Normalize(providerId));
        }

        /// <summary>Snapshot of every registered factory.</summary>
        public static IReadOnlyList<IAdsProviderFactory> All
        {
            get { lock (factories) return new List<IAdsProviderFactory>(factories.Values); }
        }

        /// <summary>Tests only: forget every factory.</summary>
        public static void ClearForTests()
        {
            lock (factories) factories.Clear();
        }
    }
}
