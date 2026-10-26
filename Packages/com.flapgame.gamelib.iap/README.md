# GameLib IAP

In-app purchases on Unity IAP 5 (`StoreController`). Install from **GameLib > Setup > In-App Purchases**: it adds this
package and `com.unity.purchasing` 5.4.4. In the Editor Unity IAP's fake store answers every purchase.

## Configure (IapModule)

List every product with its type. Ids as in Google Play Console / App Store Connect; set **Google Play Id** /
**App Store Id** when a store uses a different id. Give products that unlock something forever an **Entitlement**:

| Id | Kind | Entitlement |
|---|---|---|
| `no_ads` | NonConsumable | `no_ads` (the Ads module stops interstitials, banners and app open ads) |
| `gems_100` | Consumable | - |
| `vip_monthly` | Subscription | `vip` |

## Use

```csharp
var iap = services.Resolve<IIapService>();

// Grant consumables here - also called at startup for purchases finished while the game was closed.
iap.PurchaseGranted += purchase =>
{
    if (purchase.ProductId == "gems_100") { wallet.Add(100); save.Write(); }
};

priceLabel.text = iap.GetPriceString("gems_100", "...");   // localized: "$1.99", "49.000 ₫"
iap.Ready += RefreshPrices;                                // prices arrive after the store connects
iap.OwnedChanged += RefreshOwnedBadges;                    // owned products arrive after Ready, and change on restore / expiry

IapPurchaseResult result = await iap.PurchaseAsync("gems_100", placement: "shop");
// Success, Cancelled, Failed, Deferred (Ask to Buy / pending payment: granted later), AlreadyOwned, NotReady

restoreButton.gameObject.SetActive(Application.platform == RuntimePlatform.IPhonePlayer);
await iap.RestoreAsync();
```

- **Grant, then confirm.** The store purchase is confirmed after `PurchaseGranted` ran, so a crash in between delivers
  the purchase again on the next launch - and a transaction id already granted is only confirmed, never granted twice.
  Purchases that arrive before the game subscribes (at startup) wait for the first handler.
- **Entitlements** are granted by GameLib on purchase and on every launch from what the store says is owned. An expired
  subscription revokes its entitlement; a non-consumable is never taken back.
- **Tracking**: `IAnalytics.TrackPurchase` (Firebase `purchase` when enabled, AppsFlyer `af_purchase`) plus
  `iap_purchase_success` {product_id, product_type, price, currency, price_string, transaction_id, placement, recovered},
  `iap_purchase_failed` {product_id, reason, message, placement}, `iap_purchase_cancelled`, `iap_purchase_deferred`,
  `iap_restore` {success, error}.
- **App open ads**: the store UI runs inside `IAppLifecycle.BeginExternalActivity("purchase")`, so returning from it does
  not show an app open ad.

Receipt validation on a server is not part of GameLib; `PurchaseRecord.Receipt` carries the store receipt for sinks
that validate (AppsFlyer Purchase Connector).
