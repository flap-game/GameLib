# Changelog

## [0.8.1] - 2026-10-27
- New package, no GameLib dependency (only uGUI). The helpers games used most from CoreGame `Common` and AFramework
  `ExtensionMethods` / `Utility`, rewritten in one namespace, `FlapGame.GameLib.Extensions`:
  collections (`IsNullOrEmpty`, `RandomItem`, `RemoveRandom`, `Shuffle`, `Resize`, `IsValidIndex`; seeded overloads),
  `RandomHelper` (`Chance`, `WeightedIndex`), Transform (`FindDeep*`, `DestroyChildren`, `SetActiveChildren`, position
  setters), GameObject (`GetOrAddComponent`, `SetLayerRecursively`, `OrNull`), vectors, colors (hex), uGUI
  (`SetVisible`, `SetAlpha` on any Graphic, size from the sprite), `MathHelper`, `NumberFormatter`, `TimeFormatter`,
  `CoroutineHelper` (`Delay`, `NextFrame`, `WaitUntil` with timeout), `NetworkHelper`.
- `NumberFormatter.Compact` cuts to one decimal instead of rounding (1960 is "1.9K", 999_999 is "999.9K"), so a balance
  never reads higher than it is. `TimeFormatter` rounds seconds up for countdowns; `roundUp: false` for time that counts up.
