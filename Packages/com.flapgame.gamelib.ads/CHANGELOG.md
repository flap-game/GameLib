# Changelog

## [0.9.0] - 2026-11-06
- Fix: an app open ad could show on the way back from an ad click. The SDK's click callback reaches the main thread
  only after the resume when the click leaves the app at once (a store page, a system "open another app" prompt), so the
  return looked like a real one. `AppOpenLauncher` now waits `LateClickGraceSeconds` (0.5 s) on a return and shows
  nothing when an ad click arrives in that time.
- Fix: a banner / MREC whose tap-opened page never got its close from the SDK (seen on Android when an "open another
  app" system prompt was dismissed) kept counting as a fullscreen ad for the rest of the session - no app open ad on any
  later return, interstitials refused with `AnotherAdShowing`. Back in the app, such a banner is back in its slot
  (`CompositeAdsService.ReleaseExpandedBannersOnReturn`, called by `AdsModule` on every return, and once more 1 s later
  unless the app paused again: the SDK's "opened" for the click that left the app can land just after the resume).
- `AdPlacement.NativeBannerHeight` for a `NativeBanner`: `Adaptive` (default) is as tall as an anchored adaptive banner
  on the device (`BannerLayout.AdaptiveBannerHeightDp`, Google's rule, shared with the AdMob banner height estimate);
  `Smart` is AdMob's former smart banner height (`BannerLayout.SmartBannerHeightDp`: 32 dp on a phone in landscape,
  50 dp in portrait, 90 dp on a tablet), compact for landscape games; `Fixed` is `NativeHeightDp`.
- AdMob native ads: placement formats `NativeBanner`, `NativeCollapsible` and `NativeInterstitial` (`AdPlacement.IsNative`,
  `Native*` fields: height, close / collapse-button delays, start expanded, hide on collapse, colors). Native bars go
  through the banner API (`IsBannerLike`); `NativeInterstitial` follows the interstitial pacing rules and shares their timer.
- `INativeCollapseSource`, implemented by `CompositeAdsService`: `NativeExpandedChanged(id, expanded)` from every adapter
  that reports it. A player's collapse of a placement with `NativeHideOnCollapse` hides it as `HideBanner` does.
- `AdPlacementOverride` and `AdsPlanPlacement`: nullable `NativeCloseDelaySeconds` / `NativeCollapseButtonDelaySeconds`.
- Doctor: a native placement routed to MAX is an error; a `NativeCollapsible` with `Position = Top` warns (it shows at the
  bottom). Ad revenue of native bars reports as banner, of native interstitials as interstitial.
- Inspector: a placement shows only the fields its format reads (Native fields on native formats; Size, Custom Center,
  Preload Banner, MAX fields hidden for natives). The Null adapter shows native bars like banners.

## [0.8.10] - 2026-10-27
- `ads.OpenCreativeDebugger()` (`IAdsCreativeDebugger`) and `ads.SetAdSegment(key, values)` (`IAdSegments`, keys and values
  0..32000; passed before init and on every change).
- A banner / MREC showing fullscreen content (`IBannerExpansion`, from MAX and AdMob) counts as a fullscreen ad until it
  closes or is hidden: `IsShowingFullscreen` / `FullscreenShowingChanged` include it, so the pause applies and no
  fullscreen ad shows over it (`AnotherAdShowing`).
- `AdsModule.MaxExtraParameters` / `MaxCreativeDebuggerOnFlip`; placement `MaxExtraParameters` and `BannerBackgroundColor`
  (`AdKeyValue`). Doctor warns on an extra parameter without a key.

## [0.8.8] - 2026-10-27
- `AdsModule.MaxAdContentRating` (`AdContentRating` G / PG / T / MA): the most mature ad content AdMob may serve. Doctor
  warns that it does not apply to MAX.

## [0.8.7] - 2026-10-27
- Server-side reward verification: `ads.SetRewardUserId(id)` and `ads.ShowRewardedAsync(id, customData, onRewarded)`
  (`ShowRewardedInterstitialAsync` takes `customData` too) reach the showing provider before each rewarded show
  (`IRewardVerification`, implemented by the AdMob and MAX adapters).

## [0.8.6] - 2026-10-27
- Format `AdFormat.RewardedInterstitial` (AdMob only): follows the interstitial pacing rules and restarts their timer.
  `ads.ShowRewardedInterstitialAsync(placement, askUser, onRewarded)` runs the game's intro screen Google requires - only
  when the ad can show - and opens the ad only on yes. Doctor reports a MAX route on it as an error.

## [0.8.5] - 2026-10-27
- `ads.SetAdAudio(volume, muted)`: video ads start at the game's volume or muted (`IAdsAudio`, implemented by the AdMob
  and MAX adapters); applied once each SDK initialized and on every change.

## [0.8.4] - 2026-10-27
- `ads.OpenDebuggerAsync(providerId = null)`: opens the SDK's debug screen (`IAdsDebugger`, implemented by the AdMob and
  MAX adapters). The trip to it is marked as game-initiated, so the return shows no app open ad
  (`CompositeAdsService.DebuggerOpening`).

## [0.8.3] - 2026-10-27
- `AdImpression.CountryCode` and `AdRevenue.CountryCode` filled from the ad SDK (MAX), for AppsFlyer's `country`.

## [0.8.2] - 2026-10-27
- Events for the game's own ad analytics on `IAdsService`: `ShowRequested (placement, reason)` once per `ShowAsync`
  (`AdBlockReason.None` = sent to the SDK), `PlacementLoaded (placement, provider)`, `PlacementLoadFailed (placement,
  provider, error)`.
- `ads.ResetPacing()`: forgets the interstitial / app open intervals (also the saved one), cooldowns, session counts and
  every-Nth counts, for a QA button or a remote config switch.
- `AdPlacement.PersistRequestCount` (also in the remote plan): `ShowEveryNthRequest` keeps counting across sessions
  (PlayerPrefs `gamelib.ads.requests.<placement>`).
- `AdsModule.PauseGameDuringFullscreenAds` / `MuteAudioDuringFullscreenAds` (off by default): `Time.timeScale = 0` /
  `AudioListener.pause` while a fullscreen ad is up, put back when it closes unless the game changed them meanwhile.
  `ads.IsShowingFullscreen()`.
- `BannerSize.SmallFullWidth`: full width, 32 dp high (AdMob; a Standard banner on MAX - Validate warns, as for Collapsible).
- Depends on `com.unity.modules.audio`.

## [0.8.0] - 2026-10-27
- Interstitial rules shared by every game, all off by default (set per game on the AdsModule asset or from the remote plan): `Policy.InterstitialFromLevel` (no interstitial below that level; the game reports it with `ads.SetPlayerLevel(n)`, new `AdBlockReason.BelowLevel`), `Policy.InterstitialIntervalAcrossSessions` (the interval also counts the last interstitial of the previous session), `AdPlacement.ShowEveryNthRequest` (shows on every Nth request; a blocked request still counts; `AdBlockReason.RequestCount`).
- A game's own rule on top: `ads.AddRule(AdShowRule)` / `AdShowRule.From(placement => ...)`, asked by `CanShow` after the shared rules (`AdBlockReason.GameRule`), told about every request (`OnRequested`) and show (`OnShown`).
- App open: `AppOpenShowAtLaunchFrom` (first launch that shows the launch ad: 1 every launch, 2 the shared rule, 5 from the 5th), `AppOpenAutoShowAtLaunch` (off = the loading screen shows it with `AppOpenLauncher.ShowLaunchAdAsync()` and continues when it returns), `AppOpenMinBackgroundSeconds`, `AppOpenLaunchCountKey` (keep the launch count of a game moving to GameLib). `AppOpenLauncher.SkipFirstLaunch` is obsolete (`ShowAtLaunchFrom`).
- `CompositeAdsService.BannerHeightChanged` (placement, on-screen height in pixels; 0 = nothing on screen) and `GetVisibleBannerHeightPixels` / `ads.GetVisibleBannerHeightPixels(id)`, to lay the UI out above the banner.
- `BannerPosition.Custom`: banner / MREC centred at `AdPlacement.CustomCenter` (fraction of the safe area). `BannerLayout.CustomTopLeftDp`.
- `AdPlacement.WaitForFillSeconds` (also in the remote plan) and `ads.ShowAsync(id, waitForFillSeconds)`: a fullscreen show waits for the ad to load when loading is the only thing missing, instead of failing at once. 0 (default) = as before.
- `AdPlacement.PreloadBanner`: the banner is created hidden when its SDK is ready, so the first show displays a loaded ad.

## [0.7.1] - 2026-10-26
- A fullscreen placement that reached MaxShowsPerSession is no longer kept loaded (no reload request, no ad held in memory for a placement that cannot show again this session).
- CanShow / IsPlacementReady / banner heights no longer allocate: the effective routes of each placement are cached and rebuilt only when the plan, a provider or the placements change. Safe to call from UI every frame (PlacementReadyChanged is still the cheaper way).
- New AdsModule.VerboseLogs: the info logs of every load, show and retry are written in the Editor and development builds only, unless it is on (on a device each Debug.Log captures a stack trace). Warnings and errors are unchanged. Static switch: AdsLog.Verbose.
- Android: the IAB consent strings are read with one SharedPreferences lookup per read instead of one per key (about 24 JNI calls and 3 map copies down to a few).

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
