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
    public sealed class UnityIapStore : IIapStore
    {
        private readonly StoreController controller;

        public UnityIapStore()
        {
            controller = UnityIAPServices.StoreController();
            controller.OnProductsFetched += fetched => ProductsFetched?.Invoke(fetched.Select(ToProduct).ToList());
            controller.OnProductsFetchFailed += failed => ProductsFetchFailed?.Invoke(failed.FailureReason);
            controller.OnPurchasePending += order => PurchasePending?.Invoke(ToOrder(order));
            controller.OnPurchaseFailed += order => PurchaseFailed?.Invoke(ToOrder(order), Map(order.FailureReason), order.Details);
            controller.OnPurchaseConfirmed += order =>
            {
                if (order is FailedOrder failed)
                    PurchaseFailed?.Invoke(ToOrder(failed), Map(failed.FailureReason), failed.Details);
            };
            controller.OnPurchaseDeferred += order => PurchaseDeferred?.Invoke(ToOrder(order));
            // Google lists a paid order that is not acknowledged yet (e.g. the app was killed between grant and confirm)
            // under PendingOrders; it is still owned. DeferredOrders are not paid.
            controller.OnPurchasesFetched += orders =>
                PurchasesFetched?.Invoke(orders.ConfirmedOrders.Cast<Order>().Concat(orders.PendingOrders).Select(ToOrder).ToList());
            controller.OnPurchasesFetchFailed += failure =>
            {
                Debug.LogWarning($"[IAP] Fetching owned purchases failed: {failure.Message}");
                PurchasesFetchFailed?.Invoke(failure.Message);
            };
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
            Result? subscribed = subscription?.IsSubscribed();
            return new IapStoreOrder
            {
                ProductId = item?.Product?.definition?.id,
                TransactionId = order.Info?.TransactionID,
                OriginalTransactionId = order.Info?.Apple?.OriginalTransactionID,
                Receipt = order.Info?.Receipt,
                SubscriptionActive = subscribed == Result.True ? true : subscribed == Result.False ? false : null,
                Native = order,
            };
        }

        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register() => IapBackend.Factory = () => new UnityIapStore();
    }
}
