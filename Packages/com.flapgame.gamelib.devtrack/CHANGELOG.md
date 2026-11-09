# Changelog

## [0.9.1] - 2026-11-09
- New package, Editor only: a dev's Play-mode sessions are reported to the internal tool
  (`POST https://tools.puzzleclassic.com/sdk/v1/dev-play`), so the team sees who works on which project, from which
  machine, which git repo / branch / user and from which IP. `play_start` on entering Play, a `heartbeat` every 60 s,
  `play_stop` with the elapsed seconds on leaving Play or on quitting. The open session, the queue and the context live
  in `SessionState`, so the domain reload around Play neither loses nor duplicates a session.
- Off in batch mode (CI, Unity Cloud Build), with `GAMELIB_DEVTRACK=off`, or from
  **GameLib > Dev Tracking > Enabled**. Endpoint overridable with `GAMELIB_DEVTRACK_ENDPOINT` or EditorPrefs.
  An unreachable server costs one warning per Editor session and nothing else: bounded queue, one polled request at a
  time, backoff, give-up after 4 attempts.
- No dependency on `com.flapgame.gamelib.core`: it drops into any project, including the ones without GameLib.
- Minimum Unity 2021.3, not 6000.4 like the rest of the set: the old games have to report too, and the package uses
  no API or C# syntax a newer Editor introduced.
