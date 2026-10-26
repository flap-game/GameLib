using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FlapGame.GameLib.Iap
{
    /// <summary>
    /// The store calls <see cref="IapService"/> makes, behind an interface so the purchase logic is testable without Unity
    /// IAP. The real one (<c>FlapGame.GameLib.Iap.Sdk</c>, over Unity IAP 5's <c>StoreController</c>) registers itself in
    /// <see cref="IapBackend.Factory"/> when com.unity.purchasing is installed. Callbacks come on the main thread.
    /// </summary>
    public interface IIapStore
    {
        Task ConnectAsync();

        void FetchProducts(IReadOnlyList<IapProductConfig> products);

        event Action<IReadOnlyList<IapStoreProduct>> ProductsFetched;

        event Action<string> ProductsFetchFailed;

        /// <summary>Starts the store purchase UI; false when the store does not know the product.</summary>
        bool Purchase(string productId);

        /// <summary>Paid, waiting for <see cref="Confirm"/>. Also raised at startup for purchases never confirmed.</summary>
        event Action<IapStoreOrder> PurchasePending;

        void Confirm(IapStoreOrder order);

        /// <summary>Purchase or confirmation failed.</summary>
        event Action<IapStoreOrder, IapFailure, string> PurchaseFailed;

        event Action<IapStoreOrder> PurchaseDeferred;

        /// <summary>Asks for the purchases the store already confirmed (owned non-consumables, subscriptions).</summary>
        void FetchPurchases();

        event Action<IReadOnlyList<IapStoreOrder>> PurchasesFetched;

        void Restore(Action<bool, string> callback);
    }

    public static class IapBackend
    {
        /// <summary>Creates the Unity IAP store. Set before the first scene loads when com.unity.purchasing is installed.</summary>
        public static Func<IIapStore> Factory { get; set; }
    }

    public enum IapFailure
    {
        Unknown = 0,
        UserCancelled = 1,
        PaymentDeclined = 2,
        ProductUnavailable = 3,
        PurchasingUnavailable = 4,
        ExistingPurchasePending = 5,
        DuplicateTransaction = 6,
        StoreNotConnected = 7,
    }

    public sealed class IapStoreProduct
    {
        public string Id;
        public bool Available;
        public string PriceString;
        public decimal Price;
        public string Currency;
        public string Title;
        public string Description;
    }

    /// <summary>A store order: one product (GameLib never builds multi-item carts).</summary>
    public sealed class IapStoreOrder
    {
        public string ProductId;
        public string TransactionId;
        public string Receipt;

        /// <summary>For subscriptions: false when the store says it is not active; null when it cannot tell.</summary>
        public bool? SubscriptionActive;

        /// <summary>The SDK's order object, handed back to <see cref="IIapStore.Confirm"/>.</summary>
        public object Native;

        public override string ToString() => $"{ProductId} tx={TransactionId}";
    }
}
