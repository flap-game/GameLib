# GameLib Extensions

Everyday helpers for game code, in one namespace. No GameLib dependency (only uGUI), no asset, nothing to set up:
install it and add `using FlapGame.GameLib.Extensions;`.

Install from **GameLib > Setup > Extensions**, or add to `Packages/manifest.json`:

```
https://github.com/flap-game/GameLib.git?path=Packages/com.flapgame.gamelib.extensions#v0.8.4
```

## What is in it

| Area | Members |
|---|---|
| Collections | `IsNullOrEmpty()`, `IsValidIndex(i)`, `GetOrDefault(i, fallback)`, `AddRange` on any `ICollection`, `RandomItem()`, `TryRandomItem(out x)`, `RemoveRandom()`, `Shuffle()`, `List.Resize(n, fill)`, `array.Resized(n, fill)` |
| Random | `RandomHelper.Chance(0.25f)`, `WeightedIndex(weights)` (-1 when no weight > 0), `WeightedItem(items, weights)` |
| Transform | `FindDeep(name)`, `FindDeepStartingWith(prefix)`, `FindDeepContaining(part)`, `DestroyChildren()`, `SetActiveChildren(on)`, `ActiveChildCount()`, `SetPositionX/Y/Z`, `SetLocalPositionX/Y/Z`, `SetLocalScale(s)`, `ResetLocal()` |
| GameObject | `GetOrAddComponent<T>()`, `SetLayerRecursively(layer)`, `SetActiveChildren(on)`, `OrNull()` |
| Vectors | `WithX/Y/Z`, `Flat()`, `XY()`, `XZ()`, `ToVector3XY(z)`, `ToVector3XZ(height)`, `AngleDegrees()`, `AngleDegreesTo(p)`, `Rotate(deg)`, `DirectionFromDegrees(deg)`, `IsFinite()`, `ClosestPointOnLine(p, dir)` |
| Colors | `WithAlpha(a)`, `ToHex()`, `ToHexRgba()`, `ColorExtensions.ParseHex("#FFAA00")`, `TryParseHex` |
| uGUI | `CanvasGroup.SetVisible(on)`, `SetInteractable(on)`, `Graphic.SetAlpha(a)` (Image, Text, TextMeshProUGUI), `Image.SetSizeByWidth/Height` (keeps the sprite's proportions), `RectTransform.SetSizeByWidth/Height(size, aspect)` |
| Text | `IsNullOrEmpty()`, `IsNullOrWhiteSpace()`, `Truncate(max)` |
| Math | `MathHelper.Remap`, `RemapClamped`, `Approximately`, `Wrap(index, count)` |
| Numbers | `NumberFormatter.Compact(1500)` "1.5K" (cut, never rounded up: 1960 is "1.9K"), `WithSeparators(1234567)` "1,234,567", `Percent(0.25f)` "25%" (culture-invariant) |
| Time | `TimeFormatter.MinutesSeconds` "05:30", `HoursMinutesSeconds` "01:05:30", `Clock` (picks one), `Short` "2h 15m". Seconds round up for countdowns (never 00:00 early); `roundUp: false` for time played |
| Coroutines | `this.Delay(1f, Do)`, `this.NextFrame(Do)`, `this.WaitUntil(() => ready, Do, timeoutSeconds: 5, onTimeout: Fail)`; stopped with the MonoBehaviour |
| Network | `NetworkHelper.HasConnection` (Wi-Fi or data on, not proof the internet answers), `IsOnWifi` |

Random picks use `UnityEngine.Random`; each one has an overload taking a `System.Random` for seeded, repeatable results.

## Coming from CoreGame / AFramework

| Old | GameLib |
|---|---|
| `list.GetRandom()` (CoreGame), `RandomItem()` (AFramework) | `list.RandomItem()` |
| `RandomHelper.WeightedRandom(items, weights)` | `RandomHelper.WeightedItem(items, weights)` |
| `IsIndexOutOfList(i)` | `!IsValidIndex(i)` |
| `Utility.ResizeArray(a, n, fill)` | `a.Resized(n, fill)` |
| `FindDeepChild` / `FindDeepChildLower` / `FindDeepChildWithStartName` / `...ContainName` | `FindDeep` / `FindDeepStartingWith` / `FindDeepContaining` |
| `DestroyAllChildren`, `DestroyAllChildrenImmediate`, `DeleteAllChilds` | `DestroyChildren()` (Destroy in Play mode, DestroyImmediate in Edit mode) |
| `SetActiveChilds` | `SetActiveChildren` |
| `GetChildCount(includeInactive: false)` | `ActiveChildCount()` (`childCount` for all) |
| `Image.SetAlpha` | `Graphic.SetAlpha` (same call) |
| `ToHexWithAlpha`, `FromHex`, `TryFromHex` | `ToHexRgba`, `ColorExtensions.ParseHex`, `TryParseHex` |
| `ToVector2XY` / `ToVector2XZ` | `XY()` / `XZ()` |
| `GetAngleInDegree(v)` / `(a, b)` | `AngleDegrees()` / `AngleDegreesTo(b)` |
| `Vector2.Rotate(angle)` (AFramework: clockwise) | `Rotate(-angle)` (GameLib: counter-clockwise) |
| `Quaternion.IsValid`, `Vector3.IsValid` | `IsFinite()` |
| `NearestPointOnLine(dir, p, pointOnLine)` | `p.ClosestPointOnLine(pointOnLine, dir)` |
| `NumberFormatter.Format`, `FormatWithSeparators`, `FormatPercentage` | `Compact` (now cut: 1960 is "1.9K", was "2K"), `WithSeparators`, `Percent` |
| `TimeFormatter.ToMMSS` / `ToHHMMSS` / `ToShortString` (CoreGame: floored) | `MinutesSeconds` / `HoursMinutesSeconds` / `Short` with `roundUp: false` for the same output |
| `Utility.TimeFormat` (AFramework: rounded up) | `HoursMinutesSeconds` |
| `CoroutineHelper.WaitAndCall`, `Utility.CRDelayFunction` | `this.Delay(seconds, action)` |
| `WaitForCondition(WithTimeout)` | `this.WaitUntil(condition, done, timeout, onTimeout)` |
| `Utility.HasInternet()` | `NetworkHelper.HasConnection` |
| `TruncateWithEllipsis` | `Truncate` |
| `Utility.GetCurrentTime()` / `GetCurrentDate()` | `DateTime.UtcNow` / `DateTime.UtcNow.Date` |
| `GetValueOrDefault(dict, key, fallback)` | built in: `dict.GetValueOrDefault(key, fallback)` |

Left out on purpose: helpers tied to one game or framework (`IsVisible` on AFramework's CanvasManager, Google Sheet,
reflection, device info, `CreateCube`), `UnityObjectHelper.EnsurePersistent` (`DontDestroyOnLoad` is one line), and
one-line wrappers of Unity calls (`FindInChildren`, `LayerMask.GetRaycastValue`).
