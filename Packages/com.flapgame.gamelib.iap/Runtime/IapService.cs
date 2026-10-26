using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Iap
{
    /// <summary>
    /// <see cref="IIapService"/> over an <see cref="IIapStore"/>. Grant before confirm, each transaction once (remembered
    /// across launches by <see cref="IIapTransactionLog"/>), entitlements kept in sync with what the store says is owned,
    /// purchase events to <see cref="IAnalytics"/>, and the purchase UI wrapped in <c>IAppLifecycle.BeginExternalActivity</c>
    /// so coming back from it is not counted as a return to the app (no app open ad).
    /// </summary>
    public sealed class IapService : IIapService, IDisposable
    {
        public const string PurchaseSuccessEvent = "iap_purchase_success";
        public const string PurchaseFailedEvent = "iap_purchase_failed";
        public const string PurchaseCancelledEvent = "iap_purchase_cancelled";
        public const string PurchaseDeferredEvent = "iap_purchase_deferred";
        public const string RestoreEvent = "iap_restore";

        private sealed class InFlight
        {
            public string ProductId;
            public string Placement;
            public TaskCompletionSource<IapPurchaseResult> Result;
            public IDisposable ExternalActivity;
        }

        private readonly IIapStore store;
        private readonly IEntitlements entitlements;
        private readonly IAnalytics analytics;
        private readonly IAppLifecycle lifecycle;
        private readonly IIapTransactionLog grantedLog;
        private readonly Dictionary<string, IapProduct> products = new(StringComparer.Ordinal);
        private readonly List<IapProductConfig> configs;
        private readonly HashSet<string> owned = new(StringComparer.Ordinal);
        private readonly List<(IapStoreOrder Order, IapPurchase Purchase)> held = new();
        private Action<IapPurchase> grantedHandlers;
        private InFlight inFlight;
        private bool disposed;

        public IapService(IIapStore store, IEnumerable<IapProductConfig> configs, IEntitlements entitlements, IAnalytics analytics,
                          IAppLifecycle lifecycle = null, IIapTransactionLog grantedLog = null)
        {
            this.store = store;
            this.entitlements = entitlements;
            this.analytics = analytics;
            this.lifecycle = lifecycle;
            this.grantedLog = grantedLog ?? new PlayerPrefsTransactionLog();
            this.configs = (configs ?? Enumerable.Empty<IapProductConfig>()).Where(c => c != null && !string.IsNullOrWhiteSpace(c.Id)).ToList();
            foreach (IapProductConfig c in this.configs)
                products[c.Id] = new IapProduct { Id = c.Id, Kind = c.Kind, Entitlement = string.IsNullOrWhiteSpace(c.Entitlement) ? null : c.Entitlement.Trim() };

            // A non-consumable bought earlier is owned from the first frame, before the store answers.
            foreach (IapProduct p in products.Values)
                if (p.Kind == PurchaseProductKind.NonConsumable && p.Entitlement != null && entitlements != null && entitlements.Has(p.Entitlement))
                    owned.Add(p.Id);

            if (store == null) return;
            store.ProductsFetched += OnProductsFetched;
            store.ProductsFetchFailed += OnProductsFetchFailed;
            store.PurchasePending += OnPurchasePending;
            store.PurchaseFailed += OnPurchaseFailed;
            store.PurchaseDeferred += OnPurchaseDeferred;
            store.PurchasesFetched += OnPurchasesFetched;
        }

        public bool IsReady { get; private set; }

        public event Action Ready;

        public IReadOnlyList<IapProduct> Products => products.Values.ToList();

        public event Action<IapPurchase> PurchaseGranted
        {
            add
            {
                grantedHandlers += value;
                if (value == null || held.Count == 0) return;
                var waiting = held.ToList();
                held.Clear();
                foreach ((IapStoreOrder order, IapPurchase purchase) in waiting)
                    Grant(order, purchase);
            }
            remove => grantedHandlers -= value;
        }

        /// <summary>Connects and fetches the products; the rest follows from the store's callbacks. Never throws.</summary>
        public async Task InitializeAsync()
        {
            if (store == null) return;
            try
            {
                await store.ConnectAsync();
                if (disposed) return;
                store.FetchProducts(configs);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[IAP] Store connection failed: {e.Message}. Purchases are unavailable this session.");
            }
        }

        public bool TryGetProduct(string productId, out IapProduct product) =>
            products.TryGetValue(productId ?? string.Empty, out product);

        public string GetPriceString(string productId, string fallback = "") =>
            TryGetProduct(productId, out IapProduct p) && p.IsAvailable && !string.IsNullOrEmpty(p.PriceString) ? p.PriceString : fallback;

        public bool IsOwned(string productId) => productId != null && owned.Contains(productId);

        public Task<IapPurchaseResult> PurchaseAsync(string productId, string placement = null)
        {
            if (!TryGetProduct(productId, out IapProduct product))
                return Task.FromResult(new IapPurchaseResult(IapPurchaseStatus.NotReady, productId, message: "not in the IAP module's product list"));
            if (inFlight != null)
                return Task.FromResult(new IapPurchaseResult(IapPurchaseStatus.Failed, productId, message: "another purchase is in progress"));
            if (product.Kind == PurchaseProductKind.NonConsumable && IsOwned(productId))
                return Task.FromResult(new IapPurchaseResult(IapPurchaseStatus.AlreadyOwned, productId));
            if (store == null || !IsReady || !product.IsAvailable)
                return Task.FromResult(new IapPurchaseResult(IapPurchaseStatus.NotReady, productId, message: "the store has not returned this product"));

            inFlight = new InFlight
            {
                ProductId = productId,
                Placement = placement,
                Result = new TaskCompletionSource<IapPurchaseResult>(),
                ExternalActivity = lifecycle?.BeginExternalActivity("purchase"),
            };
            Task<IapPurchaseResult> task = inFlight.Result.Task;
            if (!store.Purchase(productId))
                Complete(productId, IapPurchaseStatus.NotReady, null, "the store does not know this product");
            return task;
        }

        public Task<bool> RestoreAsync()
        {
            if (store == null || !IsReady) return Task.FromResult(false);
            var result = new TaskCompletionSource<bool>();
            IDisposable scope = lifecycle?.BeginExternalActivity("purchase");
            store.Restore((success, error) =>
            {
                scope?.Dispose();
                analytics?.Track(RestoreEvent, ("success", success), ("error", error ?? string.Empty));
                if (success) store.FetchPurchases();
                else Debug.LogWarning($"[IAP] Restore failed: {error}");
                result.TrySetResult(success);
            });
            return result.Task;
        }

        // ── Store callbacks ─────────────────────────────────────────────

        private void OnProductsFetched(IReadOnlyList<IapStoreProduct> fetched)
        {
            foreach (IapStoreProduct s in fetched)
            {
                if (s == null || !products.TryGetValue(s.Id ?? string.Empty, out IapProduct p)) continue;
                p.IsAvailable = s.Available;
                p.PriceString = s.PriceString;
                p.Price = s.Price;
                p.Currency = s.Currency;
                p.Title = s.Title;
                p.Description = s.Description;
            }

            bool first = !IsReady;
            IsReady = true;
            store.FetchPurchases();
            if (first) Ready?.Invoke();
        }

        private void OnProductsFetchFailed(string reason) => Debug.LogWarning($"[IAP] Products could not be fetched: {reason}");

        private void OnPurchasesFetched(IReadOnlyList<IapStoreOrder> confirmed)
        {
            foreach (IapProduct p in products.Values)
            {
                if (p.Kind == PurchaseProductKind.Consumable) continue;
                bool isOwned = confirmed.Any(o => o.ProductId == p.Id && (p.Kind != PurchaseProductKind.Subscription || o.SubscriptionActive != false));
                if (isOwned)
                {
                    owned.Add(p.Id);
                    if (p.Entitlement != null) entitlements?.Grant(p.Entitlement);
                }
                else if (p.Kind == PurchaseProductKind.Subscription)
                {
                    // An expired subscription stops granting; a non-consumable is never taken back here.
                    owned.Remove(p.Id);
                    if (p.Entitlement != null) entitlements?.Revoke(p.Entitlement);
                }
            }
        }

        private void OnPurchasePending(IapStoreOrder order)
        {
            if (order == null) return;
            if (!products.TryGetValue(order.ProductId ?? string.Empty, out IapProduct product))
            {
                Debug.LogWarning($"[IAP] Purchase of '{order.ProductId}', which is not in the product list - confirmed without granting.");
                store.Confirm(order);
                return;
            }

            bool fromThisPurchase = inFlight != null && inFlight.ProductId == product.Id;
            var purchase = new IapPurchase
            {
                Product = product,
                TransactionId = order.TransactionId,
                Placement = fromThisPurchase ? inFlight.Placement : null,
                IsRecovered = !fromThisPurchase,
            };

            if (!string.IsNullOrEmpty(order.TransactionId) && grantedLog.Contains(order.TransactionId))
            {
                // Granted before the app closed but never confirmed: confirm only.
                store.Confirm(order);
                Complete(product.Id, IapPurchaseStatus.Success, order.TransactionId, null);
                return;
            }

            if (product.Entitlement == null && grantedHandlers == null)
            {
                Debug.LogWarning($"[IAP] {purchase} waits for a PurchaseGranted handler before it is granted and confirmed.");
                held.Add((order, purchase));
                Complete(product.Id, IapPurchaseStatus.Success, order.TransactionId, "held until a PurchaseGranted handler subscribes");
                return;
            }

            Grant(order, purchase);
        }

        private void Grant(IapStoreOrder order, IapPurchase purchase)
        {
            IapProduct product = purchase.Product;
            if (product.Kind != PurchaseProductKind.Consumable) owned.Add(product.Id);
            if (product.Entitlement != null) entitlements?.Grant(product.Entitlement);

            Action<IapPurchase> handlers = grantedHandlers;
            if (handlers != null)
                foreach (Delegate handler in handlers.GetInvocationList())
                {
                    try { ((Action<IapPurchase>)handler)(purchase); }
                    catch (Exception e) { Debug.LogException(e); }
                }

            if (!string.IsNullOrEmpty(order.TransactionId)) grantedLog.Add(order.TransactionId);
            TrackSuccess(purchase, order);
            store.Confirm(order);
            Complete(product.Id, IapPurchaseStatus.Success, order.TransactionId, null);
        }

        private void OnPurchaseFailed(IapStoreOrder order, IapFailure failure, string message)
        {
            string productId = order?.ProductId ?? inFlight?.ProductId;
            if (order != null && !string.IsNullOrEmpty(order.TransactionId) && grantedLog.Contains(order.TransactionId))
            {
                // The confirmation failed after the grant: the store delivers it again next launch and it is only confirmed then.
                Debug.LogWarning($"[IAP] Confirming {order} failed ({failure}: {message}); retried next launch.");
                return;
            }

            string placement = inFlight != null && inFlight.ProductId == productId ? inFlight.Placement : null;
            IapPurchaseStatus status;
            if (failure == IapFailure.UserCancelled)
            {
                status = IapPurchaseStatus.Cancelled;
                analytics?.Track(PurchaseCancelledEvent, ("product_id", productId ?? string.Empty), ("placement", placement ?? string.Empty));
            }
            else
            {
                bool alreadyOwned = failure == IapFailure.DuplicateTransaction && TryGetProduct(productId, out IapProduct p) && p.Kind != PurchaseProductKind.Consumable;
                status = alreadyOwned ? IapPurchaseStatus.AlreadyOwned : IapPurchaseStatus.Failed;
                if (alreadyOwned) store.FetchPurchases();
                analytics?.Track(PurchaseFailedEvent, ("product_id", productId ?? string.Empty), ("reason", failure.ToString()),
                                 ("message", message ?? string.Empty), ("placement", placement ?? string.Empty));
            }
            Complete(productId, status, order?.TransactionId, message ?? failure.ToString());
        }

        private void OnPurchaseDeferred(IapStoreOrder order)
        {
            string productId = order?.ProductId ?? inFlight?.ProductId;
            analytics?.Track(PurchaseDeferredEvent, ("product_id", productId ?? string.Empty));
            Complete(productId, IapPurchaseStatus.Deferred, order?.TransactionId, "waiting for approval");
        }

        private void Complete(string productId, IapPurchaseStatus status, string transactionId, string message)
        {
            InFlight current = inFlight;
            if (current == null || (productId != null && current.ProductId != productId)) return;
            inFlight = null;
            current.ExternalActivity?.Dispose();
            current.Result.TrySetResult(new IapPurchaseResult(status, current.ProductId, transactionId, message));
        }

        private void TrackSuccess(IapPurchase purchase, IapStoreOrder order)
        {
            if (analytics == null) return;
            IapProduct p = purchase.Product;
            analytics.TrackPurchase(new PurchaseRecord
            {
                ProductId = p.Id,
                ProductKind = p.Kind,
                Price = p.Price,
                Currency = p.Currency,
                TransactionId = order.TransactionId,
                Placement = purchase.Placement,
                Receipt = order.Receipt,
            });
            analytics.Track(PurchaseSuccessEvent,
                            ("product_id", p.Id),
                            ("product_type", p.Kind.ToString()),
                            ("price", (double)p.Price),
                            ("currency", p.Currency ?? string.Empty),
                            ("price_string", p.PriceString ?? string.Empty),
                            ("transaction_id", order.TransactionId ?? string.Empty),
                            ("placement", purchase.Placement ?? string.Empty),
                            ("recovered", purchase.IsRecovered));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (store != null)
            {
                store.ProductsFetched -= OnProductsFetched;
                store.ProductsFetchFailed -= OnProductsFetchFailed;
                store.PurchasePending -= OnPurchasePending;
                store.PurchaseFailed -= OnPurchaseFailed;
                store.PurchaseDeferred -= OnPurchaseDeferred;
                store.PurchasesFetched -= OnPurchasesFetched;
            }
            Complete(null, IapPurchaseStatus.Failed, null, "IAP shut down");
        }
    }
}
