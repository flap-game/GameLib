# Changelog

## [0.8.2] - 2026-10-27
- The Firebase app instance id goes to `ITrackerIds` (`TrackerIds.Firebase`) once Firebase runs.
  `IFirebaseAnalyticsBackend.GetAppInstanceIdAsync`.

## [0.7.1] - 2026-10-26
- An event that already fits Firebase limits (valid names, plain values, at most 25 parameters) is passed on without a copy and without building strings; renamed events are cached, so their rename and warning are built once. FirebaseAnalyticsSanitizer.IsValidName.
- Parameters are built straight into the array LogEvent takes (no List + ToArray).

## [0.5.0] - 2026-10-26
- A Firebase start slower than Init Timeout Seconds no longer turns analytics off for the session: one warning, and the buffered events are sent when Firebase is up.

## [0.3.0] - 2026-10-26
- First release (ported from CoreGame 0.6.7): Firebase Analytics sink with Firebase limits enforced, buffered start with timeout, ad_impression from TrackAdRevenue, optional GA4 purchase from TrackPurchase, consent mode from IConsentState.
