# Changelog

## [0.9.2] - Unreleased
- Local tracking, ported from aframework's `LocalAnalytics`: on the office LAN (or in the Editor with `Track In
  Editor`) every event is POSTed to the internal tracking server, one at a time and in order, and the online analytics
  sinks stay off (`ILocalTracking` in core). Advertising id as `idfa` (IDFA after the ATT answer on iOS, Play Services
  on Android, device id as the fallback), `eventCount` on every event, retry with a give-up after 10 failures in a row,
  a queue cap, ad revenue as `ad_impression` and purchases as `purchase`.
