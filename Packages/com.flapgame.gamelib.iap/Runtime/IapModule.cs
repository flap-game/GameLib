using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Iap
{
    /// <summary>
    /// Registers <see cref="IIapService"/> and connects to the store (Unity IAP 5) in the background. List every product
    /// here with its type; give non-consumables such as "remove ads" the entitlement they grant (<c>no_ads</c>).
    /// </summary>
    [CreateAssetMenu(menuName = "GameLib/Modules/IAP", fileName = "IapModule")]
    public sealed class IapModule : GameLibModule
    {
        [Tooltip("Every product the game sells. Ids as in Google Play Console / App Store Connect (or set per-store ids).")]
        public List<IapProductConfig> Products = new()
        {
            new IapProductConfig { Id = "no_ads", Kind = PurchaseProductKind.NonConsumable, Entitlement = FlapGame.GameLib.Entitlements.NoAds },
        };

        public override int Order => GameLibModuleOrder.Iap;

        public override ValueTask InstallAsync(IServiceRegistry services, CancellationToken cancellationToken = default)
        {
            IIapStore store = null;
            Func<IIapStore> factory = IapBackend.Factory;
            if (factory != null) store = factory();
            else Debug.LogWarning("[IAP] Unity IAP (com.unity.purchasing) is not installed - purchases are unavailable.");

            services.TryResolve(out IAppLifecycle lifecycle);
            var service = new IapService(store, Products, services.Resolve<IEntitlements>(), services.Resolve<IAnalytics>(), lifecycle);
            services.Register<IIapService>(service);
            services.RegisterDisposable(service);
            _ = service.InitializeAsync();
            return default;
        }

        public override void Validate(ICollection<GameLibIssue> issues)
        {
            if (Type.GetType("UnityEngine.Purchasing.StoreController, Unity.Purchasing") == null)
                issues.Add(GameLibIssue.Error("IAP: Unity IAP (com.unity.purchasing) is not installed - install it from GameLib > Setup."));

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (IapProductConfig p in Products)
            {
                if (p == null) continue;
                if (string.IsNullOrWhiteSpace(p.Id))
                {
                    issues.Add(GameLibIssue.Error("IAP: a product has no id."));
                    continue;
                }
                if (!ids.Add(p.Id))
                    issues.Add(GameLibIssue.Error($"IAP: product '{p.Id}' is listed twice."));
                if (p.Kind == PurchaseProductKind.Consumable && !string.IsNullOrWhiteSpace(p.Entitlement))
                    issues.Add(GameLibIssue.Warning($"IAP: consumable '{p.Id}' grants entitlement '{p.Entitlement}' - entitlements are for non-consumables and subscriptions."));
            }
        }
    }
}
