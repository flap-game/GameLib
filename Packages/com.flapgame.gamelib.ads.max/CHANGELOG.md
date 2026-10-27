# Changelog

## [0.8.5] - 2026-10-27
- `IAdsAudio`: `MaxSdk.SetMuted` (muted, or volume 0; MAX has no volume).

## [0.8.4] - 2026-10-27
- `IAdsDebugger`: AppLovin's Mediation Debugger (`MaxSdk.ShowMediationDebugger`).

## [0.8.3] - 2026-10-27
- Paid impressions carry the user's country code (`SdkConfiguration.CountryCode`), sent to AppsFlyer as `country`
  (`AdRevenueScheme.COUNTRY`) - it was always empty before.

## [0.8.0] - 2026-10-27
- Banner and MREC heights (`IBannerMetrics`): the layout above the banner and `BannerHeightChanged` now work with MAX (MREC 250 dp, adaptive from MAX, else 50 / 90 dp).
- `BannerPosition.Custom` (MREC or banner at `AdPlacement.CustomCenter`).
- An interstitial / rewarded / app open ad that MAX no longer has (expired since it loaded) is not shown: the show fails, the ad reloads. Before, a show of such an ad could stay open if MAX never called back, blocking every fullscreen ad for the session.
- A hidden banner / MREC stops auto-refreshing (no requests without impressions) and restarts when shown.
- `AdPlacement.PreloadBanner`.

## [0.7.1] - 2026-10-26
- Info logs follow AdsLog.Verbose (see Ads).

## [0.5.0] - 2026-10-26
- Impression revenue currency moved to a named constant (MAX reports revenue in USD; AdInfo has no currency). No behaviour change.

## [0.2.0] - 2026-10-26
- AppLovin MAX provider and MAX consent flow, ported from CoreGame 0.6.6. Compiles only when com.applovin.mediation.ads is installed.
