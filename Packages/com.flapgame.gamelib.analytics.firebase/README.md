# GameLib Analytics - Firebase

Sends every `IAnalytics` call to Firebase Analytics. Install from **GameLib > Setup > Analytics - Firebase** (it adds
`com.flapgame.gamelib.firebase`, External Dependency Manager, Firebase App and Firebase Analytics), then put the config
files in place - see the [Firebase package README](../com.flapgame.gamelib.firebase/README.md).

## What is sent

| GameLib call | Firebase |
|---|---|
| `analytics.Track("level_complete", ("level", 3))` | the event as is, fitted to Firebase limits (below) |
| `SetUserId` / `SetUserProperty` | `SetUserId` / `SetUserProperty` (name ≤ 24, value ≤ 36 chars) |
| `TrackAdRevenue` (sent by the Ads module for every paid impression) | `ad_impression`: `ad_platform`, `ad_source`, `ad_format`, `ad_unit_name`, `value`, `currency`, plus `placement` and `precision` |
| `TrackPurchase` (sent by the IAP module) | nothing by default - see *Purchases* |
| CMP answer (`IConsentState`) | consent mode v2: `analytics_storage`, `ad_storage`, `ad_user_data`, `ad_personalization` |

Firebase limits are enforced before the SDK sees an event (it would drop it silently): names become `[A-Za-z0-9_]`,
start with a letter and lose reserved prefixes (`firebase_`, `google_`, `ga_`); events Firebase reserves
(`session_start`, `ad_click`, `in_app_purchase`, ...) get an `x_` prefix; names ≤ 40 chars, string values ≤ 100 chars,
≤ 25 parameters. Each kind of fix is warned about once.

## Startup

The sink is added at once and Firebase starts in the background, so bootstrap never waits for it. Events tracked
meanwhile are buffered (up to 200, identity first) and sent once Firebase is up. If Firebase cannot start within
**Init Timeout Seconds** (default 30) or at all, the sink turns itself off for the session with one log line.

## Purchases

Firebase already logs `in_app_purchase` with revenue on Android and iOS by itself. **Log Standard Purchase Event** also
logs GA4's `purchase` (`value`, `currency`, `transaction_id`, `items`) - turn it on only when automatic in-app purchase
collection is off, otherwise GA4 counts the revenue twice.

## Consent

With **Follow Consent** on (default), the CMP answer is sent as consent mode: where GDPR applies everything follows the
user's choice; elsewhere storage is granted and "do not sell" or a child user denies ad user data and personalization.
Nothing is sent before a CMP has answered, so set the defaults you want before that in the Android manifest / Info.plist
(see Google's consent mode docs).
