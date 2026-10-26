# Changelog

## [0.3.0] - Unreleased
- The ads plan comes from remote config again: with a remote config module installed and no IAdsPlanSource of the game's own, AdsModule reads the JSON plan under PlanRemoteConfigKey (default `ads_plan`) at install and follows every activation (RemoteConfigAdsPlanSource).

## [0.2.0] - 2026-10-26
- Ported from CoreGame 0.6.6: placement-based ads with provider plugins, waterfall routes, Primary/Parallel loading, policy, remote plan hook, banner metrics, click events.
- AdsModule asset replaces AdsSettings + AdsInstaller; no-ads follows the no_ads entitlement; revenue goes to IAnalytics.TrackAdRevenue; consent is published to IConsentState.
- App open rule built in: set AppOpenPlacementId. Returns from ads, ad clicks, purchases and links are ignored through IAppLifecycle.
