# Changelog

## [0.2.0] - 2026-10-26
- Ported from CoreGame 0.6.6: placement-based ads with provider plugins, waterfall routes, Primary/Parallel loading, policy, remote plan hook, banner metrics, click events.
- AdsModule asset replaces AdsSettings + AdsInstaller; no-ads follows the no_ads entitlement; revenue goes to IAnalytics.TrackAdRevenue; consent is published to IConsentState.
- App open rule built in: set AppOpenPlacementId. Returns from ads, ad clicks, purchases and links are ignored through IAppLifecycle.
