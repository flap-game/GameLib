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

## Native ads

Three AdMob-only formats (0.9.0), drawn by GameLib's own native views (Java in `Plugins/Android`, Objective-C++ in
`Plugins/iOS`, built in code: no layouts or image assets). Each view has the "AD" badge, registers every asset with the
SDK (clicks and impressions are tracked by Google) and hides the assets an ad does not have.

| Format | Shown with | Look |
|---|---|---|
| `NativeBanner` | `ShowBanner` / `HideBanner` | Full-width bar at the bottom (or top: `Position` Top / TopLeft / TopRight) of the safe area (iOS, at the bottom: the background reaches the screen's bottom edge and the home indicator auto-hides while the bar is up), height from `NativeBannerHeight`: `Adaptive` (default) = as tall as an adaptive banner on the device; `Smart` = the former smart banner (32 dp on a phone in landscape with icon, headline and button only, 50 dp in portrait, 90 dp on a tablet); `Fixed` = `NativeHeightDp` (0 = 100). |
| `NativeCollapsible` | `ShowBanner` / `HideBanner` | Bottom bar `NativeHeightDp` high (0 = 50). With `NativeStartExpanded` each show opens it over ~45 % of the screen with its media; a collapse button appears after `NativeCollapseButtonDelaySeconds`, inside the ad at its top-right corner, 20 dp from the top edge and 20 dp from the right edge, with its touch area reaching left and down so the AdChoices icon in the corner stays clear. In a landscape game the expanded ad puts the media in a left column (at least 120 dp, AdMob's video minimum) and the texts on the right; the layout follows the orientation the ad was shown in (games with a fixed orientation). |
| `NativeInterstitial` | like an interstitial | Full screen; a "Close in Ns" countdown (`NativeCloseDelaySeconds`, 0 = close button at once). Follows the interstitial pacing. |

- Colors: `NativeBackgroundColor`, `NativeTextColor`, `NativeCtaColor`, `NativeCtaTextColor` on the placement.
- Banner-like natives behave like banners: loaded on the first `ShowBanner` (or on activation with `PreloadBanner`), shown
  once loaded. A shown native ad is spent: `HideBanner` removes it and a fresh one loads while the placement is allowed;
  `DestroyBanner` releases it without reloading. Load failures retry with the usual backoff (2 s .. 60 s).
- `IBannerMetrics`: the bar height (`NativeHeightDp` in pixels; the collapsed bar for `NativeCollapsible`) while it is on
  screen; the expected height is the same value before it loads.
- `INativeCollapseSource.NativeExpandedChanged`: true when a show opens a `NativeCollapsible` expanded, false when the
  player collapses it (with `NativeHideOnCollapse` the ads service then hides it). A show that opens collapsed raises nothing.
- Clicks raise `IAdClickSource.AdClicked`; paid events raise `ImpressionPaid` like the other formats.
- Test mode without a unit id: Google's native demo unit (Android `ca-app-pub-3940256099942544/2247696110`, iOS
  `ca-app-pub-3940256099942544/3986624511`).
- Natives run on Android / iOS devices only. In the Editor every native load fails with `unsupported_platform` (and is
  retried with backoff), so the placement stays not ready there.

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
