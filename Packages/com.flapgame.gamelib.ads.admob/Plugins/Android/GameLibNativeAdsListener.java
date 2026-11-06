package com.flapgame.gamelib.ads.nativeads;

/**
 * Receives native ad events from {@link GameLibNativeAds}.
 * Implemented on the C# side by an AndroidJavaProxy
 * ("com.flapgame.gamelib.ads.nativeads.GameLibNativeAdsListener").
 *
 * event: loaded, failed, shown, show_failed, impression, clicked, closed, expanded, collapsed, paid.
 * json:  "{}" or an object with the event fields (see GameLibNativeAds).
 * Called on the Android UI thread; the C# side marshals to Unity's main thread.
 */
public interface GameLibNativeAdsListener {
    void onEvent(String key, String event, String json);
}
