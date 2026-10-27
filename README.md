# GameLib

Modular Unity library for mobile games: ads, in-app purchases, tracking, save data, haptics, rate-us, app icons (UI later).
Each module is its own UPM package, so a game installs only what it uses, when it needs it.
GameLib is the successor of CoreGame.

- Unity **6000.4+**
- All packages share one version (lockstep). Install any combination from the same tag.

## Modules

| Package | Purpose | Status |
|---|---|---|
| `com.flapgame.gamelib.core` | Bootstrap, lifecycle, consent, entitlements, analytics contracts, **GameLib Setup** window | 0.1.0 |
| `com.flapgame.gamelib.haptics` | Vibration / haptic feedback | 0.1.0 |
| `com.flapgame.gamelib.save` | Save data: crash-safe file, backup, checksum, versioned sections, autosave, optional encryption | 0.8.0 |
| `com.flapgame.gamelib.extensions` | Everyday helpers: collections and random, Transform / GameObject / vector / color / UI, number and time formatting, Delay | 0.8.1 |
| `com.flapgame.gamelib.ads` | Placement-based ads (interstitial, rewarded, banner, app open), ad policy | 0.2.0 |
| `com.flapgame.gamelib.ads.max` | AppLovin MAX adapter | 0.2.0 |
| `com.flapgame.gamelib.ads.admob` | AdMob adapter and Google UMP consent | 0.3.0 |
| `com.flapgame.gamelib.iap` | Unity IAP 5: consumable / non-consumable / subscription, localized price, restore | 0.3.0 |
| `com.flapgame.gamelib.firebase` | Shared Firebase start and config checks (comes with the Firebase modules) | 0.3.0 |
| `com.flapgame.gamelib.analytics.firebase` | Firebase Analytics (`ad_impression`, consent mode, optional `purchase`) | 0.3.0 |
| `com.flapgame.gamelib.crashlytics` | Firebase Crashlytics with analytics breadcrumbs | 0.3.0 |
| `com.flapgame.gamelib.remoteconfig.firebase` | `IRemoteConfig` on Firebase Remote Config (Ads reads its plan from it) | 0.3.0 |
| `com.flapgame.gamelib.att` | iOS App Tracking Transparency prompt: after the consent form (optional delay) or on demand | 0.4.0 |
| `com.flapgame.gamelib.appsflyer` | AppsFlyer: ad revenue, purchases (manual or Purchase Connector), consent; starts after ATT | 0.3.0 |
| `com.flapgame.gamelib.build` | Unity Cloud Build: splash off, App Store upload of iOS builds | 0.6.0 |
| `com.flapgame.gamelib.rate` | Rate-us prompts and the store review sheet (iOS, Google Play) | 0.8.0 |
| `com.flapgame.gamelib.appicon` | Alternate app icons (iOS) | 0.8.0 |
| `com.flapgame.gamelib.ui` | UI framework | later |

Modules never reference each other directly; they talk through contracts in `core`
(analytics events, ad revenue, purchases, consent, entitlements such as `no_ads`, remote config).

Third-party SDK calls live in separate `*.Sdk` assemblies that compile only when the SDK package is present
(`versionDefines`). A module's asset therefore never turns into a missing script, and its logic is unit tested without
the SDK. Compile-checked against: AppLovin MAX 8.6.6, Google Mobile Ads 11.6.0, Firebase 13.16.0 and 14.1.0, AppsFlyer 6.17.900 and 7.0.3, Unity IAP 5.4.4.

## How the pieces connect

| Flow | Path |
|---|---|
| Ad revenue | Ads (MAX / AdMob) → `IAnalytics.TrackAdRevenue` → Firebase `ad_impression`, AppsFlyer `logAdRevenue`, Crashlytics breadcrumb |
| Purchase | IAP → `IAnalytics.TrackPurchase` → AppsFlyer `af_purchase` (or Purchase Connector), Firebase `purchase` (opt-in) |
| Remove ads | IAP grants `no_ads` in `IEntitlements` → Ads suppresses interstitial, banner, app open |
| Consent | Ads runs the CMP → `IConsentState` → ad SDKs, Firebase consent mode, AppsFlyer DMA consent |
| ATT (iOS) | ATT module asks after the CMP answer (+ delay) → `IConsentState.Att` → AppsFlyer starts with the answer |
| Remote values | Remote Config → `IRemoteConfig` → Ads plan (`ads_plan`), game values |
| App open rule | Ads `AppOpenLauncher` on `IAppLifecycle.Resumed`; ads, ad clicks and purchases are not returns |

## Install

Add `core` to `Packages/manifest.json`, then open **GameLib > Setup** to add further modules and their SDKs step by step:

```json
"com.flapgame.gamelib.core": "https://github.com/flap-game/GameLib.git?path=Packages/com.flapgame.gamelib.core#v0.8.10"
```

Unity does not resolve git dependencies between packages, so when installing by hand add every package
a module depends on (listed in its `package.json`) with the same tag. An OpenUPM registry is planned from 1.0.

Without the window, `GameLibCli.Install` / `GameLibCli.Doctor` run the same steps in batch mode. An AI agent that integrates
GameLib follows the [agent guide](Packages/com.flapgame.gamelib.core/Documentation~/agent-guide.md).

## External Dependency Manager

MAX, Firebase and AppsFlyer use Google's `com.google.external-dependency-manager` (OpenUPM); Google Mobile Ads 11.x
pulls in Unity's `com.unity.external-dependency-manager`. With both installed the project still compiles: Unity's EDM
turns itself off (`UNITY_EDM_DISABLED` on Android, `ProjectSettings/ExternalDependencyManagerSettings.json`) and the
console logs a *Duplicate assembly 'Google.VersionHandler.dll'* warning. Check **Assets > External Dependency Manager**
resolves Android and iOS once after adding AdMob; if it does not, keep a single resolver.

## Developing

This repository is itself a Unity 6000.4 project; packages live embedded in `Packages/`.

- `Tools/new-module.ps1 -Module <name> -DisplayName <text> [-DependsOn <module>...]` scaffolds a package.
- `Tools/release.ps1 -Version X.Y.Z` bumps every package; commit, then `-Tag` creates `vX.Y.Z` plus one
  `<package>/X.Y.Z` tag per package.
