# Changelog

## [0.5.0] - 2026-10-26
- The prompt waits for the app to be active however long it takes (it used to give up after 10 s and lose the prompt for the launch).
- Info.plist gets NSUserTrackingUsageDescription only when AttModule is listed in GameLibSettings.

## [0.4.0] - 2026-10-26
- First release: iOS App Tracking Transparency prompt after the consent form with an optional delay, or when the game calls IAttService.RequestAsync. The answer goes to IConsentState; the prompt never triggers an app open ad. Info.plist NSUserTrackingUsageDescription and the weak AppTrackingTransparency link are added at build time (moved from the AppsFlyer module).
