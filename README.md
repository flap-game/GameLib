# GameLib

Modular Unity library for mobile games: ads, in-app purchases, tracking, haptics (UI later).
Each module is its own UPM package, so a game installs only what it uses, when it needs it.
GameLib is the successor of CoreGame.

- Unity **6000.4+**
- All packages share one version (lockstep). Install any combination from the same tag.

## Modules

| Package | Purpose | Status |
|---|---|---|
| `com.flapgame.gamelib.core` | Bootstrap, lifecycle, consent, entitlements, analytics contracts, **GameLib Setup** window | 0.1.0 |
| `com.flapgame.gamelib.haptics` | Vibration / haptic feedback | 0.1.0 |
| `com.flapgame.gamelib.ads` | Placement-based ads (interstitial, rewarded, banner, app open), ad policy | 0.2.0 |
| `com.flapgame.gamelib.ads.max` | AppLovin MAX adapter | 0.2.0 |
| `com.flapgame.gamelib.ads.admob` | AdMob adapter | later |
| `com.flapgame.gamelib.iap` | Unity IAP 5: consumable / non-consumable / subscription, localized price, restore | planned |
| `com.flapgame.gamelib.firebase` | Shared Firebase initialization | planned |
| `com.flapgame.gamelib.analytics.firebase` | Firebase Analytics (`ad_impression`, IAP events) | planned |
| `com.flapgame.gamelib.crashlytics` | Firebase Crashlytics | planned |
| `com.flapgame.gamelib.appsflyer` | AppsFlyer: ad revenue, purchases (manual or Purchase Connector), ATT, consent | planned |
| `com.flapgame.gamelib.ui` | UI framework | later |

Modules never reference each other directly; they talk through contracts in `core`
(analytics events, ad revenue, purchases, consent, entitlements such as `no_ads`).

## Install

Add `core` to `Packages/manifest.json`, then open **GameLib > Setup** to add further modules and their SDKs step by step:

```json
"com.flapgame.gamelib.core": "https://github.com/flap-game/GameLib.git?path=Packages/com.flapgame.gamelib.core#v0.2.0"
```

Unity does not resolve git dependencies between packages, so when installing by hand add every package
a module depends on (listed in its `package.json`) with the same tag. An OpenUPM registry is planned from 1.0.

## Developing

This repository is itself a Unity 6000.4 project; packages live embedded in `Packages/`.

- `Tools/new-module.ps1 -Module <name> -DisplayName <text> [-DependsOn <module>...]` scaffolds a package.
- `Tools/release.ps1 -Version X.Y.Z` bumps every package; commit, then `-Tag` creates `vX.Y.Z` plus one
  `<package>/X.Y.Z` tag per package.
