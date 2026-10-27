# Changelog

## [0.8.0] - 2026-10-27
- New package. `ISaveService`: typed sections with a schema version and migrations (`Section<T>`), small values by key
  (`Save` / `Load`), scopes (`ForScope`), autosave a few seconds after a change, a write on pause / focus loss / quit.
- One file in `persistentDataPath`, written crash-safe (fsynced `.tmp`, previous file kept as `.bak`) and checked by an
  HMAC-SHA256 checksum; optional AES-256 encryption. Read falls back to `.tmp` / `.bak`, and prefers a complete `.tmp`
  newer than the file (a crash between fsync and rename); a damaged file is moved to
  `.corrupt-*`; a file that cannot be read is never overwritten; a section that cannot be read starts fresh and its data
  is kept in `quarantine`. A damaged save is reported to Crashlytics and as `save_load_issue`.
- Cloud-ready: `ExportSnapshot` / `ImportSnapshot`, metadata (progress, playtime, save count, times), `ICloudSaveBackend`
  and `SaveSync.SyncAsync` (keeps the copy with more progress). No cloud backend ships yet.
- Editor: GameLib > Save > Open Save Folder / Show Save As JSON / Delete Save.
