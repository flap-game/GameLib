# Changelog

## [0.5.0] - 2026-10-26
- A Firebase start slower than Init Timeout Seconds no longer turns analytics off for the session: one warning, and the buffered events are sent when Firebase is up.

## [0.3.0] - 2026-10-26
- First release (ported from CoreGame 0.6.7): Firebase Analytics sink with Firebase limits enforced, buffered start with timeout, ad_impression from TrackAdRevenue, optional GA4 purchase from TrackPurchase, consent mode from IConsentState.
