# Changelog

## [0.7.1] - 2026-10-26
- After an activation, reads come from the activated values instead of one Firebase native call and string decode per read. Before the first activation of the session reads still ask Firebase (it keeps the previous activation).

## [0.5.0] - 2026-10-26
- The cache file is now gamelib_remote_config_v2.json; the old one (which also held the in-app defaults) is deleted. The cache is rewritten after every activation, also when the server returns no values.
- The disk cache keeps server values only (it also saved the in-app defaults, so a default changed in an update never reached existing users and a key removed in the console never returned to its default). `IRemoteConfigProvider.GetAll`/`TryGetString` are now `GetRemoteValues`/`TryGetRemoteString` and return server values only.
- A fetch call while a timed-out fetch still runs joins it instead of starting a second SetDefaults + fetch (two Activated callbacks, two cache writes).

## [0.3.0] - 2026-10-26
- First release (ported from CoreGame 0.6.7): IRemoteConfig on Firebase Remote Config; reads fall back to the last session cache and the asset defaults before the fetch, so values work at bootstrap; fetch timeout, late activation, every activated value cached.
