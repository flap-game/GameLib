# GameLib AppsFlyer

AppsFlyer attribution as an `IAnalytics` sink. Install from **GameLib > Setup > AppsFlyer**: it adds this package,
External Dependency Manager (OpenUPM) and the AppsFlyer Unity plugin **6.17.900** (git, `#v6.17.900`).

Plugin 7.x works too (GameLib compiles against both): change the tag to `#v7.0.3` in `Packages/manifest.json`. Note
that 7.0.3 ships its **Android Purchase Connector disabled** (AppsFlyer's own note in `AppsFlyerPurchaseConnector.cs`),
so stay on 6.17.900 if you use the Purchase Connector.

## Configure (AppsFlyerModule)

| Field | |
|---|---|
| Dev Key | AppsFlyer dashboard > App settings. Required. |
| iOS App Id | Numeric Apple id, without `id`. Required for iOS. |
| Wait For Consent / Consent Wait Seconds | Start after the CMP answered (installed with the Ads module), at most 60 s. No CMP installed = no wait. |
| Consent Source | `ConsentState` (default): GameLib sends the CMP answer as DMA consent. `Tcf`: the SDK reads the TCF strings of a TCF v2.2 CMP. |
| Att / Att Wait Seconds | iOS App Tracking Transparency before the start: `Request` (GameLib asks), `WaitForOther` (MAX's consent flow asks), `DoNotWait`. |
| Att Usage Description | Info.plist `NSUserTrackingUsageDescription`, added at build time with `Request`. |
| Purchase Mode | `Manual` (default): `af_purchase` per purchase. `PurchaseConnector`: AppsFlyer ROI360 validates and logs store purchases (paid plan). `None`. |
| Forward All Events / Forwarded Events | Which game events also go to AppsFlyer (default: none - only ad revenue and purchases). |

## Start order

SDK created → CMP answered → consent sent → ATT answered (iOS) → Purchase Connector → `start`. The install is reported
with the user's choices. Everything tracked meanwhile is held and sent after the start (up to 200 calls). The ATT answer is
also written to `IConsentState.Att`, so other modules see it.

The user id (`IAnalytics.SetUserId`) becomes the AppsFlyer customer user id - after the start, so it is attached to later
events, not to the install itself.

## What is sent

| GameLib | AppsFlyer |
|---|---|
| `TrackAdRevenue` (every paid impression, from the Ads module) | `logAdRevenue(network, mediation, currency, revenue)` + `ad_unit`, `ad_type`, `placement`, `country` |
| `TrackPurchase` (Manual) | `af_purchase`: `af_revenue`, `af_currency`, `af_content_id`, `af_content_type`, `af_order_id`, `af_quantity` |
| `Track` of a forwarded event | the event, values as invariant strings |
| `SetUserId` | `setCustomerUserId` |

Test with the AppsFlyer SDK integration test (dashboard > SDK integration tests) on a registered test device.
