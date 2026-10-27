using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;

namespace FlapGame.GameLib.Iap.Tests
{
    public class IapServiceTests
    {
        private sealed class FakeStore : IIapStore
        {
            public readonly List<string> Purchases = new();
            public readonly List<IapStoreOrder> Confirmed = new();
            public int FetchPurchasesCalls;
            public Action<bool, string> RestoreCallback;

            public event Action<IReadOnlyList<IapStoreProduct>> ProductsFetched;
            public event Action<string> ProductsFetchFailed;
            public event Action<IapStoreOrder> PurchasePending;
            public event Action<IapStoreOrder, IapFailure, string> PurchaseFailed;
            public event Action<IapStoreOrder> PurchaseDeferred;
            public event Action<IReadOnlyList<IapStoreOrder>> PurchasesFetched;
            public event Action<string> PurchasesFetchFailed;

            public Task ConnectAsync() => Task.CompletedTask;

            public void FetchProducts(IReadOnlyList<IapProductConfig> products) =>
                ProductsFetched?.Invoke(products.Select(p => new IapStoreProduct
                {
                    Id = p.Id, Available = true, PriceString = "$1.99", Price = 1.99m, Currency = "USD", Title = p.Id,
                }).ToList());

            public bool Purchase(string productId)
            {
                Purchases.Add(productId);
                return true;
            }

            public void Confirm(IapStoreOrder order) => Confirmed.Add(order);
            public void FetchPurchases() => FetchPurchasesCalls++;
            public void Restore(Action<bool, string> callback) => RestoreCallback = callback;

            public void Pending(string productId, string tx) => PurchasePending?.Invoke(new IapStoreOrder { ProductId = productId, TransactionId = tx, Receipt = "r" });
            public void PendingOrder(IapStoreOrder order) => PurchasePending?.Invoke(order);
            public void Fail(string productId, IapFailure failure) => PurchaseFailed?.Invoke(new IapStoreOrder { ProductId = productId }, failure, "details");
            public void Defer(string productId) => PurchaseDeferred?.Invoke(new IapStoreOrder { ProductId = productId });
            public void Owned(params IapStoreOrder[] orders) => PurchasesFetched?.Invoke(orders);
            public void FailFetch() => ProductsFetchFailed?.Invoke("x");
            public void FailPurchasesFetch() => PurchasesFetchFailed?.Invoke("offline");
        }

        private sealed class MemoryEntitlements : IEntitlements
        {
            public readonly HashSet<string> Keys = new();
            public event Action<string, bool> Changed;
            public bool Has(string key) => Keys.Contains(key);
            public void Grant(string key) { if (Keys.Add(key)) Changed?.Invoke(key, true); }
            public void Revoke(string key) { if (Keys.Remove(key)) Changed?.Invoke(key, false); }
        }

        private sealed class RecordingSink : IAnalyticsSink
        {
            public readonly List<AnalyticsEvent> Events = new();
            public readonly List<PurchaseRecord> Purchases = new();
            public void Track(AnalyticsEvent analyticsEvent) => Events.Add(analyticsEvent);
            public void SetUserProperty(string name, string value) { }
            public void SetUserId(string userId) { }
            public void TrackAdRevenue(AdRevenue revenue) { }
            public void TrackPurchase(PurchaseRecord purchase) => Purchases.Add(purchase);
        }

        private FakeStore store;
        private MemoryEntitlements entitlements;
        private RecordingSink sink;
        private MemoryTransactionLog log;
        private IapService service;

        private static readonly IapProductConfig[] Catalog =
        {
            new() { Id = "gems_100", Kind = PurchaseProductKind.Consumable },
            new() { Id = "no_ads", Kind = PurchaseProductKind.NonConsumable, Entitlement = Entitlements.NoAds },
            new() { Id = "vip", Kind = PurchaseProductKind.Subscription, Entitlement = "vip" },
        };

        [SetUp]
        public void SetUp()
        {
            store = new FakeStore();
            entitlements = new MemoryEntitlements();
            sink = new RecordingSink();
            var analytics = new AnalyticsService();
            analytics.AddSink(sink);
            log = new MemoryTransactionLog();
            service = new IapService(store, Catalog, entitlements, analytics, null, log);
        }

        [TearDown]
        public void TearDown() => service.Dispose();

        [Test]
        public async Task SharedEntitlement_StaysWhileAnotherProductGrantsIt()
        {
            IapProductConfig[] vipPlans =
            {
                new() { Id = "vip_week", Kind = PurchaseProductKind.Subscription, Entitlement = "vip" },
                new() { Id = "vip_year", Kind = PurchaseProductKind.Subscription, Entitlement = "vip" },
            };
            using var plans = new IapService(store, vipPlans, entitlements, null, null, new MemoryTransactionLog());
            await plans.InitializeAsync();

            store.Owned(new IapStoreOrder { ProductId = "vip_week", TransactionId = "w", SubscriptionActive = false },
                        new IapStoreOrder { ProductId = "vip_year", TransactionId = "y", SubscriptionActive = true });
            Assert.That(entitlements.Has("vip"), Is.True, "the expired weekly plan must not take VIP from the yearly one");
            Assert.That(plans.IsOwned("vip_week"), Is.False);

            store.Owned(new IapStoreOrder { ProductId = "vip_year", TransactionId = "y", SubscriptionActive = false });
            Assert.That(entitlements.Has("vip"), Is.False, "no plan left");
        }

        [Test]
        public async Task Subscription_Details_FromTheStore()
        {
            await service.InitializeAsync();
            var expires = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
            store.Owned(new IapStoreOrder { ProductId = "vip", TransactionId = "a", SubscriptionActive = true,
                                            SubscriptionExpiresUtc = expires.AddDays(-7), SubscriptionFreeTrial = false },
                        new IapStoreOrder { ProductId = "vip", TransactionId = "b", SubscriptionActive = true,
                                            SubscriptionExpiresUtc = expires, SubscriptionFreeTrial = true, SubscriptionAutoRenewing = true });

            Assert.That(service.TryGetSubscription("vip", out IapSubscriptionInfo info), Is.True);
            Assert.That(info.ExpiresUtc, Is.EqualTo(expires), "the order that runs longest");
            Assert.That(info.IsFreeTrial, Is.True);
            Assert.That(info.IsAutoRenewing, Is.True);
            Assert.That(service.TryGetSubscription("no_ads", out _), Is.False, "not a subscription");

            store.Owned(new IapStoreOrder { ProductId = "vip", TransactionId = "b", SubscriptionActive = false });
            Assert.That(service.TryGetSubscription("vip", out _), Is.False, "expired");
        }

        [Test]
        public async Task Initialize_FetchesProducts_WithLocalizedPrices_ThenOwnedPurchases()
        {
            bool ready = false;
            service.Ready += () => ready = true;
            Assert.That(service.GetPriceString("gems_100", "-"), Is.EqualTo("-"));

            await service.InitializeAsync();

            Assert.That(ready, Is.True);
            Assert.That(service.IsReady, Is.True);
            Assert.That(service.GetPriceString("gems_100"), Is.EqualTo("$1.99"));
            Assert.That(store.FetchPurchasesCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task ConsumablePurchase_IsGrantedByTheHandler_BeforeConfirm_AndTracked()
        {
            await service.InitializeAsync();
            var granted = new List<IapPurchase>();
            service.PurchaseGranted += p =>
            {
                granted.Add(p);
                Assert.That(store.Confirmed, Is.Empty, "granted before the store confirmation");
            };

            Task<IapPurchaseResult> purchase = service.PurchaseAsync("gems_100", "shop");
            store.Pending("gems_100", "tx1");
            IapPurchaseResult result = await purchase;

            Assert.That(result.Status, Is.EqualTo(IapPurchaseStatus.Success));
            Assert.That(granted.Single().Placement, Is.EqualTo("shop"));
            Assert.That(granted.Single().IsRecovered, Is.False);
            Assert.That(store.Confirmed.Single().TransactionId, Is.EqualTo("tx1"));
            Assert.That(sink.Purchases.Single().Price, Is.EqualTo(1.99m));
            Assert.That(sink.Purchases.Single().Placement, Is.EqualTo("shop"));
            AnalyticsEvent success = sink.Events.Single(e => e.Name == IapService.PurchaseSuccessEvent);
            Assert.That(success.Parameters["product_id"], Is.EqualTo("gems_100"));
            Assert.That(success.Parameters["transaction_id"], Is.EqualTo("tx1"));
        }

        [Test]
        public async Task SameTransactionDeliveredAgain_IsOnlyConfirmed()
        {
            await service.InitializeAsync();
            int grants = 0;
            service.PurchaseGranted += _ => grants++;

            store.Pending("gems_100", "tx1");
            store.Pending("gems_100", "tx1"); // not confirmed before the app closed: the store delivers it again

            Assert.That(grants, Is.EqualTo(1));
            Assert.That(store.Confirmed, Has.Count.EqualTo(2));
            Assert.That(sink.Purchases, Has.Count.EqualTo(1), "revenue counted once");
        }

        [Test]
        public async Task RestoreOrRenewal_IsGranted_ButNotNewRevenue()
        {
            await service.InitializeAsync();

            // App Store: a renewal / StoreKit 1 restore is a new transaction of an earlier purchase.
            store.PendingOrder(new IapStoreOrder { ProductId = "vip", TransactionId = "tx-renew", OriginalTransactionId = "tx-first", Receipt = "r" });
            Assert.That(entitlements.Has("vip"), Is.True);
            Assert.That(sink.Purchases, Is.Empty, "not new revenue");
            AnalyticsEvent success = sink.Events.Single(e => e.Name == IapService.PurchaseSuccessEvent);
            Assert.That(success.Parameters["restore"], Is.EqualTo(true));

            store.PendingOrder(new IapStoreOrder { ProductId = "no_ads", TransactionId = "tx-new", OriginalTransactionId = "tx-new", Receipt = "r" });
            Assert.That(sink.Purchases.Single().ProductId, Is.EqualTo("no_ads"), "a first purchase is revenue");
        }

        [Test]
        public async Task GrantIsLoggedBeforeTheHandlerRuns_SoARedeliveryNeverGrantsTwice()
        {
            await service.InitializeAsync();
            bool loggedDuringHandler = false;
            service.PurchaseGranted += p => loggedDuringHandler = log.Contains(p.TransactionId);

            store.Pending("gems_100", "tx-log");

            Assert.That(loggedDuringHandler, Is.True);
        }

        [Test]
        public async Task PurchaseArrivingBeforeAnyHandler_IsHeld_UntilTheGameSubscribes()
        {
            await service.InitializeAsync();
            store.Pending("gems_100", "tx-startup");
            Assert.That(store.Confirmed, Is.Empty, "not confirmed: nobody granted it yet");

            var granted = new List<IapPurchase>();
            service.PurchaseGranted += granted.Add;

            Assert.That(granted.Single().TransactionId, Is.EqualTo("tx-startup"));
            Assert.That(granted.Single().IsRecovered, Is.True);
            Assert.That(store.Confirmed, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task NonConsumable_GrantsItsEntitlement_WithoutAHandler_AndIsThenOwned()
        {
            await service.InitializeAsync();

            Task<IapPurchaseResult> purchase = service.PurchaseAsync("no_ads");
            store.Pending("no_ads", "tx2");
            Assert.That((await purchase).IsSuccess, Is.True);

            Assert.That(entitlements.Has(Entitlements.NoAds), Is.True);
            Assert.That(service.IsOwned("no_ads"), Is.True);
            Assert.That(store.Confirmed, Has.Count.EqualTo(1));
            Assert.That((await service.PurchaseAsync("no_ads")).Status, Is.EqualTo(IapPurchaseStatus.AlreadyOwned));
        }

        [Test]
        public async Task OwnedPurchases_RestoreEntitlements_AndExpiredSubscriptionsRevoke()
        {
            await service.InitializeAsync();
            entitlements.Grant("vip");

            store.Owned(new IapStoreOrder { ProductId = "no_ads", TransactionId = "a" },
                        new IapStoreOrder { ProductId = "vip", TransactionId = "b", SubscriptionActive = false });

            Assert.That(entitlements.Has(Entitlements.NoAds), Is.True);
            Assert.That(entitlements.Has("vip"), Is.False, "expired subscription");
            Assert.That(service.IsOwned("vip"), Is.False);
            Assert.That(sink.Purchases, Is.Empty, "restored purchases are not new revenue");
        }

        [Test]
        public async Task Cancel_Fail_Defer_CompleteThePurchase_AndAreTracked()
        {
            await service.InitializeAsync();

            Task<IapPurchaseResult> cancelled = service.PurchaseAsync("gems_100", "shop");
            store.Fail("gems_100", IapFailure.UserCancelled);
            Assert.That((await cancelled).Status, Is.EqualTo(IapPurchaseStatus.Cancelled));

            Task<IapPurchaseResult> failed = service.PurchaseAsync("gems_100");
            store.Fail("gems_100", IapFailure.PaymentDeclined);
            Assert.That((await failed).Status, Is.EqualTo(IapPurchaseStatus.Failed));

            Task<IapPurchaseResult> deferred = service.PurchaseAsync("gems_100");
            store.Defer("gems_100");
            Assert.That((await deferred).Status, Is.EqualTo(IapPurchaseStatus.Deferred));

            Assert.That(sink.Events.Select(e => e.Name), Is.EqualTo(new[]
            {
                IapService.PurchaseCancelledEvent, IapService.PurchaseFailedEvent, IapService.PurchaseDeferredEvent,
            }));
        }

        [Test]
        public async Task OnePurchaseAtATime_AndUnknownOrNotReadyProducts()
        {
            Assert.That((await service.PurchaseAsync("gems_100")).Status, Is.EqualTo(IapPurchaseStatus.NotReady), "store not connected");
            Assert.That((await service.PurchaseAsync("nope")).Status, Is.EqualTo(IapPurchaseStatus.NotReady));

            await service.InitializeAsync();
            Task<IapPurchaseResult> first = service.PurchaseAsync("gems_100");
            Assert.That((await service.PurchaseAsync("no_ads")).Status, Is.EqualTo(IapPurchaseStatus.Failed));
            store.Fail("gems_100", IapFailure.UserCancelled);
            await first;
            Assert.That(store.Purchases, Is.EqualTo(new[] { "gems_100" }));
        }

        [Test]
        public async Task Restore_CompletesAfterTheRestoredPurchasesAreApplied()
        {
            await service.InitializeAsync();
            int before = store.FetchPurchasesCalls;

            Task<bool> restore = service.RestoreAsync();
            store.RestoreCallback(true, null);
            Assert.That(restore.IsCompleted, Is.False, "waits for the purchase list the store fetches after a restore");
            store.Owned(new IapStoreOrder { ProductId = "no_ads", TransactionId = "a" });

            Assert.That(await restore, Is.True);
            Assert.That(service.IsOwned("no_ads"), Is.True, "owned as soon as the restore returns");
            Assert.That(store.FetchPurchasesCalls, Is.EqualTo(before), "the store already fetches after a restore");
            Assert.That(sink.Events.Last().Name, Is.EqualTo(IapService.RestoreEvent));
        }

        [Test]
        public async Task Restore_FailedOrFetchFailed_ReturnsFalse_AndEndsTheExternalActivity()
        {
            var lifecycle = new FakeLifecycle();
            using var withLifecycle = new IapService(store, Catalog, entitlements, null, lifecycle, new MemoryTransactionLog());
            await withLifecycle.InitializeAsync();

            Task<bool> failed = withLifecycle.RestoreAsync();
            Assert.That(lifecycle.Open, Is.EqualTo(1));
            LogAssert.Expect(LogType.Warning, new Regex("Restore failed"));
            store.RestoreCallback(false, "cancelled");
            Assert.That(await failed, Is.False);
            Assert.That(lifecycle.Open, Is.EqualTo(0));

            Task<bool> fetchFailed = withLifecycle.RestoreAsync();
            store.RestoreCallback(true, null);
            store.FailPurchasesFetch();
            Assert.That(await fetchFailed, Is.False);
            Assert.That(lifecycle.Open, Is.EqualTo(0));
        }

        [Test]
        public async Task Restore_FetchLandingBeforeTheRestoreAnswer_IsNotMissed()
        {
            await service.InitializeAsync();

            Task<bool> restore = service.RestoreAsync();
            store.FailPurchasesFetch(); // Unity IAP fetches before it calls the restore back; that fetch can finish first
            store.RestoreCallback(true, null);

            Assert.That(restore.IsCompleted, Is.True);
            Assert.That(await restore, Is.False);
        }

        [Test]
        public async Task Restore_StoreThrows_ReturnsFalse_AndAllowsANewRestore()
        {
            var throwing = new ThrowingRestoreStore();
            using var withThrowing = new IapService(throwing, Catalog, entitlements, null, null, new MemoryTransactionLog());
            await withThrowing.InitializeAsync();

            LogAssert.Expect(LogType.Warning, new Regex("Restore failed"));
            Assert.That(await withThrowing.RestoreAsync(), Is.False);
            LogAssert.Expect(LogType.Warning, new Regex("Restore failed"));
            Assert.That(await withThrowing.RestoreAsync(), Is.False, "a new restore, not the stuck one");
        }

        private sealed class ThrowingRestoreStore : IIapStore
        {
            private readonly FakeStore inner = new();
            public event Action<IReadOnlyList<IapStoreProduct>> ProductsFetched { add => inner.ProductsFetched += value; remove => inner.ProductsFetched -= value; }
            public event Action<string> ProductsFetchFailed { add { } remove { } }
            public event Action<IapStoreOrder> PurchasePending { add { } remove { } }
            public event Action<IapStoreOrder, IapFailure, string> PurchaseFailed { add { } remove { } }
            public event Action<IapStoreOrder> PurchaseDeferred { add { } remove { } }
            public event Action<IReadOnlyList<IapStoreOrder>> PurchasesFetched { add { } remove { } }
            public event Action<string> PurchasesFetchFailed { add { } remove { } }
            public Task ConnectAsync() => Task.CompletedTask;
            public void FetchProducts(IReadOnlyList<IapProductConfig> products) => inner.FetchProducts(products);
            public bool Purchase(string productId) => false;
            public void Confirm(IapStoreOrder order) { }
            public void FetchPurchases() { }
            public void Restore(Action<bool, string> callback) => throw new InvalidOperationException("store gone");
        }

        [Test]
        public async Task Restore_NoPurchaseList_ReturnsAfterTheTimeout()
        {
            var timeout = new TaskCompletionSource<bool>();
            using var withTimer = new IapService(store, Catalog, entitlements, null, null, new MemoryTransactionLog(), _ => timeout.Task);
            await withTimer.InitializeAsync();

            Task<bool> restore = withTimer.RestoreAsync();
            Assert.That(withTimer.RestoreAsync(), Is.SameAs(restore), "one restore at a time");
            store.RestoreCallback(true, null);
            Assert.That(restore.IsCompleted, Is.False);

            LogAssert.Expect(LogType.Warning, new Regex("no purchase list"));
            timeout.SetResult(true);
            Assert.That(await restore, Is.True);
        }

        [Test]
        public async Task PaidButUnconfirmedSubscription_RecoveredAtStartup_StaysOwned()
        {
            // Unity IAP 5 on Google: an order never acknowledged (app killed between grant and confirm) is fetched as a
            // pending order - routed to PurchasePending first, then listed with the paid orders.
            await service.InitializeAsync();
            var order = new IapStoreOrder { ProductId = "vip", TransactionId = "tx-sub", Receipt = "r" };

            store.Pending(order.ProductId, order.TransactionId);
            store.Owned(order);

            Assert.That(entitlements.Has("vip"), Is.True);
            Assert.That(service.IsOwned("vip"), Is.True);
        }

        [Test]
        public void PurchaseWrapsTheStoreUi_InAnExternalActivity()
        {
            var lifecycle = new FakeLifecycle();
            using var withLifecycle = new IapService(store, Catalog, entitlements, null, lifecycle, new MemoryTransactionLog());
            withLifecycle.InitializeAsync().GetAwaiter().GetResult();

            Task<IapPurchaseResult> purchase = withLifecycle.PurchaseAsync("gems_100");
            Assert.That(lifecycle.Open, Is.EqualTo(1));
            store.Fail("gems_100", IapFailure.UserCancelled);
            purchase.GetAwaiter().GetResult();
            Assert.That(lifecycle.Open, Is.EqualTo(0));
        }

        [Test]
        public async Task OwnedChanged_FiresWhenOwnershipChanges_NotForAnUnchangedFetch()
        {
            await service.InitializeAsync();
            int changes = 0;
            service.OwnedChanged += () => changes++;

            store.Owned(new IapStoreOrder { ProductId = "no_ads", TransactionId = "tx-1" });
            Assert.That(changes, Is.EqualTo(1));
            store.Owned(new IapStoreOrder { ProductId = "no_ads", TransactionId = "tx-1" });
            Assert.That(changes, Is.EqualTo(1), "same purchases again");

            store.Owned(new IapStoreOrder { ProductId = "no_ads", TransactionId = "tx-1" },
                        new IapStoreOrder { ProductId = "vip", TransactionId = "tx-2", SubscriptionActive = true });
            Assert.That(changes, Is.EqualTo(2));
            store.Owned(new IapStoreOrder { ProductId = "no_ads", TransactionId = "tx-1" },
                        new IapStoreOrder { ProductId = "vip", TransactionId = "tx-2", SubscriptionActive = false });
            Assert.That(changes, Is.EqualTo(3), "subscription expired");
        }

        [Test]
        public async Task OwnedChanged_FiresForANonConsumableGrant_NotForAConsumable()
        {
            await service.InitializeAsync();
            service.PurchaseGranted += _ => { };
            int changes = 0;
            service.OwnedChanged += () => changes++;

            store.Pending("gems_100", "tx-gems");
            Assert.That(changes, Is.EqualTo(0));
            store.Pending("no_ads", "tx-noads");
            Assert.That(changes, Is.EqualTo(1));
        }

        [Test]
        public void Products_IsBuiltOnce()
        {
            Assert.That(service.Products, Is.SameAs(service.Products));
            Assert.That(service.Products.Select(p => p.Id), Is.EqualTo(new[] { "gems_100", "no_ads", "vip" }));
        }

        [Test]
        public async Task Receipt_IsOnlyBuiltForANewGrant()
        {
            await service.InitializeAsync();
            service.PurchaseGranted += _ => { };
            int built = 0;
            IapStoreOrder Order(string tx) => new() { ProductId = "gems_100", TransactionId = tx, ReceiptSource = () => { built++; return "receipt-" + tx; } };

            store.Owned(Order("tx-owned"));
            Assert.That(built, Is.EqualTo(0), "fetching purchases does not build receipts");

            store.PendingOrder(Order("tx-new"));
            Assert.That(built, Is.EqualTo(1));
            Assert.That(sink.Purchases.Single().Receipt, Is.EqualTo("receipt-tx-new"));

            store.PendingOrder(Order("tx-new"));
            Assert.That(built, Is.EqualTo(1), "a redelivered, already granted transaction is only confirmed");
        }

        private sealed class FakeLifecycle : IAppLifecycle
        {
            public int Open;
            public bool IsPaused => false;
            public bool HasFocus => true;
            public event Action<bool> PauseChanged { add { } remove { } }
            public event Action<bool> FocusChanged { add { } remove { } }
            public event Action<AppResumeInfo> Resumed { add { } remove { } }
            public event Action Quitting { add { } remove { } }
            public IDisposable BeginExternalActivity(string reason)
            {
                Open++;
                return Disposable.Create(() => Open--);
            }
            public void MarkExternalActivity(string reason) { }
        }
    }
}
