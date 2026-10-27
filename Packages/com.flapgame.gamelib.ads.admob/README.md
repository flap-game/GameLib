# GameLib Ads - AdMob

Google AdMob provider for GameLib Ads, with Google's UMP consent form. Install from **GameLib > Setup > Ads - AdMob**:
it adds this package, the OpenUPM scoped registry and `com.google.ads.mobile` 11.6.0. The provider code compiles only
once the Google Mobile Ads package is present (asmdef `versionDefines` -> `GAMELIB_ADMOB`), then registers itself -
nothing to add to `GameLibSettings`.

## Configure

1. **Assets > Google Mobile Ads > Settings**: the AdMob app ids (Android and iOS). AdMob reads them at build time, so the
   provider ignores app keys on `AdsModule`.
2. **AdsModule**: an `admob` row in Providers, and `admob` routes with the AdMob ad unit ids on each placement. Routes can
   mix providers: `max` first, `admob` as fallback, or the other way round per placement.
3. **Consent**: with AdMob installed, UMP is the CMP (it takes priority over MAX's flow - turn MAX's Terms and Privacy
   Policy Flow off so the player sees one form). Set up the GDPR message (and the US states message if you need it) in
   the AdMob console > Privacy & messaging. For a "Privacy settings" button, resolve `IConsentService` and show it when
   `IsPrivacyOptionsRequired`.

Test mode: tick *Test Mode* on `AdsModule` and add the hashed device id AdMob prints in logcat / the Xcode console to
*AdMob Test Device Ids*. *Consent Debug* forces the EEA geography to test the form.

Revenue is reported per impression (`OnAdPaid`) with the currency AdMob gives and the network from the response info.

## Mediation adapters

AdMob mediation (other networks bidding or in a waterfall inside an AdMob mediation group) needs Google's adapter for
each network in the app. **GameLib > Setup > Ads - AdMob > Optional** lists them - AppLovin, BidMachine, Bigo,
Chartboost, DT Exchange, i-mobile, InMobi, ironSource, Liftoff Monetize, LINE, maio, Meta Audience Network, Mintegral,
Moloco, myTarget, Pangle, PubMatic, Unity Ads, Verve - with Install / Remove per network. They are
`com.google.ads.mobile.mediation.<network>` from OpenUPM (the same registry and scope as Google Mobile Ads), pinned to
versions that need GMA 11.x. From the command line: `-gamelibOptional pangle,unity` next to `-gamelibModules ads.admob`.

Install only the networks the AdMob console's mediation groups use: each adapter adds that network's SDK to the build.
The network's own ids (app id, placement ids) go into the AdMob console's mediation setup, not into GameLib. Ad Inspector
(`ads.OpenDebuggerAsync()`) shows whether each adapter initialized.

A game that also uses MAX installs MAX's adapters through AppLovin's Integration Manager (**AppLovin > Integration
Manager**), not here. Both kinds for the same network bring that network's SDK twice; keep their versions compatible.

## External Dependency Manager

Google Mobile Ads 11.x depends on Unity's `com.unity.external-dependency-manager`, while MAX, Firebase and AppsFlyer
use Google's `com.google.external-dependency-manager` from OpenUPM. Both together compile (Unity's EDM disables itself);
see the GameLib README for what to check.
