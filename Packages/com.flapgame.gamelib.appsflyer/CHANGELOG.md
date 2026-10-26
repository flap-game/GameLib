# Changelog

## [0.4.0] - 2026-10-26
- AppsFlyer Unity plugin 7.0.3 by default (Setup). Its Android build has no Purchase Connector: with Purchase Mode = PurchaseConnector, Android purchases are logged as af_purchase instead (iOS keeps the connector).
- The ATT prompt moved to the new ATT module (com.flapgame.gamelib.att). AppsFlyer only waits for the answer: `Att` (AttMode) and `AttUsageDescription` are replaced by `WaitForAtt`; set the prompt text on the ATT module.
- The customer user id set before the start is sent before `start`, so the install carries it.

## [0.3.0] - 2026-10-26
- First release: AppsFlyer sink (ad revenue via logAdRevenue, af_purchase or Purchase Connector, selected game events, customer user id), DMA consent from IConsentState or TCF, start after the CMP answer and the iOS App Tracking Transparency prompt. Supports AppsFlyer Unity plugin 6.17.x and 7.x.
