# GameLib App Tracking Transparency

The iOS App Tracking Transparency (ATT) prompt. Install from **GameLib > Setup > App Tracking Transparency** (AppsFlyer
adds it too). Android and the Editor have no ATT: the status is `NotApplicable`, nothing is shown, nothing waits.

## Configure (AttModule)

| Field | |
|---|---|
| Timing | `AfterConsent` (default): GameLib shows the prompt at launch, after the consent form. `Manual`: only when the game calls `RequestAsync`. |
| Delay Seconds | `AfterConsent`: extra wait between the consent answer and the prompt (default 0). |
| Consent Wait Seconds | `AfterConsent`: longest wait for the consent form answer (default 60). Without a consent form (no Ads CMP) there is no wait. |
| Usage Description | Info.plist `NSUserTrackingUsageDescription`, the sentence on the prompt. Written at build time unless Info.plist already has one. |

Order on launch: consent form (Ads module's CMP) → delay → ATT prompt → AppsFlyer starts with the answer. The prompt shows
once per install; when the user answered (here, or in MAX's own consent flow) it is never shown again.

## Show it yourself

```csharp
IAttService att = GameLibBootstrap.Services.Resolve<IAttService>();
AttStatus status = await att.RequestAsync(); // Authorized / Denied / Restricted; NotApplicable on Android
```

`RequestAsync` returns the current status at once when it was already answered. The prompt is shown only while the app
is active and is asked again if iOS skipped it; the trip to the system alert is never counted as a return to the app,
so it does not trigger an app open ad. Every answer is written to `IConsentState` (`ConsentState.Att`).

With `Manual`, keep AppsFlyer's *Att Wait Seconds* in mind: AppsFlyer starts without the IDFA when the prompt comes later.

Install by hand:

```
https://github.com/flap-game/GameLib.git?path=Packages/com.flapgame.gamelib.att#v0.8.4
```
