# Changelog

## [0.9.0] - 2026-11-06
- Native ads: `NativeBanner`, `NativeCollapsible` and `NativeInterstitial`, drawn by GameLib's own native views
  (Android Java / iOS Objective-C++, in `Plugins/`) through `AdMobNativeBridge` (README: Native ads).
- `INativeCollapseSource`: a `NativeCollapsible` opened expanded / collapsed by the player.
- Test mode uses Google's native demo units for native placements without a unit id.
- Fix: hiding a collapsible banner while it is expanded left the SDK's expanded overlay on screen. The view is now
  destroyed (the next show loads a new one) and the expansion is reported closed, also when the placement is turned off.
- A collapsible banner opening its expanded panel by itself is no longer reported as fullscreen content: interstitials
  (standard and native) now show over it instead of being refused with `AnotherAdShowing`. A tap that opens the ad's
  page is still reported.
- Landscape: an expanded `NativeCollapsible` shows its media in a left column and the texts on the right (the media
  stays at least 120 dp, AdMob's video minimum, which the stacked layout missed on a landscape screen). For games with
  a fixed orientation: a shown ad keeps the layout of the orientation it was shown in.
- Native bars under 40 dp (a `Smart` `NativeBanner` in landscape) are one line: icon, headline, "AD", body, rating /
  advertiser / store, button; what does not fit is dropped (price, store, rating, advertiser, then the body).
- iOS native: the "AD" badge hugs its text (it could stretch to ~140pt and push the icon and text right), and the native
  interstitial's close button has the accessibility label "Close ad".
- iOS native bars at the bottom (`NativeBanner`, `NativeCollapsible` collapsed and expanded) reach the physical bottom
  edge across the full screen width; their content stays inside the safe area (the ad grows by the bottom inset).
  While one is up the home indicator auto-hides (it comes back on touch) and the bottom edge gesture is deferred;
  the game's `Device.hideHomeButton` / `deferSystemGesturesMode` come back when the last bar hides. The one-line
  layout of bars under 40 pt is on iOS too (`NativeBanner`).

## [0.8.10] - 2026-10-27
- Banner fullscreen content (`OnAdFullScreenContentOpened` / `Closed`) is reported as an expanded banner.

## [0.8.9] - 2026-10-27
- Mediation adapters for 19 networks, from GameLib > Setup > Ads - AdMob > Optional (README: Mediation adapters).

## [0.8.8] - 2026-10-27
- `RequestConfiguration.MaxAdContentRating` from `AdsModule.MaxAdContentRating`.

## [0.8.7] - 2026-10-27
- `IRewardVerification`: `ServerSideVerificationOptions` (user id, custom data) on each rewarded / rewarded interstitial ad.

## [0.8.6] - 2026-10-27
- Rewarded interstitial (`RewardedInterstitialAd`), with Google's demo units in test mode.

## [0.8.5] - 2026-10-27
- `IAdsAudio`: `MobileAds.SetApplicationVolume` / `SetApplicationMuted`.

## [0.8.4] - 2026-10-27
- `IAdsDebugger`: Google's Ad Inspector (`MobileAds.OpenAdInspector`), on AdMob test devices.

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
