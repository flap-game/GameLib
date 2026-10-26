# Changelog

## [0.1.0] - 2026-10-26
- Bootstrap that starts itself from Resources/GameLibSettings; modules are ScriptableObjects (settings + installer) run by Order.
- Core services: IAppLifecycle (with external-activity aware Resumed), IConsentState, IEntitlements, IAnalytics with ad revenue and purchase events.
- GameLib > Setup window: step-by-step module install, module assets, project check; build validator.
