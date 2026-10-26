# Changelog

## [0.5.0] - 2026-10-26
- iOS: Control Center, Notification Center, an incoming-call banner or a system alert no longer raise IAppLifecycle.Resumed (Unity pauses for them on iOS, not on Android); only a real trip to the background does - so no app open ad after pulling Control Center down. Native observer in Plugins/iOS/GameLibLifecycle.mm.
- BufferedAnalyticsSink.RunLatest(key, call): last call per key, never dropped, flushed before the queued calls - consent for Firebase and AppsFlyer uses it, so events never reach Firebase without consent mode. A full buffer of ad revenue / purchases drops a new plain event instead of revenue.
- BufferedAnalyticsSink: a timeout no longer disables the sink. It logs one warning and keeps buffering; a start that lands later flushes everything ("late ready"), a later failure disables it then.
- BufferedAnalyticsSink: a full buffer drops the oldest event first; ad revenue and purchases are dropped only when nothing else is left. New optional `bufferEvent` filter: events the SDK would not send are not buffered.

## [0.4.0] - 2026-10-26
- IAttService contract (App Tracking Transparency status and prompt), implemented by the new ATT module.
- IConsentState.WaitUntilResolvedAsync: wait for the CMP answer with a timeout.
- BufferedAnalyticsSink.TryGetPendingUserId: the user id set before the SDK started.
- GameLib > Setup: ATT module; AppsFlyer installs plugin 7.0.3 and the ATT module with it.

## [0.3.0] - 2026-10-26
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
