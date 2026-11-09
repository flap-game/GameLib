# GameLib Analytics - Local

Local tracking for the test devices, the same idea as aframework's `LocalAnalytics`: while the build runs on the office
LAN, every `IAnalytics` call is POSTed to the internal tracking server and the **online analytics sinks stay off**, so
a test session never shows up in the production dashboards. On a player's device nothing matches and the module does
nothing - it can stay in a shipped build.

Install from **GameLib > Setup > Analytics - Local** (it only needs the Unity Web Request and Android JNI modules).

```
https://github.com/flap-game/GameLib.git?path=Packages/com.flapgame.gamelib.analytics.local#v0.9.1
```

## When it turns on

| Where | Condition |
|---|---|
| Device build | One of the device's own IPv4 addresses starts with a prefix in **Lan Address Prefixes** (default `192.25.20`) |
| Editor | **Track In Editor** is on (no LAN check) |
| Anywhere else | Off, with one log line naming the addresses it saw |

The decision is made while the module installs, before the online sinks, and it is published as `ILocalTracking` in
`core`. Each online sink asks `LocalTracking.SuppressesOnline(services)` before installing itself:

| Module | While local tracking is on |
|---|---|
| Analytics - Firebase | Off (Firebase Analytics is not even started) |
| AppsFlyer | **On**, as in aframework - a test install still has to attribute like a real one. Turn **Suppress Attribution** on to stop it too |
| Crashlytics, Remote Config | Untouched |
| Debug console sink | Untouched (it is what you read in the Editor) |

Turn **Suppress Online Tracking** off to send to the local server *and* the online sinks.

## What is sent

One POST per event, `application/json`, exactly the body aframework sent:

```json
{"bundleId":"com.flapgame.game","appVersion":"1.2.3","idfa":"<device>","eventName":"level_complete","params":{"level":3,"eventCount":12}}
```

- `params` is left out when the event has none; `eventCount` is the event's 0-based sequence number in the session, so a
  gap in the server's rows is visible.
- `idfa` is the advertising id: the IDFA on iOS (after the ATT answer), the Google advertising id on Android (read
  through Play Services on a worker thread, because Unity 6 no longer wraps it). Without one - the player opted out, no
  Play Services - it falls back to `SystemInfo.deviceUniqueIdentifier`.
- `TrackAdRevenue` becomes an `ad_impression` event with the same parameter names as the Firebase sink
  (`ad_platform`, `ad_source`, `ad_format`, `ad_unit_name`, `value`, `currency`, `placement`, `precision`, `country`).
- `TrackPurchase` becomes a flat `purchase` event (`product_id`, `product_kind`, `value`, `currency`,
  `transaction_id`, `placement`, `is_test`).
- `SetUserId` and `SetUserProperty` are ignored: the server stores events and joins a device's rows on `idfa`.

## Sending and failures

Events are queued and posted **one at a time, in order**: an event leaves the queue only after a 2xx answer, so a
failed post is retried with the next one still behind it. Then:

- **Max Consecutive Failures** failed posts in a row (default 10, as in aframework) turn local tracking off for the
  session: the queue is dropped and later calls are no-ops. One warning says why.
- **Max Queued Events** (default 500) caps the queue while the server is unreachable; the oldest event is dropped and
  the first drop is logged. `eventCount` keeps counting, so the gap is visible on the server.
- On iOS the first event waits for the ATT answer (**Att Wait Seconds**, default 60; 0 = forever). Anything but
  *Authorized* turns local tracking off for the session - as in aframework, where there is no IDFA to join on.
  Note that the online sinks were already skipped by then, so that session reports nothing at all; it only ever happens
  on a test device, where ATT is answered with Allow.
- Shutdown (`GameLibBootstrap.Reset`, quitting) cancels the in-flight request and leaves the rest of the queue.

## Settings

| Field | Default | What it does |
|---|---|---|
| Endpoint | `https://tools.puzzleclassic.com/sdk/v1/tracking` | Where the events go |
| Lan Address Prefixes | `192.25.20` | IPv4 prefixes that mark a test device |
| Track In Editor | off | Also post from Play mode |
| Suppress Online Tracking | on | Keep the online analytics sinks off while active |
| Suppress Attribution | off | Also keep AppsFlyer off |
| Request Timeout Seconds | 10 | Per POST |
| Retry Delay Seconds | 1 | Between a failed post and the retry, and between two polls of an empty queue |
| Max Consecutive Failures | 10 | Failed posts before giving up for the session |
| Max Queued Events | 500 | Queue cap while the server is unreachable |
| Att Wait Seconds | 60 | iOS only; 0 waits forever |

## Checking it works

In the Editor turn **Track In Editor** on and watch the console: `[LocalAnalytics] On (Editor, Track In Editor is on)
-> <endpoint>; the online analytics sinks stay off.` A device off the LAN logs `[LocalAnalytics] Off: no local address
on 192.25.20 (this device: ...)`.
