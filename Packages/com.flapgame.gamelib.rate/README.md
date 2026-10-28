# GameLib Rate

Rate-us prompts. The game draws its own popup; the module decides **when** to show it and opens the store's review flow.

Install from **GameLib > Setup > Rate**, or add to `Packages/manifest.json` (together with core, same tag):

```
https://github.com/flap-game/GameLib.git?path=Packages/com.flapgame.gamelib.rate#v0.8.13
```

## Use

```csharp
var rate = (await GameLibBootstrap.WhenReadyAsync()).Resolve<IRateService>();

// A moment where the game may ask (back on Home, after a win...):
if (rate.NotifyTrigger()) rateUsPopup.Show();

// In the popup:
await rate.RequestReviewAsync();   // "Rate": the store's review sheet, else the store page
rate.StopPrompting();              // "Never"
// "Later": just close the popup - the next prompt comes after the next N triggers.
```

`RequestReviewAsync` and `OpenStorePage` mark the trip to the store as the game's own, so coming back shows no app open ad.

## Settings (RateModule asset)

| Field | Meaning |
|---|---|
| Prompt Every Triggers | Ask on every Nth `NotifyTrigger` since the last prompt. A trigger blocked by another rule still counts. |
| Max Prompts | Most prompts ever (0 = no limit). |
| Min Hours Between Prompts | 0 = none. |
| Stop After Rated | No more prompts after the review sheet / store page or "never". |
| App Store Id | Digits of `apps.apple.com/app/id...`: the iOS store page when the review sheet is not available. |
| Android Review Library | Gradle coordinates of Play In-App Review (default `com.google.android.play:review:2.0.2`). |

Counters are in PlayerPrefs (`gamelib.rate.*`); `ResetState()` clears them (debug menu).

## Platform notes

- **iOS**: `Device.RequestStoreReview()` (StoreKit). Apple shows the sheet at most 3 times a year and never in TestFlight
  builds from the App Store review flow; nothing tells the game whether the player rated.
- **Android**: Play In-App Review through JNI. The editor package adds the library to the generated `unityLibrary`
  Gradle module and its keep rules to `proguard-unity.txt`. Play also has a quota and shows nothing in a build that was
  not installed from Play (use internal app sharing / internal testing to see it). Without Play the store page opens
  (`https://play.google.com/store/apps/details?id=...`: the Play app when installed, else the browser).
  Do not also install Google's `com.google.play.review` Unity package (duplicate classes).
