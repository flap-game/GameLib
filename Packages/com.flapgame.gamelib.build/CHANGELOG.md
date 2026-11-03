# Changelog

## [0.8.14] - 2026-11-03
- iOS pod build settings (`IosPodBuildSettingsFix`, after EDM4U's pod install): pod targets below the game's minimum iOS
  version are raised to it in Pods.xcodeproj (Xcode 27 rejects the 10.0 / 12.0 of several MAX adapters), and with the
  Yandex / AppMetrica pods present every pod is built with `BUILD_LIBRARY_FOR_DISTRIBUTION = NO` (otherwise "Undefined
  symbol ... AppMetricaLibraryAdapter.shared"). Games can delete their own copies of these fixes.
- iOS editor target guard (`IosEditorTargetGuard`): an iOS build with the Editor still compiled for another target fails
  before it starts, instead of shipping without every `#if UNITY_IOS` post-process (ATT usage text, MAX Info.plist keys).

## [0.8.13] - 2026-10-28
- iOS pod install guard (`IosPodIntegrationGuard`): a build on macOS whose `pod install` did not finish fails right away
  with the cause, instead of later in fastlane with a misleading Objective-C compile error.

## [0.6.0] - 2026-10-26
- New package: Unity Cloud Build pre-export method `GameLibCloudBuild.PreExport` (Unity splash off),
  `CloudBuildSettings` asset, and the App Store upload post-build script (GameLib > Cloud Build > Install App Store Upload Script).
