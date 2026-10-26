# GameLib Remote Config - Firebase

`IRemoteConfig` backed by Firebase Remote Config. Install from **GameLib > Setup > Remote Config - Firebase** (it adds
`com.flapgame.gamelib.firebase`, External Dependency Manager, Firebase App and Firebase Remote Config), then put the
config files in place - see the [Firebase package README](../com.flapgame.gamelib.firebase/README.md).

```csharp
var config = services.Resolve<IRemoteConfig>();
int lives = (int)config.GetLong("start_lives", 5);

await config.WhenReadyAsync();               // where fresh values matter, e.g. before the first level
var offers = config.GetObject<List<Offer>>("offers", new List<Offer>());

config.Activated += () => ApplyBalance(config);   // a later fetch landed
```

## Where values come from

Reads never block and never throw. Each read takes the first of:

1. the value Firebase activated this session,
2. the value cached on disk by the last session's fetch,
3. the default on the `RemoteConfigModule` asset,
4. the default passed to the call.

So values are usable right at bootstrap - the Ads module reads its plan (`ads_plan`) from here before any network.

`GetBool` follows Firebase (`true/1/yes/on` and `false/0/no/off`); numbers are parsed invariant (`1.5`, not `1,5`);
`GetObject<T>` parses JSON with Newtonsoft (case-insensitive names, enums by name or number, unknown fields ignored).
Types only built by `GetObject` should carry `[UnityEngine.Scripting.Preserve]` so IL2CPP stripping keeps them.

## Settings (RemoteConfigModule)

| Field | Default | |
|---|---|---|
| Defaults | - | In-app values, used until the first fetch ever lands and for keys the server does not set. |
| Fetch Timeout Seconds | 5 | After this `WhenReadyAsync` completes with cached/default values; a late fetch still activates and raises `Activated`. |
| Minimum Fetch Interval Seconds | 3600 | Firebase server throttling. The SDK default (12 h) makes console changes take half a day. |
| No Fetch Throttle In Development | on | 0 s interval in the Editor and development builds. |
| Fetch In Background | on | Off = bootstrap waits for the fetch (or the timeout). |

Without the Firebase Remote Config package the module still works with defaults and cache, and logs a warning.
