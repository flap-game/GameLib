# Changelog

## [0.8.2] - 2026-10-27
- `BannerSize.SmallFullWidth`: a custom `AdSize(AdSize.FullWidth, 32)` banner (full width of the safe area on iOS).

## [0.8.0] - 2026-10-27
- `BannerPosition.Custom` (MREC or banner at `AdPlacement.CustomCenter`; not for collapsible banners).
- `AdPlacement.PreloadBanner` (not for collapsible banners).

## [0.7.1] - 2026-10-26
- GetBannerHeightPixels returns the height measured when the banner loaded instead of a native call on every read.
- The remembered banner height no longer forces PlayerPrefs.Save (a blocking disk write) on the first banner load of each session.
- Info logs follow AdsLog.Verbose (see Ads).

## [0.5.0] - 2026-10-26
- A retried init no longer keeps the previous run's units in shared fullscreen slots (slots are rebuilt with the units).

## [0.3.0] - 2026-10-26
- First release (ported from CoreGame 0.6.6): AdMob adapter (interstitial, rewarded, banner, app open; one loaded ad per unit id; impression revenue) and Google UMP consent service.
