using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

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
            public void Fail(string productId, IapFailure failure) => PurchaseFailed?.Invoke(new IapStoreOrder { ProductId = productId }, failure, "details");
            public void Defer(string productId) => PurchaseDeferred?.Invoke(new IapStoreOrder { ProductId = productId });
            public void Owned(params IapStoreOrder[] orders) => PurchasesFetched?.Invoke(orders);
            public void FailFetch() => ProductsFetchFailed?.Invoke("x");
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
        public async Task Restore_RefreshesOwnedPurchases_AndIsTracked()
        {
            await service.InitializeAsync();
            int before = store.FetchPurchasesCalls;

            Task<bool> restore = service.RestoreAsync();
            store.RestoreCallback(true, null);

            Assert.That(await restore, Is.True);
            Assert.That(store.FetchPurchasesCalls, Is.EqualTo(before + 1));
            Assert.That(sink.Events.Last().Name, Is.EqualTo(IapService.RestoreEvent));
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
