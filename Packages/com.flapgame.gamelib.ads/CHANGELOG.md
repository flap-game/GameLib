# Changelog

## [0.5.0] - 2026-10-26
- A shown app open ad no longer restarts the interstitial interval (AdsPolicy.AppOpenFollowsInterstitialRules = true still does both).
- Banners requested before the ad SDKs initialized (e.g. in a menu's Start) are no longer reported Failed: the composite remembers the wanted banner per placement and shows it once a route is active. HideBanner / DestroyBanner cancel it.
- Banner fallback routes now display: when a load failure opens the next route, the wanted banner is re-issued to it (and hidden on the route that stopped serving it); the primary takes over again once it loads.
- Consent is published again when a provider that timed out at init joins late (new CompositeAdsService.ProviderJoinedLate), so a CMP inside MAX's init no longer leaves IConsentState with the pre-init answer.
- App open: the cold-start wait (AppOpenColdStartWaitSeconds) now starts when the ad SDKs are up (new AppOpenLauncher.AdsStarted), not at module install, so slow consent / SDK init no longer skips the launch ad; the launch ad shows at most 30 s after launch (ColdStartMaxSeconds), starting and loading included, so it never pops up mid-play.
- A consent flow that ends without permission to request ads (e.g. Google UMP offline on the first launch) is retried on the next returns to the app (up to 3); ads start as soon as a retry allows them.

## [0.3.0] - 2026-10-26
- The ads plan comes from remote config again: with a remote config module installed and no IAdsPlanSource of the game's own, AdsModule reads the JSON plan under PlanRemoteConfigKey (default `ads_plan`) at install and follows every activation (RemoteConfigAdsPlanSource).

## [0.2.0] - 2026-10-26
- Ported from CoreGame 0.6.6: placement-based ads with provider plugins, waterfall routes, Primary/Parallel loading, policy, remote plan hook, banner metrics, click events.
- AdsModule asset replaces AdsSettings + AdsInstaller; no-ads follows the no_ads entitlement; revenue goes to IAnalytics.TrackAdRevenue; consent is published to IConsentState.
- App open rule built in: set AppOpenPlacementId. Returns from ads, ad clicks, purchases and links are ignored through IAppLifecycle.
