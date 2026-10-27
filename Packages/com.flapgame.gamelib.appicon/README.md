# GameLib App Icon

Alternate app icons the player can pick. **iOS only** (iOS 10.3+); Android has no supported way, `IsSupported` is false.
The Editor simulates the change so an icon picker can be tested in Play mode.

Install from **GameLib > Setup > App Icon**, or add to `Packages/manifest.json` (together with core, same tag):

```
https://github.com/flap-game/GameLib.git?path=Packages/com.flapgame.gamelib.appicon#v0.8.6
```

## Setup

On the `AppIconModule` asset, add one row per alternate icon: a **Name** (letters, digits, `-`, `_`) and its **Image**
(square PNG / JPG, best 1024 x 1024). The default icon stays the one in Player Settings. The image is not put in the
game build: the iOS build step writes it into `Unity-iPhone/Images.xcassets` as `<name>.appiconset` (one 1024 px image,
alpha removed; Xcode 14+ makes the other sizes) and lists the names in `ASSETCATALOG_COMPILER_ALTERNATE_APPICON_NAMES`.

## Use

```csharp
var icons = (await GameLibBootstrap.WhenReadyAsync()).Resolve<IAppIconService>();
if (icons.IsSupported)
{
    foreach (string name in icons.IconNames) AddButton(name);
    bool ok = await icons.SetIconAsync("gold");   // null = back to the default icon
    string current = icons.CurrentIcon;           // null = default
}
```

iOS shows its own "You have changed the icon" alert; iOS remembers the choice, the game does not have to.

## Moving from AppIconChangerUnity (aframework)

Use the same names as `FrameworkGlobalConfig > iOS Icon > Alternative Icons` (`icon2` ... ) so players keep their icon,
then remove the old plugin and its build step: two build steps writing the same icon sets would clash.
