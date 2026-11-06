# GameLib — integration guide for agents

This guide is for an AI agent that adds GameLib to a Unity game, or adds a module to a game that already uses it. It
gives the order of work and the rules. The module READMEs are the reference for each API. This guide links to them and
does not repeat them. In a game, the README of module `<id>` is at
`Library/PackageCache/com.flapgame.gamelib.<id>@*/README.md`.

Read the copy that belongs to the installed version. In a game it is at
`Library/PackageCache/com.flapgame.gamelib.core@*/Documentation~/agent-guide.md`. Before core is installed, read it from
the tag: `https://github.com/flap-game/GameLib/blob/v<version>/Packages/com.flapgame.gamelib.core/Documentation~/agent-guide.md`.

## Rules

- **Never invent a value the store, an ad network or a console owns.** This covers SDK keys, ad unit ids, app ids,
  product ids, the AppsFlyer dev key, the iOS app id and the Firebase config files. Ask the human for all of them in one
  message before you configure. A placeholder is only allowed when the human asks for one, and then Doctor must still
  show it as missing.
- **Ask for the game's interstitial rule.** GameLib only ships the time-based part (`AdsModule.Policy`). Conditions such
  as "from level 2" or "every win and every loss" are per game, live in game code, and must come from the human.
- **Every game gets the `build` module and the App Store upload script.** All of the human's games build on Unity
  Cloud Build: every target calls the Pre-Export method `GameLibCloudBuild.PreExport` (in the `build` module), and the
  iOS targets run `BuildScripts/post-build-upload-appstore.bash` after the build. Add `build` to
  the module list even when the human does not name it. Then run **GameLib > Cloud Build > Install App Store Upload
  Script** (or `FlapGame.GameLib.Build.CloudBuildScripts.InstallAppStoreUpload(<project root>)` through `execute_code`), and commit
  `BuildScripts/` and `.gitattributes` to **every branch Cloud Build builds**. When the file is missing, Cloud Build
  stops at its config check with "Post-build script: File ... không có trên branch ...".
- Install through `GameLibCli` or GameLib Setup only, never by hand:
  - no SDK installed as a `.unitypackage`;
  - no scripting define symbols — GameLib turns its SDK code on with asmdef `versionDefines`;
  - no git URL for a GameLib package with a tag different from core's.
- Game code talks to GameLib services (`IAdsService`, `IIapService`, `IAnalytics`, ...), never to the MAX, AdMob,
  AppsFlyer or Firebase SDK directly. A direct call skips consent, no-ads, revenue tracking and the app open rule.
- **App open ads:** set `AdsModule.AppOpenPlacementId` and stop there. Do not write a trigger of your own. Before
  `Application.OpenURL`, a share sheet or a store page, call `IAppLifecycle.MarkExternalActivity("link")`.
- **Purchases:** grant consumables in `IIapService.PurchaseGranted`, the only place that is safe against a crash and a
  double grant. Do not track the purchase yourself; GameLib already sends it to every sink. Leave
  `LogStandardPurchaseEvent` off unless the human says automatic in-app purchase collection is off in Firebase.
- **When this game replaces CoreGame / aframework code**, remove the old pieces:
  - SDK init calls;
  - the hand-written app open trigger;
  - define symbols;
  - `AutoIncrementBuildVersion` (the PO sets the build numbers on GameHub).

  Two systems must not run side by side. When it is unclear what an old call maps to, ask; do not guess.

## 1. Before you start

Check these, then ask the human everything missing in **one** message:

| Need | For |
|---|---|
| Which modules | `haptics`, `save`, `ads` + `ads.max` / `ads.admob`, `att`, `iap`, `analytics.firebase`, `crashlytics`, `remoteconfig.firebase`, `appsflyer`, `rate`, `appicon`, `build`. The ids are in `GameLibModuleCatalog`. `build` is always in (see Rules); do not ask about it. |
| Placements | Id, format, and the Android and iOS ad unit id per network. Plus which placement is the app open one. |
| Interstitial rule | The game's own conditions (see Rules). |
| MAX SDK key / AdMob app ids | AppLovin Integration Manager / Google Mobile Ads settings. |
| Products | Store id, kind (Consumable / NonConsumable / Subscription), and what each one grants. |
| Firebase files | `google-services.json`, `GoogleService-Info.plist`. |
| AppsFlyer | Dev key and the numeric iOS app id. |
| ATT | The prompt sentence (`NSUserTrackingUsageDescription`). |

**Most of these values are on GameHub.** Each of the human's games has a release config per platform there. Ask the
human for the GameHub address, the API key (never write either into the project or a commit) and the bundle ids, then
read:

```sh
curl -H "X-Api-Key: <key>" "<GameHub address>/api/public/release-config?package=<bundle id>&platform=android"
```

(`platform=ios` with the iOS bundle id for iOS; the two configs differ.) What each section gives:

| GameHub section | Goes to |
|---|---|
| `player_setting.package_name` | The bundle id of that platform. |
| `max_id.sdk_key`, `max_id.<inter / reward / open / banner_top / banner_bottom / mrec>` | MAX SDK key; MAX ad unit ids of that platform. |
| `admob_id.app_id`, `admob_id.<open / inter / reward / banner_* / native_*>` | AdMob app id; AdMob ad unit ids (`native_*` = the native formats, AdMob only). |
| `max_mediation`, `admob_mediation` (`"true"` = on) | Which mediation adapters to install: the table in [mediation-versions.md](../../com.flapgame.gamelib.ads/Documentation~/mediation-versions.md) maps each key to its `-gamelibOptional` key. |
| `ads_setting.adUnits` + `ad_config.placements` | The game's ad plan: which unit each placement uses. Propose AdsModule placements from it and confirm with the human. |
| `appsflyer.dev_key`, `appsflyer.game_id` | AppsFlyer dev key; on iOS `game_id` is the numeric App Store id. |
| `firebase.data` | The content of `google-services.json` (Android) / `GoogleService-Info.plist` (iOS). `data_dev` is a second (dev) project: ask which one the build uses. |
| `bytebrew` | No GameLib module: ask the human. |

An empty value means "not set": ask, do not fill it in. Still ask the interstitial rule and the products.

**An iOS game with no ATT of its own:** a game restored from an Android build, or one whose ads / MMP SDKs were
removed, has no ATT call and no `NSUserTrackingUsageDescription`. On iOS no prompt shows and nothing reports an error.
Add the `att` module, make sure `AttModule` is listed in GameLibSettings, and ask for the prompt sentence.

The Unity version must be 6000.4 or newer (`ProjectSettings/ProjectVersion.txt`).

**Cloud Build only** (the human wants no ads, no tracking, only Unity Cloud Build): ask for nothing in the table above.
Install core and the `build` module (`-gamelibModules build`, step 2), and skip steps 3 and 4: no GameLib Setup, no
modules, nothing in a scene. Doctor's warning about the missing GameLib settings asset is expected. Then follow the
[Build README](../../com.flapgame.gamelib.build/README.md): for iOS, run **GameLib > Cloud Build > Install App Store
Upload Script** and commit `BuildScripts/` and `.gitattributes`. The Cloud Build dashboard is the human's part: give them the
README's dashboard table, or, on GameHub, tell them to pick **GameLib** for the Post-build Script and the Pre-Export
Method of the game.

## 2. Install

Add core to `Packages/manifest.json`. Use the tag the human names, else the newest `v*` tag on the repository
(`git ls-remote --tags https://github.com/flap-game/GameLib.git "v*"`):

```json
"com.flapgame.gamelib.core": "https://github.com/flap-game/GameLib.git?path=Packages/com.flapgame.gamelib.core#v<version>"
```

Then run Install with the modules. Dependencies are added on their own: `appsflyer` brings `att`, and the Firebase
modules bring `firebase`. AdMob mediation adapters are optional: add `-gamelibOptional pangle,unity,...` (the networks
of the AdMob mediation groups; an unknown name lists the valid ones). MAX adapters the same way with `max-` keys
(`max-unityads,max-bytedance,...`; each installs the network's `.android` + `.ios` package). Both lists are pinned to the
AdMob + MAX version set (`com.flapgame.gamelib.ads/Documentation~/mediation-versions.json`), so a game with both stacks
builds: never install an adapter at another version, nor via MAX's Integration Manager. A game's GameHub release config
(`max_mediation` / `admob_mediation`) says which networks it enables.

```sh
Unity -batchmode -quit -projectPath <project> -buildTarget Android \
  -executeMethod FlapGame.GameLib.Editor.GameLibCli.Install -gamelibModules ads.max,iap,appsflyer \
  -logFile <log>
```

Read the result from `Logs/gamelib-install.json` (or from `-gamelibReport <path>`), or from the log line after
`[GameLib] report:`. Act on the exit code:

| Exit | Status | Do |
|---|---|---|
| 0 | `Done` | Go to step 3. `Issues` lists what to configure. |
| 2 | `RunAgain` | Run the same command again. Unity resolves and compiles the new packages on the next start. A project with nothing installed takes about 3 runs: packages, then SDK versions from new registries, then module assets. |
| 1 | `Error` | Read `Errors`. If the log has `error CS`, the game does not compile: fix that first. Install cannot run in a project that does not compile. |

Each run is safe to repeat; it only adds what is missing.

**When the Editor already has the project open,** batch mode refuses to start. Either ask the human to close it, or call
the same API inside the Editor, for example through Unity MCP `execute_code`:

```csharp
var report = FlapGame.GameLib.Editor.GameLibCli.InstallModules(new[] { "ads.max", "iap" });
```

Wait for Unity to resolve and compile, then call it again until `Status` is `Done`.

Firebase comes from the GameWorkstore git mirrors at 13.16.0 (nothing to commit). 13.x and 14.x both work; a project
already on Google's tarballs in `Packages/GoogleTgz/` (14.x) keeps them - Install never replaces an existing Firebase
entry. All `com.google.firebase.*` packages must be on one version: Doctor reports an error otherwise.
`com.unity.purchasing`, the MAX package and EDM come from registries.

## 3. Configure

Each value goes to one place:

| Value | Where |
|---|---|
| Placements, providers, app open placement, ad policy | `Assets/GameLib/Modules/AdsModule.asset` ([Ads README](../../com.flapgame.gamelib.ads/README.md)) |
| MAX SDK key, AdMob app id for MAX's Google adapter, MAX consent flow | AppLovin > Integration Manager ([MAX README](../../com.flapgame.gamelib.ads.max/README.md)). Not its adapters: those come from Install (`max-` keys), at the version set's pins. |
| AdMob app ids | Assets > Google Mobile Ads > Settings ([AdMob README](../../com.flapgame.gamelib.ads.admob/README.md)) |
| Products | `IapModule.asset` ([IAP README](../../com.flapgame.gamelib.iap/README.md)) |
| Dev key, iOS app id, purchase mode | `AppsFlyerModule.asset` ([AppsFlyer README](../../com.flapgame.gamelib.appsflyer/README.md)) |
| ATT prompt text, timing | `AttModule.asset` ([ATT README](../../com.flapgame.gamelib.att/README.md)) |
| Firebase config files | Anywhere under `Assets/` ([Firebase README](../../com.flapgame.gamelib.firebase/README.md)) |
| Remote Config defaults | `RemoteConfigModule.asset` ([Remote Config README](../../com.flapgame.gamelib.remoteconfig.firebase/README.md)) |

To see the current values of every module asset, read `Settings` in the Doctor report (step 5). Enums appear there as
numbers.

Write the module assets with a temporary editor script, not by editing YAML. The compiler checks the field names, and
enums are written by name. Template:

```csharp
// Assets/Editor/GameLibConfigure.cs - run it once, then delete the file (and Assets/Editor if it was created for it).
using System.Collections.Generic;
using FlapGame.GameLib;
using FlapGame.GameLib.Ads;
using FlapGame.GameLib.Iap;
using UnityEditor;

public static class GameLibConfigure
{
    public static void Run()
    {
        var ads = AssetDatabase.LoadAssetAtPath<AdsModule>("Assets/GameLib/Modules/AdsModule.asset");
        ads.Providers = new List<AdsProviderConfig> { new AdsProviderConfig { ProviderId = "max" } };
        ads.Placements = new List<AdPlacement>
        {
            new AdPlacement { Id = "inter_win", Format = AdFormat.Interstitial,
                              Routes = { new AdRoute("max", "<android unit id>", "<ios unit id>") } },
            new AdPlacement { Id = "app_open", Format = AdFormat.AppOpen,
                              Routes = { new AdRoute("max", "<android unit id>", "<ios unit id>") } },
        };
        ads.AppOpenPlacementId = "app_open";
        EditorUtility.SetDirty(ads);

        var iap = AssetDatabase.LoadAssetAtPath<IapModule>("Assets/GameLib/Modules/IapModule.asset");
        iap.Products = new List<IapProductConfig>
        {
            new IapProductConfig { Id = "no_ads", Kind = PurchaseProductKind.NonConsumable, Entitlement = Entitlements.NoAds },
        };
        EditorUtility.SetDirty(iap);
        AssetDatabase.SaveAssets();
    }
}
```

```sh
Unity -batchmode -quit -projectPath <project> -executeMethod GameLibConfigure.Run -logFile <log>
```

Asset paths are the ones Install reports in `Changes`. Only assign the fields you were given values for; leave the rest
at the defaults.

## 4. Game code

GameLib starts by itself before the first scene (`GameLibSettings` > Auto Initialize). There is no bootstrap object to
add. Get the services with `await GameLibBootstrap.WhenReadyAsync()`. When a module is optional, use `TryResolve`.

| Task | API | Reference |
|---|---|---|
| Show interstitial / rewarded / banner | `IAdsService.ShowInterstitialAsync(placement, onCompleted)`, `ShowRewardedAsync`, `ShowBannerAsync` / `HideBanner`. Continue the game in `onCompleted`, which always fires. | [Ads](../../com.flapgame.gamelib.ads/README.md) |
| Game's interstitial rule | Check the condition in game code, then call `ShowInterstitialAsync` | Ads |
| Native ads (AdMob only) | Placement format `NativeBanner` / `NativeCollapsible` (`ShowBannerAsync`) or `NativeInterstitial` (`ShowInterstitialAsync`), AdMob route; never a MAX route | [Ads](../../com.flapgame.gamelib.ads/README.md) |
| Extra app open condition | `services.Resolve<AppOpenLauncher>().Gate = () => !inTutorial;` | Ads |
| Remove ads | IAP product with `Entitlement = Entitlements.NoAds`. Nothing else to do. | IAP |
| Buy / grant / restore | `PurchaseAsync(id, placement)`, grant in `PurchaseGranted`, `RestoreAsync` (show the button on iOS only), `GetPriceString` after `Ready` | [IAP](../../com.flapgame.gamelib.iap/README.md) |
| Game events | `IAnalytics.Track("level_complete", ("level", n))`. Names in `snake_case`, ≤ 40 characters. | [Analytics - Firebase](../../com.flapgame.gamelib.analytics.firebase/README.md) |
| User id | `IAnalytics.SetUserId` early at launch, so the AppsFlyer install carries it | [AppsFlyer](../../com.flapgame.gamelib.appsflyer/README.md) |
| Privacy settings button | `IConsentService.IsPrivacyOptionsRequired` | Ads |
| Opening a link / store page | `IAppLifecycle.MarkExternalActivity("link")` first | [Core](../README.md) |
| Remote values | `IRemoteConfig.GetLong / GetBool / GetObject<T>` | [Remote Config](../../com.flapgame.gamelib.remoteconfig.firebase/README.md) |
| Non-fatal errors, custom keys | `ICrashReporter` | [Crashlytics](../../com.flapgame.gamelib.crashlytics/README.md) |
| Save data | `save.Section<T>("player")`, change `Data`, `MarkDirty()`; small values `ForScope("x").Save/Load`; `Flush()` after a purchase or a win. Never PlayerPrefs for progress. | [Save](../../com.flapgame.gamelib.save/README.md) |
| Helpers (random pick, shuffle, FindDeep, DestroyChildren, `1.5K`, `05:30`, Delay) | `using FlapGame.GameLib.Extensions;` before writing your own. No AFramework / CoreGame `Common` helpers. | [Extensions](../../com.flapgame.gamelib.extensions/README.md) |
| Vibration | `IHapticsService.Play(HapticType...)` | [Haptics](../../com.flapgame.gamelib.haptics/README.md) |
| Rate us | `if (rate.NotifyTrigger()) popup.Show();` then `RequestReviewAsync()` / `StopPrompting()` | [Rate](../../com.flapgame.gamelib.rate/README.md) |
| Alternate app icon (iOS) | `IAppIconService.SetIconAsync(name)`, icons listed on the AppIconModule asset | [App Icon](../../com.flapgame.gamelib.appicon/README.md) |
| Ad events for the game's sheet (request, loaded, load fail) | `ads.ShowRequested`, `ads.PlacementLoaded`, `ads.PlacementLoadFailed`. Revenue and show results are already tracked: never log them by hand. | [Ads](../../com.flapgame.gamelib.ads/README.md) |
| Pause during fullscreen ads | AdsModule `Pause Game During Fullscreen Ads` / `Mute Audio During Fullscreen Ads`, not a hand-written handler as well | [Ads](../../com.flapgame.gamelib.ads/README.md) |
| Kids / family game: no mature ads | AdsModule `Max Ad Content Rating` = G or PG (AdMob only; MAX in its dashboard) | [Ads](../../com.flapgame.gamelib.ads/README.md) |
| Server grants rewards (SSV) | Callback URL in the AdMob / MAX console; `ads.SetRewardUserId(id)` + `ads.ShowRewardedAsync(id, customData, onRewarded)` | [Ads](../../com.flapgame.gamelib.ads/README.md) |
| Bonus reward offered at level end (rewarded interstitial) | AdMob placement format `RewardedInterstitial` + `ads.ShowRewardedInterstitialAsync(id, askUser: intro popup, onRewarded)`; follows the interstitial rules; never plain `ShowAsync` | [Ads](../../com.flapgame.gamelib.ads/README.md) |
| Ads follow the game's sound toggle | `ads.SetAdAudio(volume, muted)` at start and on every change (only when the game has a sound setting) | [Ads](../../com.flapgame.gamelib.ads/README.md) |
| QA: which network / creative a MAX ad came from, report a bad ad | `ads.OpenCreativeDebugger()` from a QA button (also: flip the device face down twice on a fullscreen MAX ad) | [MAX](../../com.flapgame.gamelib.ads.max/README.md) |
| MAX waterfall / floors per player group (payers, level band) | `ads.SetAdSegment(key, values)` with the numbers used in the MAX dashboard | [MAX](../../com.flapgame.gamelib.ads.max/README.md) |
| QA: which mediated networks fill | `await ads.OpenDebuggerAsync()` (AdMob Ad Inspector on test devices, MAX Mediation Debugger) from a QA button | [Ads](../../com.flapgame.gamelib.ads/README.md) |
| Every-Nth across sessions, QA reset | placement `Persist Request Count`; `ads.ResetPacing()` | [Ads](../../com.flapgame.gamelib.ads/README.md) |
| Firebase / AppsFlyer id in game events (`af_firebase_id`) | `services.Resolve<ITrackerIds>().FirebaseAppInstanceId()` / `.AppsFlyerId()` (null until known) | [Core](../README.md) |
| Game's own interstitial rule | `ads.AddRule(AdShowRule.From(p => ...))`, level gate `ads.SetPlayerLevel(n)` | [Ads](../../com.flapgame.gamelib.ads/README.md) |

## 5. Verify

```sh
Unity -batchmode -quit -projectPath <project> -buildTarget Android \
  -executeMethod FlapGame.GameLib.Editor.GameLibCli.Doctor -logFile <log>
```

The report is written to `Logs/gamelib-doctor.json`. Exit 0 means no errors; warnings can remain. Errors also stop the
player build. Run it again with `-buildTarget iOS`, which runs the iOS checks and compiles with the iOS defines. In
both logs, check that there is no `error CS`.

Doctor does **not** check:
- the MAX SDK key;
- the AdMob app ids;
- that the AdMob app is linked to Firebase (GameLib leaves AdMob's `ad_impression` to Firebase; when it is not linked,
  turn on `Log AdMob Impressions` on the Firebase Analytics module, or AdMob revenue never reaches GA4);
- that ad unit ids and product ids exist in the consoles;
- anything that only shows on a device: ads filling, the consent form, the ATT prompt, purchases, revenue in the
  dashboards.

Say so in your report instead of calling the work verified. The human tests on a device.

**Testing ads on a device with the game's real ad units:** register the device as a test device on both SDKs first,
or the test requests count as live traffic on the game's accounts. AdsModule `Test Mode` on, plus:

| SDK | Android id | iOS id |
|---|---|---|
| AdMob (`AdMob Test Device Ids`) | MD5 of `Settings.Secure.ANDROID_ID`, uppercase hex (also printed in logcat on the first request) | MD5 of the IDFA string, lowercase hex. The IDFA needs the ATT prompt answered "Allow". |
| MAX (`Max Test Device Ids`) | The GAID | The IDFA |

Then `ads.OpenDebuggerAsync("max")` (Mediation Debugger: every network the game enables must be under *Completed SDK
Integrations*) and `ads.OpenDebuggerAsync("admob")` (Ad Inspector, Adapters tab). Ads served show a "Test Ad" label.
Turn Test Mode off and clear the ids before a store build. Do not tap ads.

## Symptoms

| Symptom | Cause |
|---|---|
| No app open ad on the first launch | Intended: never on the first launch after install. |
| No app open ad when coming back from an ad, a purchase or a link | Intended: those are not returns. |
| Banner / interstitial never shows, rewarded works | The `no_ads` entitlement is owned (a test purchase). |
| Only mock ads in the Editor | Intended: the networks only run on a device. |
| Ad Inspector > Adapters: "No adapters found", although the adapters are installed | The AdMob app's mediation groups have no third-party source for these units. Console work for the human, not code. |
| AdMob adapter `NotReady`: "No SDK keys are found. Please add valid SDK keys in the AdMob UI" / "Invalid account id" | That network's keys are missing or wrong in the AdMob mediation setup. Console work for the human. |
| iOS crash a few seconds after start, `EXC_BAD_ACCESS` in `DTXRemoteConfigDTO.Spot.init(from:)` (sometimes a SIGKILL in `MTGCrashManager`) | DT Exchange with MAX test mode on (seen on DT 8.5.0 and 8.5.1). On a test device run without DT Exchange; keep it in the game. |
| `pod install`: "could not find compatible versions" for a network pod | An adapter outside the AdMob + MAX version set (e.g. MAX Integration Manager's newest). Reinstall it through Install. |
| A MAX adapter (HyprMX, maio, myTarget, CSJ, Tencent) disappears from the manifest | MAX 8.6.6 removes networks it treats as obsolete. Use AdMob's adapter for maio / myTarget. |
| A module does nothing, Doctor warns "never starts" | Its asset is not in `GameLibSettings`. Run Install for that module again. |
| Firebase modules silent, one warning in the Editor | The config file for the build target is missing. |
| `executeMethod` class not found | The project does not compile, or core is not installed. |
| Cloud Build fails in seconds: "Post-build script: File `BuildScripts/post-build-upload-appstore.bash` không có trên branch ..." | The upload script was never committed on that branch. Install it (see Rules), commit, push, build again. |
| Cloud Build: `export method 'GameLibCloudBuild.PreExport' not found, aborting` | The `build` module is not installed on that branch. Install it with `-gamelibModules build`, commit `Packages/manifest.json` and `packages-lock.json`, push. |

## For the game's CLAUDE.md

So that later sessions find this guide, add these lines to the game's `CLAUDE.md`:

```md
## GameLib
Ads / IAP / tracking come from GameLib (com.flapgame.gamelib.*). Before changing them, read
Library/PackageCache/com.flapgame.gamelib.core@*/Documentation~/agent-guide.md and the module README it links.
Check with: Unity -batchmode -quit -projectPath . -executeMethod FlapGame.GameLib.Editor.GameLibCli.Doctor
```
