# Changelog

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
