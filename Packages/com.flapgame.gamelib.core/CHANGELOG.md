# Changelog

## [0.9.2] - Unreleased
- `ILocalTracking` and `LocalTracking.SuppressesOnline` / `SuppressesAttribution`: the contract the new local tracking
  module (`analytics.local`) registers when a session runs on a test device, so the online sinks can stay off. Nothing
  registers it in a normal build and every sink installs as before.
- Setup lists **Analytics - Local** (`com.flapgame.gamelib.analytics.local`).
- Setup lists **Dev Tracking** (`com.flapgame.gamelib.devtrack`), the Editor-only module that reports a dev's
  Play sessions to the internal tool.

## [0.9.1] - 2026-11-06
- Agent guide: read the ids from the game's GameHub release config (what each section maps to); MAX adapters come from
  Install (`max-` keys), not the Integration Manager; native ad formats; register the device as a test device on both
  SDKs before testing real ad units (how to get each id); symptoms seen in device tests (Ad Inspector "No adapters
  found", adapter key errors, DT Exchange iOS crash in MAX test mode, pod version conflicts, MAX removing obsolete
  networks). Docs only, no code change.

## [0.9.0] - 2026-11-06
- Setup: Ads - AppLovin MAX offers MAX's mediation adapters (22 networks) as optional SDKs, each network's `.android`
  and `.ios` package installed / removed together (`SdkRequirement.Companions`), pinned with the AdMob + MAX version
  set; CLI keys `max-<network>` (`SdkRequirement.Key`). MAX core is pinned to 8.6.6 (`GameLibModuleCatalog.MaxVersion`)
  instead of the registry's newest.
- Setup: AdMob mediation adapter pins follow the AdMob + MAX version set (`com.flapgame.gamelib.ads/Documentation~/mediation-versions.json`).
  Verve 3.38.0 -> 1.1.3 (3.38.0 is a broken publish: myTarget's adapter inside, an iOS pod that does not exist, so
  `pod install` failed); BidMachine 1.5.3, DT Exchange 3.7.0, InMobi 5.4.1, ironSource 4.9.0, Mintegral 2.2.7, Moloco
  3.11.0, myTarget 3.43.0.

## [0.8.11] - 2026-10-27
- Agent guide: "Cloud Build only" setup (core + `build`, no modules, no Setup; GameHub presets for the post-build script and
  the Pre-Export Method).

## [0.8.9] - 2026-10-27
- Setup: optional SDKs per module (`GameLibModuleInfo.OptionalSdks`), installed / removed one by one; CLI
  `-gamelibOptional key,key` (`InstallModules(ids, optional)`), and the Doctor report lists the installed ones. Ads -
  AdMob offers Google's 19 mediation adapters (`com.google.ads.mobile.mediation.*`, OpenUPM).

## [0.8.6] - 2026-10-27
- `AdFormatKind.RewardedInterstitial`.

## [0.8.2] - 2026-10-27
- `ITrackerIds` (always registered): the Firebase app instance id and the AppsFlyer id of this install, for joining
  rows across tools (`ids.FirebaseAppInstanceId()`, `ids.AppsFlyerId()`, `Changed`). Kept on the device, so the last
  known ids are there from the first frame of later launches.
- Setup installs Firebase **13.16.0** from the GameWorkstore git mirrors (`https://github.com/GameWorkstore/
  com.google.firebase.<x>.git#13.16.0`, as CoreGame games do) instead of Google's tarballs: nothing to commit. 13.x and
  14.x are supported; a project on tarballs keeps them. `GameLibModuleCatalog.FirebaseTarballSdk` for the old source.
- Doctor / Setup: an error when the `com.google.firebase.*` packages are on different versions, a warning on a Firebase
  major version GameLib is not checked against (`GameLibProject.CheckFirebaseVersions`).

## [0.8.1] - 2026-10-27
- Setup lists the new Extensions package.

## [0.8.0] - 2026-10-27
- Setup lists the new Rate, App Icon and Save modules.
- `GameLibModuleOrder.Save` (-50): after analytics and crash reporting, before the game's own modules.

## [0.7.1] - 2026-10-26
- IAnalytics.Track no longer allocates a closure per call, and a BufferedAnalyticsSink that is ready forwards Track / TrackAdRevenue / TrackPurchase directly (no closure, no lock).

## [0.7.0] - 2026-10-26
- GameLibCli: GameLib Setup from batch mode for scripts and agents. `Install -gamelibModules id,...` adds the modules, their GameLib dependencies and SDKs (scoped registries, the newest registry version when none is pinned, Firebase tarballs), then creates and lists the module assets; run it until it exits 0 (2 = run again after Unity resolves). `Doctor` reports every module's state, its asset settings and the setup issues. JSON report in Logs/gamelib-<command>.json, exit code 1 on errors. Same API in the Editor: `InstallModules`, `BuildDoctorReport`.
- Documentation~/agent-guide.md: integration order, rules and checks for AI agents.
- ManifestManager.HasScopedRegistry; TarballInstaller.DownloadAsync (download without touching the manifest).

## [0.5.0] - 2026-10-26
- iOS: Control Center, Notification Center, an incoming-call banner or a system alert no longer raise IAppLifecycle.Resumed (Unity pauses for them on iOS, not on Android); only a real trip to the background does - so no app open ad after pulling Control Center down. Native observer in Plugins/iOS/GameLibLifecycle.mm.
- BufferedAnalyticsSink.RunLatest(key, call): last call per key, never dropped, flushed before the queued calls - consent for Firebase and AppsFlyer uses it, so events never reach Firebase without consent mode. A full buffer of ad revenue / purchases drops a new plain event instead of revenue.
- BufferedAnalyticsSink: a timeout no longer disables the sink. It logs one warning and keeps buffering; a start that lands later flushes everything ("late ready"), a later failure disables it then.
- BufferedAnalyticsSink: a full buffer drops the oldest event first; ad revenue and purchases are dropped only when nothing else is left. New optional `bufferEvent` filter: events the SDK would not send are not buffered.

## [0.4.0] - 2026-10-26
- IAttService contract (App Tracking Transparency status and prompt), implemented by the new ATT module.
- IConsentState.WaitUntilResolvedAsync: wait for the CMP answer with a timeout.
- BufferedAnalyticsSink.TryGetPendingUserId: the user id set before the SDK started.
- GameLib > Setup: ATT module; AppsFlyer installs plugin 7.0.3 and the ATT module with it.

## [0.3.0] - 2026-10-26
- IRemoteConfig contract and RemoteConfigJson (Newtonsoft parsing shared by every remote config provider).
- BufferedAnalyticsSink<T>: buffers calls for an SDK that starts in the background, flushes in order, turns itself off on failure or timeout.
- Disposable.Create, GameLibModuleOrder.RemoteConfig.
- GameLib > Setup: Firebase modules available; installs Firebase tarballs into Packages/GoogleTgz; a module's SDK step covers the SDKs of the modules it depends on.

## [0.2.0] - 2026-10-26
- IConsentService gains Status, CanRequestAds and RequestAsync (the CMP contract the Ads module drives); ConsentStatus moved to core.
- GameLib > Setup installs SDKs from scoped registries (AppLovin, OpenUPM) and handles modules without an asset of their own.
- Depends on com.unity.nuget.newtonsoft-json (manifest editing).

## [0.1.0] - 2026-10-26
- Bootstrap that starts itself from Resources/GameLibSettings; modules are ScriptableObjects (settings + installer) run by Order.
- Core services: IAppLifecycle (with external-activity aware Resumed), IConsentState, IEntitlements, IAnalytics with ad revenue and purchase events.
- GameLib > Setup window: step-by-step module install, module assets, project check; build validator.
