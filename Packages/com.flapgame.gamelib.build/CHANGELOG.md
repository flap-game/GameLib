# Changelog

## [Unreleased] - Unreleased
- iOS pod install guard (`IosPodIntegrationGuard`): a build on macOS whose `pod install` did not finish fails right away
  with the cause, instead of later in fastlane with a misleading Objective-C compile error.

## [0.6.0] - 2026-10-26
- New package: Unity Cloud Build pre-export method `GameLibCloudBuild.PreExport` (Unity splash off),
  `CloudBuildSettings` asset, and the App Store upload post-build script (GameLib > Cloud Build > Install App Store Upload Script).
