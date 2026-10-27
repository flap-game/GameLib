# GameLib Ads

Placement-based interstitial, rewarded, banner, MREC and app open ads. Gameplay only ever names a **placement**
(`"level_end"`, `"revive"`); which network serves it (MAX now, AdMob later), in which order, and how often is data
on the `AdsModule` asset - so switching networks never touches game code.

Install from **GameLib > Setup > Ads**, then add a provider (**Ads - AppLovin MAX**). Without a provider package the
mock adapter serves every placement in the Editor and development builds (release builds show nothing).

## Use

```csharp
IAdsService ads = (await GameLibBootstrap.WhenReadyAsync()).Resolve<IAdsService>();

// Interstitial: onCompleted always fires, shown or not - continue the game there.
await ads.ShowInterstitialAsync("level_end", onCompleted: LoadNextLevel);

// Rewarded: true only when the reward was earned.
bool earned = await ads.ShowRewardedAsync("revive");

// Banner
await ads.ShowBannerAsync("home_banner");
ads.HideBanner("home_banner");

// Ask before showing a button
if (ads.CanShow("revive", out AdBlockReason reason)) ShowReviveButton();
```

`AdPlacementRef` assets let designers pick placements in the Inspector instead of typing ids.

Nothing loaded yet: `ShowAsync` fails at once, unless the placement's `Wait For Fill Seconds` is set (also in the remote
plan) - then it waits up to that long for the ad to load, when loading is the only thing missing. Per call:
`ads.ShowAsync("revive", 3f)`. Show a loading indicator while it waits.

## AdsModule asset

| Section | What it holds |
|---|---|
| Providers | One row per network (`max`, `admob`): on/off switch, app keys where the SDK needs them. |
| Load Strategy | `Primary` keeps only the first route of each placement loaded (the next one loads after a failure); `Parallel` keeps every route loaded. |
| Policy | Rules over all fullscreen placements: global interstitial interval, first-interstitial delay, rewarded resets the interval, app open interval (90 s) and grace after another ad. |
| App Open | `AppOpenPlacementId`: set it and the shared app open rule runs by itself (see below). |
| Placements | Id, format, routes (provider + Android / iOS unit ids, in waterfall order), cooldown, session cap, hidden by no-ads, banner size / position. |
| Logs | `VerboseLogs`: info logs for every load / show / retry also in release builds (always in the Editor and development builds). |

## Interstitial rules

The shared rules are all off by default; each game sets its values on the AdsModule asset (or from the remote plan):

| Rule | Where | Blocked with |
|---|---|---|
| Seconds between two interstitials | `Policy.InterstitialMinIntervalSeconds` | `GlobalInterval` |
| ... also across app restarts | `Policy.InterstitialIntervalAcrossSessions` | `GlobalInterval` |
| None in the first seconds of a session | `Policy.FirstInterstitialDelaySeconds` | `SessionStartGrace` |
| None below a level | `Policy.InterstitialFromLevel` + `ads.SetPlayerLevel(level)` | `BelowLevel` |
| Every Nth request of a placement | placement `Show Every Nth Request` (a blocked request still counts) | `RequestCount` |
| Seconds between two shows / per session cap | placement `Cooldown Seconds` / `Max Shows Per Session` | `Cooldown` / `SessionCap` |

Anything else is the game's own rule, asked after the shared ones:

```csharp
ads.AddRule(AdShowRule.From(p => p.Format != AdFormat.Interstitial || !tutorial.IsActive));
```

Subclass `AdShowRule` to count requests (`OnRequested`) or shows (`OnShown`) yourself. Keep `Allows` cheap: `CanShow`
calls it, and UI may poll that every frame.

## App open rule

With `AppOpenPlacementId` set:

- no app open ad on the first launch after install (`AppOpenShowAtLaunchFrom`: 1 = every launch, 5 = from the 5th);
- one on every later launch (when it loads within `AppOpenColdStartWaitSeconds`) and on every return from the background
  (at least `AppOpenMinBackgroundSeconds` away, 0 = every return);
- never on the way back from an ad, an ad click, a purchase, or a link the game opened
  (call `IAppLifecycle.MarkExternalActivity("link")` before `Application.OpenURL`);
- at least `Policy.AppOpenMinIntervalSeconds` (90 s) between two app open ads; interstitial rules do not apply to it.

Add the game's own condition through the launcher: `services.Resolve<AppOpenLauncher>().Gate = () => !inTutorial;`

To show the launch ad from the loading screen instead (Home opens after it closes), turn `AppOpenAutoShowAtLaunch` off:

```csharp
await services.Resolve<AppOpenLauncher>().ShowLaunchAdAsync(); // returns at once when this launch shows none
LoadHome();
```

A game moving to GameLib keeps its players' launch count by setting `AppOpenLaunchCountKey` to its old PlayerPrefs key.

## Banner layout

`ads.GetVisibleBannerHeightPixels(id)` is the height a shown banner takes on screen (0 when hidden or not loaded);
`((CompositeAdsService)ads).BannerHeightChanged += (id, px) => ...` tells when it changes. `Position = Custom` centres a
banner / MREC at `Custom Center` (fraction of the safe area, (0, 0) = top-left).

`Preload Banner` creates the banner hidden as soon as its SDK is ready, so the first screen with a banner shows an ad
instead of an empty slot (not for collapsible banners). A hidden MAX banner stops refreshing until it is shown again.

## No-ads, consent, tracking

- **No-ads** follows the `no_ads` entitlement (`IEntitlements`): the IAP module grants it, or grant it yourself.
  Placements with *Suppressed By No Ads* stop loading, banners are destroyed, rewarded keeps working.
- **Consent**: the provider's CMP (MAX's consent flow) runs before any SDK loads. The result goes to every ad SDK and to
  the shared `IConsentState`, so tracking modules follow it. For a "Privacy settings" button resolve `IConsentService`
  and show the button when `IsPrivacyOptionsRequired`.
- **Tracking**: every paid impression goes to `IAnalytics.TrackAdRevenue` (Firebase `ad_impression`, AppsFlyer ad
  revenue); every show logs `ads_show_result {placement_id, provider, result}`.

## Remote plan

An `AdsPlan` drives providers, route order, rules and policy remotely. With **Remote Config - Firebase** installed,
AdsModule reads it as JSON from the key in **Plan Remote Config Key** (default `ads_plan`): at startup from the last
session's cache (or the default on the Remote Config asset), then again after every fetch. Example value:

```json
{
  "Name": "ab_max_only",
  "Providers": ["max"],
  "Placements": [{ "Id": "inter_win", "Routes": ["max"], "CooldownSeconds": 60 }],
  "Policy": { "InterstitialMinIntervalSeconds": 180 }
}
```

Fields left out keep the asset's values; unknown provider ids are dropped with a warning; malformed JSON means no plan.
A game can register its own `IAdsPlanSource` before the Ads module runs instead. Without either, the asset is used as is.
