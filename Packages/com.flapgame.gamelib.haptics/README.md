# GameLib Haptics

Vibration and haptic feedback for Android and iOS. A no-op in the Editor and on other platforms.

Install from **GameLib > Setup > Haptics**, or add to `Packages/manifest.json` (together with core, same tag):

```
https://github.com/flap-game/GameLib.git?path=Packages/com.flapgame.gamelib.haptics#v0.8.11
```

## Use

```csharp
var haptics = (await GameLibBootstrap.WhenReadyAsync()).Resolve<IHapticsService>();

haptics.Play(HapticType.LightImpact);          // semantic preset, best native feedback per platform
haptics.Vibrate(durationMs: 40, intensity: 0.8f, count: 2, gapMs: 80);
haptics.IsEnabled = settingsToggle.isOn;       // persisted player setting
```

Settings live on the `HapticsModule` asset: default on/off, persistence, global intensity, max duration.

## Platform notes

- **Android**: `android.permission.VIBRATE` is added to the generated manifest at build time (normal permission, no prompt).
  Uses predefined effects / amplitude control when the device supports them.
- **iOS**: `GameLibHaptics.mm` uses UIFeedbackGenerator for presets and Core Haptics for custom vibrations
  (links CoreHaptics.framework).
