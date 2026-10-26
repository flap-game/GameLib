# Changelog

## [0.7.1] - 2026-10-26
- Breadcrumbs reuse one StringBuilder and key list per thread and write int / long values without a string; same text as before.
- With Breadcrumbs From Analytics off, events, ad revenue and purchases return at once instead of going through the buffer.

## [0.5.0] - 2026-10-26
- With Breadcrumbs From Analytics off, analytics events are not buffered before init, so they do not take the room of exceptions and logs.
- A Firebase start slower than Init Timeout Seconds no longer turns crash reporting off for the session: one warning, and the buffered breadcrumbs are sent when Firebase is up.

## [0.3.0] - 2026-10-26
- First release (ported from CoreGame 0.6.7): ICrashReporter on Firebase Crashlytics, analytics breadcrumbs and custom keys, buffered start with timeout.
