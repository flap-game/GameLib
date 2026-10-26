# Changelog

## [0.3.0] - Unreleased
- IRemoteConfig contract and RemoteConfigJson (Newtonsoft parsing shared by every remote config provider).
- BufferedAnalyticsSink<T>: buffers calls for an SDK that starts in the background, flushes in order, turns itself off on failure or timeout.
- Disposable.Create, GameLibModuleOrder.RemoteConfig.
- GameLib > Setup: Firebase modules available; installs Firebase tarballs into Packages/GoogleTgz; a module's SDK step covers the SDKs of the modules it depends on.

## [0.2.0] - 2026-10-26
- IConsentService gains Status, CanRequestAds and RequestAsync (the CMP contract the Ads module drives); ConsentStatus moved to core.
- GameLib > Setup installs SDKs from scoped registries (AppLovin, OpenUPM) and handles modules without an asset of their own.
- Depends on com.unity.nuget.newtonsoft-json (manifest editing).

## [0.1.0] - 2026-10-26
- Bootstrap that starts itself from Resources/GameLibSettings; modules are ScriptableObjects (settings + installer) run by Order.
- Core services: IAppLifecycle (with external-activity aware Resumed), IConsentState, IEntitlements, IAnalytics with ad revenue and purchase events.
- GameLib > Setup window: step-by-step module install, module assets, project check; build validator.
