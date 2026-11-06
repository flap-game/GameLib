# AdMob + MAX mediation: one version set for both stacks

A game that ships both Google Mobile Ads (with AdMob mediation adapters) and AppLovin MAX (with MAX adapters) puts
each network's SDK into the app once, but asks for it twice. This set is the newest one where both asks agree. The
machine-readable copy, which tools and agents should read, is [`mediation-versions.json`](mediation-versions.json).

Checked 2026-11-06 with `python Tools/mediation_versions.py check` (registries + Maven POMs + CocoaPods podspecs), and by
building a test project with **every** adapter below on Android (Gradle) and iOS (pod install + xcodebuild).

Test build result (Unity 6000.4.12f1, every adapter of both stacks, GameLib core/ads/ads.admob/ads.max):

- **Android** (PC): APK built; Gradle's dependency tree holds one version per network SDK, the ones in the tables.
- **iOS** (Mac, Xcode 27.2): `pod install` resolved with no conflict - one pod per network SDK serves both adapters
  (e.g. `Ads-Global` 8.3.0.8 for MAX ByteDance and AdMob Pangle, `FiveAd` 3.0.1 for both LINE adapters),
  Google-Mobile-Ads-SDK 13.11.0, AppLovinSDK 13.6.4. `xcodebuild` **succeeded** with no duplicate or undefined symbol
  once the Pods targets were at iOS 15.0.
- Xcode 27 refuses pods whose own deployment target is below 15.0 (Mintegral 10.0, Chartboost 11.0, most AppLovin
  adapters 12.0): *"The iOS deployment target ... is set to 12.0, but the range of supported deployment target
  versions is 15.0 to 27.2"*. `com.flapgame.gamelib.build` (`IosPodBuildSettingsFix`) raises them after pod install;
  without that package, add the same Podfile `post_install` yourself.
- Expected noise: `[AppLovin MAX] Failed to install AppLovin Quality Service plugin. SDK Key is empty` (no SDK key in
  the test project).

## Why versions must match

- **iOS fails the build.** Google's adapter pods pin the network SDK exactly (`'BidMachine', '3.8.2'`), AppLovin's do
  too (`'= 3.8.2'`). Two different pins for one pod: `pod install` stops with *could not find compatible versions*.
- **Android builds, then may crash.** Gradle silently takes the highest version, so one adapter runs against an SDK it
  was not compiled with (`NoSuchMethodError` at ad load, not at build).
- So every network on both stacks is held at the SDK version **both** adapters were built for. Where one side is
  newer, the newer one waits (noted below). The only accepted difference is LINE Android, patch level (3.1.0 vs 3.1.1).

## Core

| Package | Version | Native |
|---|---|---|
| `com.google.ads.mobile` (OpenUPM) | 11.6.0 | GMA Android 25.5.0, iOS `~> 13.11` (resolves 13.11.0) |
| `com.applovin.mediation.ads` (AppLovin registry) | 8.6.6 | AppLovin SDK 13.6.4 (Android + iOS) |

Build settings for both stacks together: Android minSdk 25 (Unity 6000.4's floor; MAX needs 24), Custom Main Gradle Template + Gradle Properties Template
(Jetifier on); iOS deployment target **15.0** (DT Exchange, Meta, LINE, maio, MobileFuse, Ogury, Yandex need it),
Xcode 16+ (Xcode 27: also `com.flapgame.gamelib.build`, see above).

## Networks on both stacks

MAX columns are `<package suffix>.android` / `.ios` versions, e.g. `com.applovin.mediation.adapters.bidmachine.android`.

| Network | AdMob adapter | MAX adapter (Android / iOS) | SDK Android / iOS | Held back |
|---|---|---|---|---|
| AppLovin | `applovin` 8.7.7 | (MAX core 8.6.6) | 13.6.4 / 13.6.4 | Upgrade MAX core only together with this adapter |
| BidMachine | `bidmachine` 1.5.3 | `bidmachine` 3080001.0.0 / 308020000.0.0 | 3.8.0 / 3.8.2 | - |
| BIGO Ads | `bigo` 3.1.0 | `bigoads` 6010000.0.0 / 6010001.0.0 | 6.1.0 / 6.1.0 | MAX Android 6.2.0 |
| Chartboost | `chartboost` 4.15.2 | `chartboost` 9140101.0.0 / 9140200.0.0 | 9.14.1 / 9.14.2 | - |
| DT Exchange | `dtexchange` 3.7.0 | `fyber` 8050000.0.0 / 8050100.0.0 | 8.5.0 / 8.5.1 | MAX Android 8.5.1 |
| Google (as a MAX network) | - | `google` and `googleadmanager` 25050000.0.0 / 13110000.0.0 | 25.5.0 / 13.11.0 | Must equal what GMA 11.6.0 resolves |
| InMobi | `inmobi` 5.4.1 | `inmobi` 11050001.0.0 / 11050000.0.0 | 11.5.0 / 11.5.0 | MAX Android 11.5.1 |
| ironSource | `ironsource` 4.9.0 | `ironsource` 907000000.0.0 / 907000000.0.0 | 9.7.0 / 9.7.0.0 | - |
| Liftoff Monetize | `liftoffmonetize` 5.7.10 | `vungle` 7070400.0.0 / 7070300.0.0 | 7.7.4 / 7.7.3 | MAX 7.7.9 / 7.7.8 (Google's adapter lags) |
| LINE | `line` 2.2.1 | `line` 300001010.1.0 / 3000100.0.0 | 3.1.1 (AdMob built on 3.1.0) / 3.0.1 | MAX 3.2.0 / 3.1.1 |
| Meta Audience Network | `metaaudiencenetwork` 3.20.1 | `facebook` 6220001.0.0 / 6220004.0.0 | 6.22.0 / 6.22.0 | MAX iOS 6.22.1 |
| Mintegral | `mintegral` 2.2.7 | `mintegral` 17019100.0.0 / 801080000.0.0 | 17.1.91 / 8.1.8 | MAX Android 17.2.21 |
| Moloco | `moloco` 3.11.0 | `moloco` 4130001.0.0 / 4110101.0.0 | 4.13.0 / 4.11.1 | MAX 4.14.0 / 4.12.0 |
| Pangle | `pangle` **7.2.2** | `bytedance` 803000401.0.0 / 803000800.0.0 | 8.3.0.4 / 8.3.0.8 | AdMob 7.3.0 (8.4.0.5): MAX iOS has no 8.4 yet |
| PubMatic | `pubmatic` 2.4.1 | `pubmatic` 5040100.0.0 / 5040100.0.0 | 5.4.1 / 5.4.1 | MAX iOS 5.5.0 |
| Unity Ads | `unity` 3.22.0 | `unityads` 4210001.0.0 / 4210001.0.0 | 4.21.0 / 4.21.0 | - |
| Verve | `verve` **1.1.3** | `verve` 3090300.0.0 / 3090300.0.0 | 3.9.3 / 3.9.3 | - |

## One stack only

| Network | Install | Never |
|---|---|---|
| i-mobile | AdMob `imobile` 1.3.16 | (no MAX adapter) |
| maio | AdMob `maio` 3.1.11 | MAX `maio.*`: stuck at SDK 2.0.4 / 2.1.6 since 2025, and the AdMob adapter on that SDK needs GMA iOS 12 |
| myTarget | AdMob `mytarget` 3.43.0 | MAX `mytarget.*`: AppLovin dropped myTarget from MAX |
| MobileFuse | MAX `mobilefuse` 1130100.0.0 / 1130000.0.0 | - |
| Ogury | MAX `ogurypresage` 6030101.0.0 / 5030100.0.0 | - |
| Smaato | MAX `smaato` 23020300.0.0 / 23020300.0.0 | - |
| Yandex | MAX `yandex` 8060000.0.0 / 8060000.0.0 | - |
| YSO Network | MAX `ysonetwork` 1030900.0.0 / 1020100.0.0 | - |

Left out: `hyprmx.*`, `csj.ios`, `tencentgdt.ios`, `maio.*`, `mytarget.*` - MAX 8.6.6 lists them as obsolete
(`AppLovinInitialize.ObsoleteNetworks`) and uninstalls them from the manifest on Editor load; `pangle.ios` (old Pangle 4.7 package; MAX Pangle is
`bytedance.*`).

## Traps found

- **AdMob Verve 3.38.0 is broken**, and OpenUPM marks it *latest* because 3.38 > 1.1.x. It was a wrong publish
  (2026-08-08): its Android dependency is the **myTarget** adapter and its iOS pod `GoogleMobileAdsMediationVerve
  5.43.0.0` does not exist, so `pod install` fails. The real line is 1.1.x. GameLib Setup pinned 3.38.0 until this set (core CHANGELOG, Unreleased).
- **DT Exchange iOS crashes when MAX runs in test mode** (found 2026-11-06, iPhone 14 Pro Max, iOS 27.2, DT 8.5.1 and
  8.5.0 alike): a few seconds after init, `EXC_BAD_ACCESS` in DT's remote-config decoder
  (`DTXRemoteConfigDTO.Spot.init(from:)`), sometimes reported as a SIGKILL from Mintegral's crash reporter
  deadlocking on the same fault. MAX test mode on (a test device registered): 5 of 6 launches crashed; test mode off:
  0 of 6. So it hits QA devices, not (as far as seen) players. On a test device run the iOS ad test without DT
  Exchange (`fyber.ios` + AdMob `dtexchange`); keep it in the game. Re-check when DT ships a release after 8.5.1.
- **Do not pick by "latest" on either registry.** Sort by publish date and read the native pins; `check` does.
- **MAX Integration Manager installs the newest adapter**, which breaks the pairing above. Install MAX adapters from
  GameLib Setup (below) and do not press the Integration Manager's *Upgrade All*.

## Installing

- AdMob adapters: GameLib > Setup > Ads - AdMob > Optional (the catalog carries these pins), or
  `-gamelibOptional bidmachine,unity,...`.
- MAX adapters: GameLib > Setup > Ads - AppLovin MAX > Optional - one row per network, Install / Remove adds or
  removes its `.android` and `.ios` packages together at these pins - or `-gamelibOptional max-unityads,max-bytedance,...`
  (MAX keys are `max-` + the package's network name). Setup also pins MAX core to 8.6.6.
- Install only networks that are live in the AdMob mediation groups / MAX ad units; the set guarantees they agree, not
  that each is needed.

## A game's own subset

`python Tools/mediation_versions.py manifest <project> --gamehub <release-config.json> --platform android|ios` writes
the manifest with only the networks a game enables on GameHub (`max_mediation` / `admob_mediation` of
`GET /api/public/release-config?package=<bundle id>&platform=<platform>`, header `X-Api-Key`), at this set's versions.
Networks MAX treats as obsolete are skipped with a warning.

## Updating the set

1. `python Tools/mediation_versions.py check` lists newer versions published since the pins.
2. Edit `mediation-versions.json` (both sides of a network together), re-run `check` until no `FAIL`.
3. Copy the pins into `GameLibModuleCatalog` - `AdMobMediation(...)`, `MaxMediation(...)`, `MaxVersion` (`check` fails
   while they differ) - and update this page.
4. `python Tools/mediation_versions.py manifest <test project>` writes a manifest with every adapter; build it for
   Android and iOS (`MediationCheckBuild.Android` / `.IOS` in `D:\Funnii\UnityProjects\mediation-check`).
