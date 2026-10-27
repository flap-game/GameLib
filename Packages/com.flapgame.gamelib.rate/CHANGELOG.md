# Changelog

## [0.8.0] - 2026-10-27
- New package. `IRateService`: when to ask (`NotifyTrigger`: every Nth trigger, prompt cap, hours between prompts, never
  again after rating), the store's review sheet (`RequestReviewAsync`: StoreKit on iOS, Google Play In-App Review on
  Android through JNI, the store page when it is not available) and `OpenStorePage`. The Android build gets
  `com.google.android.play:review` and its R8 keep rules automatically.
