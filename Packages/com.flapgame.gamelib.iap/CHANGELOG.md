# Changelog

## [0.8.0] - 2026-10-27
- `IIapService.TryGetSubscription(id, out IapSubscriptionInfo)`: free trial, auto-renewing and end of period of an active subscription, as the store reports them (null when it does not). Breaking for custom `IIapService` implementations.
- Several products may grant one entitlement (weekly / monthly / yearly VIP): an expired subscription no longer revokes it while another product that grants it is owned.

## [0.7.1] - 2026-10-26
- New IIapService.OwnedChanged: raised when IsOwned changes (a grant, the owned purchases fetched at launch or after a restore, an expired subscription). Breaking for custom IIapService implementations.
- The store receipt is built only when a new grant sends it (IapStoreOrder.Receipt is read through ReceiptSource on first use). On iOS Unity IAP rebuilt the whole app receipt for every order on every purchases fetch.
- One PlayerPrefs.Save per grant instead of two: the transaction log and a new entitlement are written together, still before the PurchaseGranted handlers and the store confirmation.
- Products is built once instead of a new list per read.
- The restore timeout timers stop when the restore finishes.
- UnityIapStore unsubscribes from the StoreController when the IAP service is disposed (bootstrap reset).

## [0.5.0] - 2026-10-26
- Restored purchases and subscription renewals (App Store: OriginalTransactionId differs) are granted but not sent as revenue (IAnalytics.TrackPurchase); iap_purchase_success carries restore=true. IapPurchase.IsRestore, IapStoreOrder.OriginalTransactionId.
- A transaction is logged before the PurchaseGranted handlers run: a crash in between can lose that grant but never grants it twice.
- RestoreAsync also handles a purchase fetch that lands before the restore answer, a store that throws, and a restore answer that never comes (false after 120 s); a stuck restore no longer blocks later ones or the app open ad.
- A paid subscription or non-consumable whose confirmation had not reached the store yet (app killed between grant and confirm, or confirmed offline) is no longer revoked at the next launch: Unity IAP lists it as a pending order, which now counts as owned.
- RestoreAsync completes after the restored purchases are applied (IsOwned and entitlements up to date), fetches once instead of twice, returns false when that fetch fails, and returns after 15 s at most. Concurrent calls share one restore.
- IIapStore: PurchasesFetchFailed event; PurchasesFetched lists every paid order (confirmed and pending).

## [0.3.0] - 2026-10-26
- First release: IIapService on Unity IAP 5 (StoreController) - products by id with localized prices, consumables granted by the game before the store confirmation (each transaction once, held until a handler subscribes), non-consumables and subscriptions granted as entitlements and restored from the store, restore, deferred purchases, purchase tracking, purchase UI as an external activity.
