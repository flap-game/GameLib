using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FlapGame.GameLib.Iap
{
    /// <summary>
    /// In-app purchases by product id. Registered by <see cref="IapModule"/>; connects to the store in the background.
    /// <para>
    /// Granting: products with an <see cref="IapProductConfig.Entitlement"/> (e.g. <c>no_ads</c>) are granted by GameLib
    /// itself through <see cref="IEntitlements"/>. Everything else (gems, coins) is granted by the game in
    /// <see cref="PurchaseGranted"/> - which also delivers purchases completed while the game was closed. The store
    /// purchase is confirmed only after the handlers ran, and a transaction is never granted twice.
    /// </para>
    /// </summary>
    public interface IIapService
    {
        /// <summary>True once the store connected and the products (prices) are known.</summary>
        bool IsReady { get; }

        /// <summary>Raised once, when <see cref="IsReady"/> becomes true.</summary>
        event Action Ready;

        IReadOnlyList<IapProduct> Products { get; }

        /// <summary>
        /// Raised when <see cref="IsOwned"/> changed for a product: a purchase was granted, the owned purchases arrived from
        /// the store (after launch or a restore), a subscription expired. Refresh "owned" badges here instead of polling.
        /// </summary>
        event Action OwnedChanged;

        bool TryGetProduct(string productId, out IapProduct product);

        /// <summary>The store's localized price ("$0.99", "23.000 ₫"), or <paramref name="fallback"/> before the store answered.</summary>
        string GetPriceString(string productId, string fallback = "");

        /// <summary>True for an owned non-consumable or an active subscription.</summary>
        bool IsOwned(string productId);

        /// <summary>
        /// Buys <paramref name="productId"/>; completes when the store answered. <paramref name="placement"/> (shop, offer
        /// popup, ...) is sent with the purchase events. One purchase at a time.
        /// </summary>
        Task<IapPurchaseResult> PurchaseAsync(string productId, string placement = null);

        /// <summary>
        /// Restores non-consumables and subscriptions (App Store asks for the Apple id; Google Play just refreshes).
        /// Required as a button on iOS. True when the store answered.
        /// </summary>
        Task<bool> RestoreAsync();

        /// <summary>
        /// Raised for every purchase to grant, before the store confirms it - the game adds gems etc. here and saves.
        /// Purchases that arrive while nobody listens (at startup) are held until the first handler subscribes.
        /// </summary>
        event Action<IapPurchase> PurchaseGranted;
    }

    public sealed class IapProduct
    {
        public string Id { get; internal set; }

        public PurchaseProductKind Kind { get; internal set; }

        /// <summary>Entitlement key granted by the purchase (e.g. <c>no_ads</c>), or null.</summary>
        public string Entitlement { get; internal set; }

        /// <summary>False until the store returned the product.</summary>
        public bool IsAvailable { get; internal set; }

        public string PriceString { get; internal set; }

        public decimal Price { get; internal set; }

        public string Currency { get; internal set; }

        public string Title { get; internal set; }

        public string Description { get; internal set; }

        public override string ToString() => $"{Id} ({Kind}) {PriceString}";
    }

    /// <summary>One purchase to grant.</summary>
    public sealed class IapPurchase
    {
        public IapProduct Product { get; internal set; }

        public string ProductId => Product?.Id;

        public string TransactionId { get; internal set; }

        /// <summary>Where the purchase started; null for purchases recovered at startup or approved later.</summary>
        public string Placement { get; internal set; }

        /// <summary>True when the purchase was made in an earlier session (approved later, or the app closed before granting).</summary>
        public bool IsRecovered { get; internal set; }

        /// <summary>
        /// True for a restored purchase or a subscription renewal (App Store: another transaction of an earlier one). It is
        /// granted, but not reported as new revenue.
        /// </summary>
        public bool IsRestore { get; internal set; }

        public override string ToString() =>
            $"{ProductId} tx={TransactionId}{(IsRecovered ? " (recovered)" : string.Empty)}{(IsRestore ? " (restore)" : string.Empty)}";
    }

    public enum IapPurchaseStatus
    {
        Success = 0,
        Cancelled = 1,
        Failed = 2,
        /// <summary>Waiting for approval (Ask to Buy, pending payment); granted through <see cref="IIapService.PurchaseGranted"/> later.</summary>
        Deferred = 3,
        AlreadyOwned = 4,
        /// <summary>The store is not connected yet, or the product is unknown to it.</summary>
        NotReady = 5,
    }

    public readonly struct IapPurchaseResult
    {
        public IapPurchaseResult(IapPurchaseStatus status, string productId, string transactionId = null, string message = null)
        {
            Status = status;
            ProductId = productId;
            TransactionId = transactionId;
            Message = message;
        }

        public IapPurchaseStatus Status { get; }

        public string ProductId { get; }

        public string TransactionId { get; }

        /// <summary>The store's failure reason, for logs.</summary>
        public string Message { get; }

        public bool IsSuccess => Status == IapPurchaseStatus.Success;

        public override string ToString() => $"{ProductId}: {Status}{(Message != null ? $" ({Message})" : string.Empty)}";
    }

    [Serializable]
    public sealed class IapProductConfig
    {
        public string Id;

        public PurchaseProductKind Kind;

        [UnityEngine.Tooltip("Entitlement granted by GameLib on purchase and restore, e.g. no_ads. Empty = the game grants in PurchaseGranted.")]
        public string Entitlement;

        [UnityEngine.Tooltip("Google Play id when it differs from Id.")]
        public string GooglePlayId;

        [UnityEngine.Tooltip("App Store id when it differs from Id.")]
        public string AppStoreId;
    }
}
