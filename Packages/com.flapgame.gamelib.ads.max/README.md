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
