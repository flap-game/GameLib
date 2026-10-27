# Changelog

## [0.8.0] - 2026-10-27
- New package. `IAppIconService`: alternate app icons on iOS (`IconNames`, `CurrentIcon`, `SetIconAsync`; simulated in
  the Editor). Icons are listed on the `AppIconModule` asset (name + image); the iOS build writes them as 1024 px app
  icon sets into the Xcode asset catalog and sets `ASSETCATALOG_COMPILER_ALTERNATE_APPICON_NAMES`.
