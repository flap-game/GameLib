# GameLib Core

Foundation package of GameLib. Every other `com.flapgame.gamelib.*` package depends on it.

Install:

```
https://github.com/flap-game/GameLib.git?path=Packages/com.flapgame.gamelib.core#v0.8.13
```

Then open **GameLib > Setup**, create the settings asset (Core), and add modules one by one.

## Startup

`Assets/GameLib/Resources/GameLibSettings.asset` lists the module assets. With **Auto Initialize** on (default)
GameLib starts by itself before the first scene loads - no bootstrap object in any scene. Turn it off to call
`GameLibBootstrap.InitializeAsync()` yourself.

```csharp
IServiceRegistry services = await GameLibBootstrap.WhenReadyAsync();
var analytics = services.Resolve<IAnalytics>();
analytics.Track("level_complete", ("level", 3), ("stars", 2));

if (services.TryResolve(out IHapticsService haptics))   // optional module
    haptics.Play(HapticType.Success);
```

A module that throws during startup is logged and skipped; tick **Required** on its asset to fail fast instead.

## Services always present

| Service | Purpose |
|---|---|
| `IAppLifecycle` | Pause / focus / quit events, plus `Resumed` with `FromExternalActivity`: wrap ad shows and purchases in `BeginExternalActivity(...)` (or call `MarkExternalActivity(...)` before opening a link) so coming back from them is not counted as the player returning. |
| `IConsentState` | The privacy state (GDPR, CCPA, COPPA, ATT) written by the module that owns the CMP and read by every SDK module. |
| `IEntitlements` | Durable owned keys. IAP grants `Entitlements.NoAds`, Ads reads it; games can add their own. |
| `ITrackerIds` | The ids the trackers gave this install: `FirebaseAppInstanceId()`, `AppsFlyerId()` (null until the SDK answered once; kept on the device for later launches), `Changed`. For an `af_firebase_id`-style parameter. |
| `IAnalytics` | Events, user id / properties, `TrackAdRevenue`, `TrackPurchase`, fanned out to every installed sink. Logs to the Console in the Editor and development builds. |

## Checks

**GameLib > Setup > Check project** lists missing packages, SDKs and module settings. Errors also stop the player build.

## Scripts and agents

`GameLibCli` does what GameLib Setup does, from the command line, and writes a JSON report with an exit code:

```
Unity -batchmode -quit -projectPath <project> -executeMethod FlapGame.GameLib.Editor.GameLibCli.Install -gamelibModules ads.max,iap
Unity -batchmode -quit -projectPath <project> -executeMethod FlapGame.GameLib.Editor.GameLibCli.Doctor
```

Agents that integrate GameLib into a game follow [Documentation~/agent-guide.md](Documentation~/agent-guide.md).
