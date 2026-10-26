# GameLib AppsFlyer

AppsFlyer attribution as an `IAnalytics` sink. Install from **GameLib > Setup > AppsFlyer**: it adds this package,
External Dependency Manager (OpenUPM), the AppsFlyer Unity plugin **7.0.3** (git, `#v7.0.3`) and the
[ATT module](../com.flapgame.gamelib.att/README.md), which shows the iOS prompt. GameLib also works with plugin 6.17.x.

Plugin 7.0.3 leaves the Purchase Connector out of its Android build (AppsFlyer's note in `AppsFlyerDependencies.xml`).
GameLib detects that at launch: with *Purchase Mode* = `PurchaseConnector`, Android purchases are logged as `af_purchase`
instead (a warning says so), and iOS keeps the connector. Purchase revenue is never lost or counted twice.

## Configure (AppsFlyerModule)

| Field | |
|---|---|
| Dev Key | AppsFlyer dashboard > App settings. Required. |
| iOS App Id | Numeric Apple id, without `id`. Required for iOS. |
| Wait For Consent / Consent Wait Seconds | Start after the CMP answered (installed with the Ads module), at most 60 s. No CMP installed = no wait. |
| Consent Source | `ConsentState` (default): GameLib sends the CMP answer as DMA consent. `Tcf`: the SDK reads the TCF strings of a TCF v2.2 CMP. |
| Wait For Att / Att Wait Seconds | iOS: start after the ATT answer (the ATT module or MAX's consent flow asks), at most 60 s, so the install carries the IDFA. Without the ATT module installed there is no wait. |
| Purchase Mode | `Manual` (default): `af_purchase` per purchase. `PurchaseConnector`: AppsFlyer ROI360 validates and logs store purchases (paid plan). `None`. |
| Forward All Events / Forwarded Events | Which game events also go to AppsFlyer (default: none - only ad revenue and purchases). |

## Start order

SDK created → CMP answered → consent sent → ATT answered (iOS) → customer user id → Purchase Connector → `start`. The install is reported
with the user's choices. Everything tracked meanwhile is held and sent after the start (up to 200 calls). The ATT answer is
also written to `IConsentState.Att`, so other modules see it.

The user id (`IAnalytics.SetUserId`) becomes the AppsFlyer customer user id. Set it early (at launch, before the
consent form is answered) and it goes out before `start`, so the install itself carries it; set later, it is attached
to the events that follow.

## What is sent

| GameLib | AppsFlyer |
|---|---|
| `TrackAdRevenue` (every paid impression, from the Ads module) | `logAdRevenue(network, mediation, currency, revenue)` + `ad_unit`, `ad_type`, `placement`, `country` |
| `TrackPurchase` (Manual) | `af_purchase`: `af_revenue`, `af_currency`, `af_content_id`, `af_content_type`, `af_order_id`, `af_quantity` |
| `Track` of a forwarded event | the event, values as invariant strings |
| `SetUserId` | `setCustomerUserId` |

Test with the AppsFlyer SDK integration test (dashboard > SDK integration tests) on a registered test device.
