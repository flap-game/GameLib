# GameLib Dev Tracking

**Editor only.** While a dev plays the game in the Editor, this package reports the Play session to the team's internal
tool: which project, from which machine, which git repo / branch / user, and from which IP. The team works from home, so
the owner can see on the web tool who is on which project - and notice a project opened from a machine nobody expected.

Nothing of it reaches a build: one Editor assembly (`includePlatforms: ["Editor"]`), no runtime code, no scene setup, no
asset, no `GameLibModule`. It does not depend on `com.flapgame.gamelib.core` either, so it drops into any Unity project,
including the old ones that do not use GameLib.

## Install

In a GameLib project, through **GameLib > Setup > Dev Tracking**, or by hand with the git URL:

```json
"com.flapgame.gamelib.devtrack": "https://github.com/flap-game/GameLib.git?path=Packages/com.flapgame.gamelib.devtrack#v0.9.1"
```

In a project without GameLib, the same line in `Packages/manifest.json` is all it needs (plus
`com.unity.modules.unitywebrequest`, which almost every project already has).

As an embedded package, so that the project carries it: copy the folder to `Packages/com.flapgame.gamelib.devtrack/` in
the project and commit it. Unity picks it up on the next focus; nothing else to add to the manifest.

## What it sends

`POST https://tools.puzzleclassic.com/sdk/v1/dev-play`, `application/json`, no auth, any 2xx is success (the server
answers 204). One event per POST:

| Event | When |
|---|---|
| `play_start` | Play mode was entered. A new `sessionId` (GUID) per Play session. |
| `heartbeat` | Every 60 s while Play runs, so a session that never reports its stop is still visible as "was playing". |
| `play_stop` | Play mode was left, or the Editor quit with a session still open. |

```json
{"event":"play_start","sessionId":"2f1c4b0e-7a3d-4a1b-9c2e-8d6f5a4b3c2d","projectName":"Word Jigsort","projectPath":"D:\\Funnii\\UnityProjects\\word-jigsort","bundleId":"com.flapgame.wordjigsort","unityVersion":"6000.4.1f1","buildTarget":"Android","gitUrl":"https://github.com/flap-game/word-jigsort.git","gitBranch":"main","gitCommit":"5f64b61a0c9e4d2b8a71f0c3d4e5f6a7b8c9d0e1","gitUserName":"Tuan Anh Tran","gitUserEmail":"tuananh@flapgame.com","pcUser":"TuanAnhTran","pcName":"FLAP-PC-07","localIp":"192.25.20.113","clientVersion":"0.9.1"}
```

| Field | What |
|---|---|
| `event` | `play_start`, `heartbeat` or `play_stop`. Always present. |
| `sessionId` | GUID, one per Play session; the three events of a session share it. Always present. |
| `projectName` | `Application.productName`. Always present. |
| `projectPath` | The project folder (the parent of `Application.dataPath`) |
| `bundleId` | `Application.identifier` |
| `unityVersion` | `Application.unityVersion` |
| `buildTarget` | `EditorUserBuildSettings.activeBuildTarget` |
| `gitUrl`, `gitBranch`, `gitCommit` | `origin`'s url, the branch, the commit sha of the checkout the project sits in |
| `gitUserName`, `gitUserEmail` | `user.name` / `user.email` as `git` resolves them (global config included) |
| `pcUser`, `pcName` | `Environment.UserName` / `Environment.MachineName` |
| `localIp` | The machine's primary IPv4 (the interface that has a gateway) |
| `durationSeconds` | Seconds in Play mode. Only on `heartbeat` and `play_stop`. |
| `clientVersion` | This package's version. One `const` in `DevTrackConfig`; it tracks package.json, keep the two in step. |

A field with no value is left out (`event`, `sessionId` and `projectName` are sent empty rather than missing). The git
and machine fields are collected **once per Editor session** on a background thread, so a heartbeat costs nothing and
nothing waits on `git`: `git config --get ...`, `git rev-parse` and `git remote get-url origin` are asked first, because
only `git` sees the dev's global config; without `git` on PATH, `.git/HEAD`, `.git/refs/...` (or `.git/packed-refs`) and
`.git/config` are parsed instead. The repository root may be above the Unity project folder.

## Turning it off

| Switch | Effect |
|---|---|
| `Application.isBatchMode` | Off. CI and Unity Cloud Build never report. Wins over everything else. |
| `GAMELIB_DEVTRACK=off` | Off for that Editor (an environment variable, so it also works for a machine-wide setting). `=on` turns it on whatever the menu says. A value that is neither on nor off is ignored. |
| **GameLib > Dev Tracking > Enabled** | The per-machine toggle (EditorPrefs `GameLib.DevTrack.Enabled`, on by default). Read once when the Editor loads, so a change applies from the next domain reload - entering Play, or a script change. |

The endpoint is overridable the same way, for pointing a test Editor at a staging server:
`GAMELIB_DEVTRACK_ENDPOINT=<url>`, else EditorPrefs `GameLib.DevTrack.Endpoint`, else the
`DevTrackConfig.DefaultEndpoint` const.

## The menu

**GameLib > Dev Tracking**:

- **Enabled** - the toggle above.
- **Show State** - logs on/off and why, the endpoint, the open session and its seconds, the queue (size, dropped, given
  up), and the exact `play_start` body this project produces.
- **Send Test Event** - queues a `play_start` + `play_stop` pair on a throwaway session id, for checking the server end
  without entering Play.

## Failures cost nothing

A dev whose machine cannot reach the server must not notice this package at all:

- the queue is a bounded FIFO (50 events, oldest dropped) kept in `SessionState`, and it is also the retry buffer;
- one `UnityWebRequest` at a time, polled from `EditorApplication.update` - no coroutine, nothing that blocks the main
  thread, and a request timeout of 10 s;
- a failed event is retried after 5 s, then 10 s, then 20 s, and is given up on after 4 attempts;
- **at most one warning per Editor session**, and nothing at all in normal operation. No error wall, ever.

Entering and leaving Play both reload the domain, which wipes statics. The open session (`sessionId`, start time, last
heartbeat), the queue and the collected context live in `UnityEditor.SessionState`, so a reload - including a script
change in the middle of Play - neither loses nor duplicates a session. `SessionState` dies with the Editor process, so a
session can never be resurrected by the next Editor.

## How it is put together

The logic is in pure classes with no Editor dependency, and `DevTrackHook` is the thin `[InitializeOnLoad]` adapter that
calls them from `EditorApplication` - that split is what makes the lifecycle testable without entering Play mode.

| Class | Does |
|---|---|
| `DevTrackConfig` | The endpoint const, the client version, and the off switches (`Decide` / `ResolveEndpoint` are pure) |
| `DevTrackPayload` | The JSON body, written by hand |
| `DevTrackState` | What survives a domain reload, and `SessionState` as its store (`IDevTrackStateStore`) |
| `DevTrackQueue` | The bounded FIFO over the state's list: cap, backoff, give-up |
| `DevTrackSession` | The state machine: start, heartbeat, stop; takes the clock as an argument |
| `DevTrackGit` | `git`, with the `.git` files as the fallback |
| `DevTrackContext` / `DevTrackContextProbe` | The context, collected once on a background thread |
| `DevTrackSender` | The polled `UnityWebRequest` pump |
| `DevTrackHook` / `DevTrackMenu` | `EditorApplication` and the menu |

Tests: `Tests/Editor` (payload, session state machine with a reload in the middle, queue, git parsing from a fake
`.git` on disk, off-switch precedence).
