using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Scripting;

namespace FlapGame.GameLib.Iap
{
    /// <summary>
    /// <see cref="IIapStore"/> over Unity IAP 5's <see cref="StoreController"/> (Google Play, App Store; the fake store in the
    /// Editor). Compiled only when com.unity.purchasing is installed.
    /// </summary>
    public sealed class UnityIapStore : IIapStore, IDisposable
    {
        private readonly StoreController controller;
        // Kept to unsubscribe: the controller outlives this store (a bootstrap reset creates a new one).
        private readonly Action<List<Product>> onProductsFetched;
        private readonly Action<ProductFetchFailed> onProductsFetchFailed;
        private readonly Action<PendingOrder> onPurchasePending;
        private readonly Action<FailedOrder> onPurchaseFailed;
        private readonly Action<Order> onPurchaseConfirmed;
        private readonly Action<DeferredOrder> onPurchaseDeferred;
        private readonly Action<Orders> onPurchasesFetched;
        private readonly Action<PurchasesFetchFailureDescription> onPurchasesFetchFailed;
        private bool disposed;

        public UnityIapStore()
        {
            controller = UnityIAPServices.StoreController();
            onProductsFetched = fetched => ProductsFetched?.Invoke(fetched.Select(ToProduct).ToList());
            onProductsFetchFailed = failed => ProductsFetchFailed?.Invoke(failed.FailureReason);
            onPurchasePending = order => PurchasePending?.Invoke(ToOrder(order));
            onPurchaseFailed = order => PurchaseFailed?.Invoke(ToOrder(order), Map(order.FailureReason), order.Details);
            onPurchaseConfirmed = order =>
            {
                if (order is FailedOrder failed)
                    PurchaseFailed?.Invoke(ToOrder(failed), Map(failed.FailureReason), failed.Details);
            };
            onPurchaseDeferred = order => PurchaseDeferred?.Invoke(ToOrder(order));
            // Google lists a paid order that is not acknowledged yet (e.g. the app was killed between grant and confirm)
            // under PendingOrders; it is still owned. DeferredOrders are not paid.
            onPurchasesFetched = orders =>
                PurchasesFetched?.Invoke(orders.ConfirmedOrders.Cast<Order>().Concat(orders.PendingOrders).Select(ToOrder).ToList());
            onPurchasesFetchFailed = failure =>
            {
                Debug.LogWarning($"[IAP] Fetching owned purchases failed: {failure.Message}");
                PurchasesFetchFailed?.Invoke(failure.Message);
            };

            controller.OnProductsFetched += onProductsFetched;
            controller.OnProductsFetchFailed += onProductsFetchFailed;
            controller.OnPurchasePending += onPurchasePending;
            controller.OnPurchaseFailed += onPurchaseFailed;
            controller.OnPurchaseConfirmed += onPurchaseConfirmed;
            controller.OnPurchaseDeferred += onPurchaseDeferred;
            controller.OnPurchasesFetched += onPurchasesFetched;
            controller.OnPurchasesFetchFailed += onPurchasesFetchFailed;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            controller.OnProductsFetched -= onProductsFetched;
            controller.OnProductsFetchFailed -= onProductsFetchFailed;
            controller.OnPurchasePending -= onPurchasePending;
            controller.OnPurchaseFailed -= onPurchaseFailed;
            controller.OnPurchaseConfirmed -= onPurchaseConfirmed;
            controller.OnPurchaseDeferred -= onPurchaseDeferred;
            controller.OnPurchasesFetched -= onPurchasesFetched;
            controller.OnPurchasesFetchFailed -= onPurchasesFetchFailed;
        }

        public event Action<IReadOnlyList<IapStoreProduct>> ProductsFetched;
        public event Action<string> ProductsFetchFailed;
        public event Action<IapStoreOrder> PurchasePending;
        public event Action<IapStoreOrder, IapFailure, string> PurchaseFailed;
        public event Action<IapStoreOrder> PurchaseDeferred;
        public event Action<IReadOnlyList<IapStoreOrder>> PurchasesFetched;
        public event Action<string> PurchasesFetchFailed;

        public Task ConnectAsync() => controller.Connect();

        public void FetchProducts(IReadOnlyList<IapProductConfig> products)
        {
            var definitions = new List<ProductDefinition>(products.Count);
            foreach (IapProductConfig p in products)
                definitions.Add(new ProductDefinition(p.Id, StoreSpecificId(p), Map(p.Kind)));
            controller.FetchProducts(definitions);
        }

        public bool Purchase(string productId)
        {
            Product product = controller.GetProducts().FirstOrDefault(p => p.definition?.id == productId);
            if (product == null || !product.availableToPurchase) return false;
            controller.PurchaseProduct(product);
            return true;
        }

        public void Confirm(IapStoreOrder order)
        {
            if (order?.Native is PendingOrder pending) controller.ConfirmPurchase(pending);
        }

        public void FetchPurchases() => controller.FetchPurchases();

        public void Restore(Action<bool, string> callback) => controller.RestoreTransactions((ok, error) => callback?.Invoke(ok, error));

        private static string StoreSpecificId(IapProductConfig p)
        {
            string id = Application.platform switch
            {
                RuntimePlatform.Android => p.GooglePlayId,
                RuntimePlatform.IPhonePlayer or RuntimePlatform.OSXPlayer or RuntimePlatform.tvOS => p.AppStoreId,
                _ => null,
            };
            return string.IsNullOrWhiteSpace(id) ? p.Id : id.Trim();
        }

        private static ProductType Map(PurchaseProductKind kind) => kind switch
        {
            PurchaseProductKind.NonConsumable => ProductType.NonConsumable,
            PurchaseProductKind.Subscription => ProductType.Subscription,
            _ => ProductType.Consumable,
        };

        private static IapFailure Map(PurchaseFailureReason reason) => reason switch
        {
            PurchaseFailureReason.UserCancelled => IapFailure.UserCancelled,
            PurchaseFailureReason.PaymentDeclined => IapFailure.PaymentDeclined,
            PurchaseFailureReason.ProductUnavailable => IapFailure.ProductUnavailable,
            PurchaseFailureReason.PurchasingUnavailable => IapFailure.PurchasingUnavailable,
            PurchaseFailureReason.ExistingPurchasePending => IapFailure.ExistingPurchasePending,
            PurchaseFailureReason.DuplicateTransaction => IapFailure.DuplicateTransaction,
            PurchaseFailureReason.StoreNotConnected => IapFailure.StoreNotConnected,
            _ => IapFailure.Unknown,
        };

        private static IapStoreProduct ToProduct(Product p) => new()
        {
            Id = p.definition?.id,
            Available = p.availableToPurchase,
            PriceString = p.metadata?.localizedPriceString,
            Price = p.metadata?.localizedPrice ?? 0m,
            Currency = p.metadata?.isoCurrencyCode,
            Title = p.metadata?.localizedTitle,
            Description = p.metadata?.localizedDescription,
        };

        private static IapStoreOrder ToOrder(Order order)
        {
            CartItem item = order.CartOrdered?.Items()?.FirstOrDefault();
            SubscriptionInfo subscription = order.Info?.PurchasedProductInfo?.FirstOrDefault()?.subscriptionInfo;
            return new IapStoreOrder
            {
                ProductId = item?.Product?.definition?.id,
                TransactionId = order.Info?.TransactionID,
                OriginalTransactionId = order.Info?.Apple?.OriginalTransactionID,
                // Unity IAP rebuilds the receipt on every read (on iOS from the whole app receipt): only when a grant needs it.
                ReceiptSource = () => order.Info?.Receipt,
                SubscriptionActive = Read(subscription, s => s.IsSubscribed()),
                // Unity IAP fills the trial flag on Google from a developer payload that Billing 5+ never sets (always
                // false), and on Apple derives "auto-renewing" from refund / expiry only (a renewal turned off in Settings
                // still reads true): unknown rather than wrong.
                SubscriptionFreeTrial = order.Info?.Google != null ? null : Read(subscription, s => s.IsFreeTrial()),
                SubscriptionAutoRenewing = order.Info?.Apple != null ? null : Read(subscription, s => s.IsAutoRenewing()),
                SubscriptionExpiresUtc = ExpireDate(subscription),
                Native = order,
            };
        }

        private static bool? Read(SubscriptionInfo subscription, Func<SubscriptionInfo, Result> field)
        {
            if (subscription == null) return null;
            try
            {
                Result r = field(subscription);
                return r == Result.True ? true : r == Result.False ? false : null;
            }
            catch (Exception)
            {
                return null; // a receipt field this store does not carry
            }
        }

        private static DateTime? ExpireDate(SubscriptionInfo subscription)
        {
            if (subscription == null) return null;
            try
            {
                DateTime at = subscription.GetExpireDate();
                if (at == default || at == DateTime.MinValue || at == DateTime.MaxValue) return null;
                return at.Kind == DateTimeKind.Local ? at.ToUniversalTime() : DateTime.SpecifyKind(at, DateTimeKind.Utc);
            }
            catch (Exception)
            {
                return null;
            }
        }

        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register() => IapBackend.Factory = () => new UnityIapStore();
    }
}
