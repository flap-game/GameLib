# Changelog

## [Unreleased] - Unreleased
- iOS: AppsFlyerLib.framework / PurchaseConnector.framework are taken back out of Embed Frameworks when CocoaPods provides
  them. Plugin 7.0.3 always embeds them for the SPM path (EDM4U 1.2.187+); on the Podfile path the archive failed with
  "Release-iphoneos/PurchaseConnector.framework: No such file or directory".

## [0.8.6] - 2026-10-27
- Ad type `rewarded_interstitial`.

## [0.8.2] - 2026-10-27
- The AppsFlyer id goes to `ITrackerIds` (`TrackerIds.AppsFlyer`) once the SDK started (`getAppsFlyerId` on 6.17,
  `getAppsFlyerUID` on 7.x). `IAppsFlyerBackend.GetAppsFlyerIdAsync`.

## [0.5.0] - 2026-10-26
- README: a consent change mid-session applies from the next launch (AppsFlyer applies DMA consent at start).
- The startup timeout counts the bootstrap wait. Past it the buffer only warns: a later start still sends everything buffered (it used to start with revenue events dropped).
- Only forwarded events (all with Forward All Events) are buffered before the start, so game events no longer push ad revenue and purchases out of the buffer.
- af_purchase without a 3-letter currency or with a price of 0 is logged without af_revenue/af_currency, with a warning (it used to send "" / 0).

## [0.4.0] - 2026-10-26
- AppsFlyer Unity plugin 7.0.3 by default (Setup). Its Android build has no Purchase Connector: with Purchase Mode = PurchaseConnector, Android purchases are logged as af_purchase instead (iOS keeps the connector).
- The ATT prompt moved to the new ATT module (com.flapgame.gamelib.att). AppsFlyer only waits for the answer: `Att` (AttMode) and `AttUsageDescription` are replaced by `WaitForAtt`; set the prompt text on the ATT module.
- The customer user id set before the start is sent before `start`, so the install carries it.

## [0.3.0] - 2026-10-26
- First release: AppsFlyer sink (ad revenue via logAdRevenue, af_purchase or Purchase Connector, selected game events, customer user id), DMA consent from IConsentState or TCF, start after the CMP answer and the iOS App Tracking Transparency prompt. Supports AppsFlyer Unity plugin 6.17.x and 7.x.
