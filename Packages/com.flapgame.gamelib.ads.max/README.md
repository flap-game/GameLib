# GameLib Ads - AppLovin MAX

AppLovin MAX provider for GameLib Ads. Install from **GameLib > Setup > Ads - AppLovin MAX**: it adds this package,
the AppLovin scoped registry and `com.applovin.mediation.ads`. The provider code compiles only once the MAX package is
present (asmdef `versionDefines` -> `GAMELIB_MAX`), then registers itself - nothing to add to `GameLibSettings`.

## Configure

1. **AppLovin > Integration Manager**: SDK key, mediated network adapters, AdMob app id when AdMob is mediated.
2. **AppLovin > Integration Manager > Terms and Privacy Policy Flow**: turn it on to use MAX's consent flow
   (Google UMP). It is a build-time setting; GameLib runs it before any ad loads. When the AdMob provider is installed
   too, its UMP takes over and the MAX flow must be off, otherwise the player sees two forms.
3. **AdsModule**: add a `max` row in Providers, and `max` routes with the MAX ad unit ids (Android and iOS) on each placement.

Test mode: tick *Test Mode* on `AdsModule` and add your device's advertising id to *Max Test Device Ids*.

Revenue is reported per impression in USD (MAX reports no other currency) with the network that served it.

## QA

- `await ads.OpenDebuggerAsync()`: the Mediation Debugger (every network, its adapter, test ads).
- `ads.OpenCreativeDebugger()`: the Creative Debugger - the ads shown recently, their network and creative, and a way to
  report a bad one. It also opens on a fullscreen MAX ad when the device is flipped face down twice; turn that off with
  `AdsModule > Max Creative Debugger On Flip`.
- `AdsModule > Verbose Logs` also turns on the AppLovin SDK's own log (logcat / Xcode tag `AppLovinSdk`).

## Segments

`ads.SetAdSegment(key, values)` puts the player in a MAX segment, so the MAX dashboard can give that segment its own
waterfall or floors: for example key 1 = payer (value 1 once they bought something), key 2 = level band. Keys and values
are 0..32000, numbers the game picks and enters in the dashboard the same way. `ads.SetAdSegment(key)` with no value
takes the player out of it. Call it whenever the value changes; GameLib passes the set to MAX before it initializes and
again on every change.

## Extra parameters

For a parameter AppLovin documents or its support asks for, without a GameLib change:

- SDK-wide: `AdsModule > Max Extra Parameters` (`MaxSdk.SetExtraParameter`, set before the SDK initializes).
- One ad unit: the placement's `Max Extra Parameters` (`Set*ExtraParameter` for its format, set before its first load; a
  banner gets them right after its view is created).

Keys and values are strings. Local extra parameters (objects such as an Amazon ad response) are not covered.

## Banners

- `Banner Background Color` on a banner placement: an opaque color behind the banner. AppLovin asks for a
  non-transparent one for banners to be fully functional. Alpha 0 (default) = not set. Not for MREC.
- A banner or MREC that opens fullscreen content (an expandable creative) counts as a fullscreen ad while it is open: the
  fullscreen pause and mute apply, and no interstitial shows over it. AdMob banners do the same.
