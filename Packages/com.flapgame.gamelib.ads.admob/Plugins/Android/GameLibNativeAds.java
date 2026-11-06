package com.flapgame.gamelib.ads.nativeads;

import android.animation.Animator;
import android.animation.AnimatorListenerAdapter;
import android.animation.ValueAnimator;
import android.app.Activity;
import android.app.Dialog;
import android.content.Context;
import android.content.DialogInterface;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Typeface;
import android.graphics.drawable.ColorDrawable;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.text.TextUtils;
import android.util.DisplayMetrics;
import android.util.Log;
import android.util.TypedValue;
import android.view.DisplayCutout;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.google.android.gms.ads.AdListener;
import com.google.android.gms.ads.AdLoader;
import com.google.android.gms.ads.AdRequest;
import com.google.android.gms.ads.AdValue;
import com.google.android.gms.ads.AdapterResponseInfo;
import com.google.android.gms.ads.LoadAdError;
import com.google.android.gms.ads.OnPaidEventListener;
import com.google.android.gms.ads.ResponseInfo;
import com.google.android.gms.ads.VideoOptions;
import com.google.android.gms.ads.nativead.MediaView;
import com.google.android.gms.ads.nativead.NativeAd;
import com.google.android.gms.ads.nativead.NativeAdOptions;
import com.google.android.gms.ads.nativead.NativeAdView;

import org.json.JSONObject;

import java.lang.ref.WeakReference;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;

/**
 * GameLib native ads bridge (Google Mobile Ads Android SDK, NativeAd + NativeAdView).
 *
 * One native ad / one view per opaque string key chosen by C#. Formats: bottom/top banner, collapsible
 * bottom bar, full-screen interstitial. Every view is built in code (no XML / resources / image assets).
 * Events go to {@link GameLibNativeAdsListener#onEvent(String, String, String)}:
 * loaded {"adSource"}, failed {"error"}, shown, show_failed {"error"}, impression, clicked, closed,
 * expanded, collapsed, paid {"valueMicros","currency","precision","adSource"}.
 *
 * Threading: every public method may be called from any thread; all ad and view work is posted to the
 * UI thread, and all state is touched only there. Events are emitted on the UI thread.
 *
 * The activity is held through a WeakReference (no static activity leak).
 *
 * R8 / ProGuard: the C# side reaches these classes by name through JNI, so a minified build needs
 *   -keep class com.flapgame.gamelib.ads.nativeads.** { *; }
 * (in the game's proguard-user.txt, or a consumer rule shipped by the package).
 *
 * Interstitial: shown in a full-screen Dialog (its own window above the Unity surface). A Dialog takes all
 * touches and the back key without hooking Unity's activity; back is swallowed until the close button is
 * visible, then it closes the ad.
 */
public final class GameLibNativeAds {

    private static final String TAG = "GameLibNativeAds";

    private static final int KIND_NONE = 0;
    private static final int KIND_BANNER = 1;
    private static final int KIND_COLLAPSIBLE = 2;
    private static final int KIND_INTERSTITIAL = 3;

    // "AD" badge: fixed box in the ad view's top-left corner (>= 15dp tall)
    private static final int BADGE_W_DP = 24;
    private static final int BADGE_H_DP = 16;
    private static final int BADGE_MARGIN_DP = 2;

    private static final Handler MAIN = new Handler(Looper.getMainLooper());
    private static final Map<String, State> STATES = new HashMap<String, State>();

    private static WeakReference<Activity> activityRef = new WeakReference<Activity>(null);
    private static volatile GameLibNativeAdsListener listener;

    private GameLibNativeAds() {
    }

    // ------------------------------------------------------------------------------------------------
    // Public API (any thread)
    // ------------------------------------------------------------------------------------------------

    public static void initialize(final Activity activity, final GameLibNativeAdsListener eventListener) {
        listener = eventListener;
        runOnUi(new Runnable() {
            @Override
            public void run() {
                activityRef = new WeakReference<Activity>(activity);
            }
        });
    }

    public static void load(final String key, final String adUnitId) {
        runOnUi(new Runnable() {
            @Override
            public void run() {
                doLoad(key, adUnitId);
            }
        });
    }

    public static void showBanner(final String key, final int heightDp, final boolean top, final int bg,
                                  final int text, final int cta, final int ctaText) {
        runOnUi(new Runnable() {
            @Override
            public void run() {
                Colors c = new Colors(bg, text, cta, ctaText);
                doShowOverlay(key, KIND_BANNER, heightDp > 0 ? heightDp : 100, top, false, 0, c);
            }
        });
    }

    public static void showCollapsible(final String key, final int collapsedDp, final boolean startExpanded,
                                       final int buttonDelaySeconds, final int bg, final int text,
                                       final int cta, final int ctaText) {
        runOnUi(new Runnable() {
            @Override
            public void run() {
                Colors c = new Colors(bg, text, cta, ctaText);
                doShowOverlay(key, KIND_COLLAPSIBLE, collapsedDp > 0 ? collapsedDp : 50, false, startExpanded,
                        Math.max(0, buttonDelaySeconds), c);
            }
        });
    }

    public static void showInterstitial(final String key, final int closeDelaySeconds, final int bg,
                                        final int text, final int cta, final int ctaText) {
        runOnUi(new Runnable() {
            @Override
            public void run() {
                doShowInterstitial(key, Math.max(0, closeDelaySeconds), new Colors(bg, text, cta, ctaText));
            }
        });
    }

    public static void hide(final String key) {
        runOnUi(new Runnable() {
            @Override
            public void run() {
                State s = STATES.get(key);
                if (s != null) {
                    teardownShown(s);
                }
            }
        });
    }

    public static void destroy(final String key) {
        runOnUi(new Runnable() {
            @Override
            public void run() {
                State s = STATES.remove(key);
                if (s == null) {
                    return;
                }
                teardownShown(s);
                s.loadGeneration++; // an in-flight load is destroyed on arrival
                s.loading = false;
                if (s.pendingAd != null) {
                    s.pendingAd.destroy();
                    s.pendingAd = null;
                }
            }
        });
    }

    // ------------------------------------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------------------------------------

    private static final class Colors {
        final int bg;
        final int text;
        final int secondary;
        final int cta;
        final int ctaText;

        Colors(int bg, int text, int cta, int ctaText) {
            this.bg = bg;
            this.text = text;
            int alpha = Math.round(Color.alpha(text) * 0.6f);
            this.secondary = (alpha << 24) | (text & 0x00FFFFFF);
            this.cta = cta;
            this.ctaText = ctaText;
        }
    }

    private static final class State {
        final String key;
        NativeAd pendingAd;          // loaded, not shown yet
        boolean loading;
        int loadGeneration;

        NativeAd shownAd;            // spent once shown; destroyed on hide / close / replace
        int kind = KIND_NONE;
        int viewGeneration;          // bumps on every teardown; delayed runnables check it
        Views views;
        View.OnLayoutChangeListener insetsListener;
        View insetsHost;
        Dialog dialog;
        boolean closable;
        boolean closedSent;

        State(String key) {
            this.key = key;
        }
    }

    /** View references of one shown ad. */
    private static final class Views {
        FrameLayout wrapper;         // overlay container in android.R.id.content (banner / collapsible)
        NativeAdView adView;
        LinearLayout content;
        FrameLayout mediaBox;
        MediaView media;
        LinearLayout row;
        ImageView icon;
        LinearLayout textColumn;
        TextView cornerBadge;        // banner / collapsible: "AD" in the ad view's top-left corner
        TextView inlineBadge;        // one-line bars (< 40dp): "AD" right after the headline instead
        TextView headline;
        TextView body;
        LinearLayout metaRow;
        TextView rating;
        TextView advertiser;
        TextView store;
        TextView price;
        TextView[] separators;
        TextView cta;
        FrameLayout strip;           // collapsible: holder of the collapse button, over the ad's top-right (below AdChoices)
        View collapseButton;
        TextView countdown;          // interstitial
        View closeButton;            // interstitial
        int collapsedDp;
        int collapsedPx;
        int expandedPx;
        boolean landscape;           // collapsible: screen wider than tall when built (orientation is fixed per game)
        int mediaWidthPx;            // collapsible, landscape: media column width while expanded
        boolean expanded;
        boolean top;
    }

    // ------------------------------------------------------------------------------------------------
    // Load
    // ------------------------------------------------------------------------------------------------

    private static void doLoad(final String key, String adUnitId) {
        if (key == null) {
            return;
        }
        State s = STATES.get(key);
        if (s == null) {
            s = new State(key);
            STATES.put(key, s);
        }
        if (s.pendingAd != null) {
            emit(key, "loaded", json("adSource", adSourceOf(s.pendingAd)));
            return;
        }
        if (s.loading) {
            return; // the in-flight load reports loaded / failed
        }
        Activity activity = liveActivity();
        if (activity == null) {
            emit(key, "failed", json("error", "no_activity"));
            return;
        }
        if (TextUtils.isEmpty(adUnitId)) {
            emit(key, "failed", json("error", "no_ad_unit_id"));
            return;
        }

        final State state = s;
        final int generation = ++state.loadGeneration;
        state.loading = true;

        VideoOptions videoOptions = new VideoOptions.Builder().setStartMuted(true).build();
        NativeAdOptions options = new NativeAdOptions.Builder()
                .setMediaAspectRatio(NativeAdOptions.NATIVE_MEDIA_ASPECT_RATIO_ANY)
                .setAdChoicesPlacement(NativeAdOptions.ADCHOICES_TOP_RIGHT)
                .setVideoOptions(videoOptions)
                .build();

        try {
            AdLoader loader = new AdLoader.Builder(activity, adUnitId)
                    .forNativeAd(new NativeAd.OnNativeAdLoadedListener() {
                        @Override
                        public void onNativeAdLoaded(final NativeAd ad) {
                            runOnUi(new Runnable() {
                                @Override
                                public void run() {
                                    onAdArrived(state, generation, ad);
                                }
                            });
                        }
                    })
                    .withAdListener(new AdListener() {
                        @Override
                        public void onAdFailedToLoad(final LoadAdError error) {
                            runOnUi(new Runnable() {
                                @Override
                                public void run() {
                                    if (!isCurrentLoad(state, generation)) {
                                        return;
                                    }
                                    state.loading = false;
                                    String message = error == null ? "unknown"
                                            : error.getCode() + " " + error.getMessage();
                                    emit(state.key, "failed", json("error", message));
                                }
                            });
                        }

                        @Override
                        public void onAdImpression() {
                            emitOnUi(state.key, "impression", "{}");
                        }

                        @Override
                        public void onAdClicked() {
                            emitOnUi(state.key, "clicked", "{}");
                        }
                    })
                    .withNativeAdOptions(options)
                    .build();
            loader.loadAd(new AdRequest.Builder().build());
        } catch (Throwable t) {
            state.loading = false;
            Log.w(TAG, "load failed", t);
            emit(key, "failed", json("error", "exception: " + t.getMessage()));
        }
    }

    private static boolean isCurrentLoad(State state, int generation) {
        return STATES.get(state.key) == state && state.loadGeneration == generation;
    }

    private static void onAdArrived(final State state, int generation, final NativeAd ad) {
        if (!isCurrentLoad(state, generation)) {
            ad.destroy(); // key destroyed while loading
            return;
        }
        state.loading = false;
        if (state.pendingAd != null) {
            state.pendingAd.destroy();
        }
        state.pendingAd = ad;
        ad.setOnPaidEventListener(new OnPaidEventListener() {
            @Override
            public void onPaidEvent(AdValue value) {
                String payload = "{}";
                try {
                    JSONObject o = new JSONObject();
                    o.put("valueMicros", value.getValueMicros());
                    o.put("currency", value.getCurrencyCode() == null ? "" : value.getCurrencyCode());
                    o.put("precision", value.getPrecisionType());
                    o.put("adSource", adSourceOf(ad));
                    payload = o.toString();
                } catch (Throwable ignored) {
                }
                emitOnUi(state.key, "paid", payload);
            }
        });
        emit(state.key, "loaded", json("adSource", adSourceOf(ad)));
    }

    private static String adSourceOf(NativeAd ad) {
        try {
            ResponseInfo info = ad == null ? null : ad.getResponseInfo();
            AdapterResponseInfo loaded = info == null ? null : info.getLoadedAdapterResponseInfo();
            String name = loaded == null ? null : loaded.getAdSourceName();
            return name == null ? "" : name;
        } catch (Throwable t) {
            return "";
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Show: banner / collapsible (overlay in android.R.id.content)
    // ------------------------------------------------------------------------------------------------

    /**
     * Takes the ad to show: the pending one (the previous shown ad is torn down), or - when a show is
     * repeated while the same ad is still on screen - the shown one, whose view is rebuilt in place.
     * Returns null after emitting show_failed.
     */
    private static NativeAd takeAdForShow(State s, String key) {
        if (s == null || (s.pendingAd == null && s.shownAd == null)) {
            emit(key, "show_failed", json("error", "no_ad_loaded"));
            return null;
        }
        NativeAd ad;
        if (s.pendingAd != null) {
            teardownShown(s);
            ad = s.pendingAd;
            s.pendingAd = null;
        } else {
            ad = s.shownAd;
            s.shownAd = null; // keep the ad alive across the view rebuild
            teardownShown(s);
        }
        return ad;
    }

    private static void doShowOverlay(String key, int kind, int heightDp, boolean top, boolean startExpanded,
                                      int buttonDelaySeconds, Colors colors) {
        State s = STATES.get(key);
        Activity activity = liveActivity();
        if (activity == null) {
            emit(key, "show_failed", json("error", "no_activity"));
            return;
        }
        ViewGroup content = (ViewGroup) activity.findViewById(android.R.id.content);
        if (content == null) {
            emit(key, "show_failed", json("error", "no_content_view"));
            return;
        }
        NativeAd ad = takeAdForShow(s, key);
        if (ad == null) {
            return;
        }

        Views v;
        try {
            if (kind == KIND_BANNER) {
                v = buildBanner(activity, heightDp, top, colors);
            } else {
                v = buildCollapsible(activity, content, heightDp, startExpanded, colors);
            }
            bindAssets(v, ad);
            v.adView.setNativeAd(ad);

            FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT,
                    top ? Gravity.TOP : Gravity.BOTTOM);
            content.addView(v.wrapper, lp);
        } catch (Throwable t) {
            Log.w(TAG, "show failed", t);
            ad.destroy();
            emit(key, "show_failed", json("error", "exception: " + t.getMessage()));
            return;
        }

        s.shownAd = ad;
        s.kind = kind;
        s.views = v;
        s.closedSent = false;
        attachSafeAreaTracking(s, content);
        emit(key, "shown", "{}");

        if (kind == KIND_COLLAPSIBLE) {
            if (v.expanded) {
                emit(key, "expanded", "{}");
                scheduleCollapseButton(s, buttonDelaySeconds);
            } else {
                emit(key, "collapsed", "{}");
            }
        }
    }

    private static Views buildBanner(Activity activity, int heightDp, boolean top, Colors c) {
        Views v = new Views();
        v.top = top;
        v.wrapper = new FrameLayout(activity);
        v.adView = new NativeAdView(activity);
        v.adView.setBackgroundColor(c.bg);
        v.content = new LinearLayout(activity);
        v.content.setOrientation(LinearLayout.VERTICAL);
        v.content.setGravity(Gravity.BOTTOM);
        v.content.setClickable(true); // swallow taps on the bar background so they do not reach the game
        v.adView.addView(v.content, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        buildRow(activity, v, c);
        v.content.addView(v.row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0));
        v.cornerBadge = createAdBadge(activity);
        v.adView.addView(v.cornerBadge, badgeParams(activity));

        int heightPx = dp(activity, heightDp);
        v.collapsedDp = heightDp; // banner: the bar height
        v.collapsedPx = heightPx;
        layoutRow(activity, v, heightDp, heightPx, 1, true, false);
        v.wrapper.addView(v.adView, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, heightPx));
        registerAssetViews(v);
        return v;
    }

    /**
     * Gap between the collapse button's circle and the ad's top edge, and its right edge (the same on both sides). The
     * circle sits in the top-right corner of its 48dp touch target, which reaches left and down only, so neither the
     * circle nor the touch target covers the AdChoices icon in the ad's top-right corner.
     */
    private static final int COLLAPSE_BUTTON_TOP_DP = 20;
    /** AdMob's minimum media view size for video, both sides. */
    private static final int MIN_MEDIA_DP = 120;

    private static Views buildCollapsible(Activity activity, ViewGroup content, int collapsedDp,
                                          boolean startExpanded, Colors c) {
        Views v = new Views();
        v.collapsedDp = collapsedDp;
        v.collapsedPx = dp(activity, collapsedDp);
        int screenH = content.getHeight() > 0 ? content.getHeight() : screenHeightPx(activity);
        int screenW = content.getWidth() > 0 ? content.getWidth() : screenWidthPx(activity);
        v.landscape = screenW > screenH;
        // the media must stay >= 120x120dp (AdMob's video minimum): in landscape it is the full ad height
        v.expandedPx = Math.max(v.collapsedPx, Math.max(Math.round(screenH * 0.45f),
                v.landscape ? dp(activity, MIN_MEDIA_DP) : 0));
        // landscape: media column on the left (16:9 of the ad height, at most half the width), row on the right
        v.mediaWidthPx = Math.max(dp(activity, MIN_MEDIA_DP), Math.min(Math.round(v.expandedPx * 16f / 9f), screenW / 2));
        v.expanded = startExpanded;

        v.wrapper = new FrameLayout(activity);
        LinearLayout column = new LinearLayout(activity);
        column.setOrientation(LinearLayout.VERTICAL);
        v.wrapper.addView(column, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        // The collapse button sits inside the ad, over its top-right corner but below the AdChoices icon (which
        // owns the corner itself and must not be covered): a 28dp visual circle inside a 48dp touch target.
        v.strip = new FrameLayout(activity);
        v.collapseButton = new RoundIconButton(activity, RoundIconButton.CHEVRON_DOWN, 0xAA000000, Color.WHITE,
                dp(activity, 28));
        v.collapseButton.setVisibility(View.GONE);
        int touchPx = dp(activity, 48);
        ((RoundIconButton) v.collapseButton).cornerAnchored = true;
        v.strip.addView(v.collapseButton, new FrameLayout.LayoutParams(touchPx, touchPx, Gravity.CENTER));

        v.adView = new NativeAdView(activity);
        v.adView.setBackgroundColor(c.bg);
        v.content = new LinearLayout(activity);
        v.content.setOrientation(LinearLayout.VERTICAL);
        v.content.setGravity(Gravity.BOTTOM);
        v.content.setClickable(true);
        v.adView.addView(v.content, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        v.mediaBox = new FrameLayout(activity);
        v.mediaBox.setBackgroundColor(0xFF000000);
        v.media = new MediaView(activity);
        v.media.setImageScaleType(ImageView.ScaleType.FIT_CENTER);
        // the MediaView is attached to mediaBox only while expanded (see setMediaAttached)
        v.content.addView(v.mediaBox, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        buildRow(activity, v, c);
        v.content.addView(v.row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0));
        v.cornerBadge = createAdBadge(activity);
        v.adView.addView(v.cornerBadge, badgeParams(activity));

        column.addView(v.adView, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,
                v.expanded ? v.expandedPx : v.collapsedPx));
        FrameLayout.LayoutParams slp = new FrameLayout.LayoutParams(touchPx, touchPx, Gravity.TOP | Gravity.RIGHT);
        slp.topMargin = dp(activity, COLLAPSE_BUTTON_TOP_DP);
        slp.rightMargin = dp(activity, COLLAPSE_BUTTON_TOP_DP);
        v.wrapper.addView(v.strip, slp); // over the ad view (added after the column)
        applyCollapsibleState(activity, v);
        registerAssetViews(v);
        return v;
    }

    /** Lays out the collapsible for v.expanded (no animation); sets the ad view height. */
    private static void applyCollapsibleState(Context ctx, Views v) {
        if (v.expanded) {
            int rowDp = Math.max(v.collapsedDp, 76);
            int rowPx = Math.min(dp(ctx, rowDp), v.expandedPx);
            if (v.landscape) {
                // the row fills the right column below the collapse button's touch target
                int belowButtonPx = v.expandedPx - dp(ctx, COLLAPSE_BUTTON_TOP_DP + 48);
                rowPx = Math.max(rowPx, Math.min(belowButtonPx, dp(ctx, 120)));
                rowDp = Math.round(rowPx / density(ctx));
            }
            setMediaAttached(v, true);
            v.mediaBox.setVisibility(View.VISIBLE);
            v.strip.setVisibility(View.VISIBLE);
            arrangeCollapsible(v, true);
            layoutRow(ctx, v, rowDp, rowPx, 2, true, true);
            setHeight(v.adView, v.expandedPx);
        } else {
            v.mediaBox.setVisibility(View.GONE);
            setMediaAttached(v, false);
            v.strip.setVisibility(View.GONE);
            v.collapseButton.setVisibility(View.GONE);
            arrangeCollapsible(v, false);
            layoutRow(ctx, v, v.collapsedDp, v.collapsedPx, 1, false, false);
            setHeight(v.adView, v.collapsedPx);
        }
    }

    /**
     * Collapsible content: media above the row (portrait, and every collapsed bar) or, expanded in landscape,
     * media column left and the row right (aligned to the bottom, clear of the collapse button).
     */
    private static void arrangeCollapsible(Views v, boolean expanded) {
        boolean side = expanded && v.landscape;
        v.content.setOrientation(side ? LinearLayout.HORIZONTAL : LinearLayout.VERTICAL);
        v.mediaBox.setLayoutParams(side
                ? new LinearLayout.LayoutParams(v.mediaWidthPx, ViewGroup.LayoutParams.MATCH_PARENT)
                : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        v.row.setLayoutParams(side
                ? new LinearLayout.LayoutParams(0, v.row.getLayoutParams().height, 1f)
                : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, v.row.getLayoutParams().height));
    }

    /**
     * Collapsible: the MediaView is in the hierarchy only while expanded (~full width x 45% of the
     * screen, well above the 120x120dp video minimum). Collapsed, it is detached so the collapsed bar has
     * no media view at all; registerAssetViews registers it with the NativeAdView only when expanded.
     */
    private static void setMediaAttached(Views v, boolean attached) {
        if (v.media == null || v.mediaBox == null) {
            return;
        }
        boolean isAttached = v.media.getParent() == v.mediaBox;
        if (attached && !isAttached) {
            v.mediaBox.addView(v.media, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT, Gravity.CENTER));
        } else if (!attached && isAttached) {
            v.mediaBox.removeView(v.media);
        }
    }

    private static void scheduleCollapseButton(final State s, int delaySeconds) {
        final int viewGeneration = s.viewGeneration;
        final Views v = s.views;
        v.collapseButton.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View view) {
                collapse(s, viewGeneration);
            }
        });
        Runnable reveal = new Runnable() {
            @Override
            public void run() {
                if (s.viewGeneration == viewGeneration && s.views == v && v.expanded) {
                    v.collapseButton.setVisibility(View.VISIBLE);
                }
            }
        };
        if (delaySeconds <= 0) {
            reveal.run();
        } else {
            MAIN.postDelayed(reveal, delaySeconds * 1000L);
        }
    }

    private static void collapse(final State s, final int viewGeneration) {
        final Views v = s.views;
        if (v == null || s.viewGeneration != viewGeneration || !v.expanded) {
            return;
        }
        Context ctx = v.adView.getContext();
        v.expanded = false;
        v.collapseButton.setVisibility(View.GONE);
        v.collapseButton.setOnClickListener(null);
        v.strip.setVisibility(View.GONE);
        v.mediaBox.setVisibility(View.GONE);
        // collapsed: no media view registered nor in the hierarchy; re-bind so the SDK re-evaluates
        v.adView.setMediaView(null);
        setMediaAttached(v, false);
        if (s.shownAd != null) {
            v.adView.setNativeAd(s.shownAd);
        }
        arrangeCollapsible(v, false);
        layoutRow(ctx, v, v.collapsedDp, v.collapsedPx, 1, false, false);

        int from = v.adView.getHeight() > 0 ? v.adView.getHeight() : v.expandedPx;
        ValueAnimator animator = ValueAnimator.ofInt(from, v.collapsedPx);
        animator.setDuration(250);
        animator.addUpdateListener(new ValueAnimator.AnimatorUpdateListener() {
            @Override
            public void onAnimationUpdate(ValueAnimator animation) {
                if (s.viewGeneration == viewGeneration) {
                    setHeight(v.adView, (Integer) animation.getAnimatedValue());
                }
            }
        });
        animator.addListener(new AnimatorListenerAdapter() {
            @Override
            public void onAnimationEnd(Animator animation) {
                if (s.viewGeneration == viewGeneration) {
                    setHeight(v.adView, v.collapsedPx);
                }
            }
        });
        animator.start();
        emit(s.key, "collapsed", "{}");
    }

    // ------------------------------------------------------------------------------------------------
    // Shared row: icon | [headline, body, meta] | CTA
    // ------------------------------------------------------------------------------------------------

    private static void buildRow(Context ctx, Views v, Colors c) {
        v.row = new LinearLayout(ctx);
        v.row.setOrientation(LinearLayout.HORIZONTAL);
        v.row.setGravity(Gravity.CENTER_VERTICAL);
        int pad = dp(ctx, 8);
        v.row.setPadding(pad, dp(ctx, 3), pad, dp(ctx, 3));

        v.icon = new ImageView(ctx);
        v.icon.setScaleType(ImageView.ScaleType.FIT_CENTER);
        GradientDrawable iconShape = new GradientDrawable();
        iconShape.setColor(0x00000000);
        iconShape.setCornerRadius(dp(ctx, 6));
        v.icon.setBackground(iconShape);
        v.icon.setClipToOutline(true);
        v.row.addView(v.icon, new LinearLayout.LayoutParams(0, 0));

        v.textColumn = new LinearLayout(ctx);
        v.textColumn.setOrientation(LinearLayout.VERTICAL);
        v.textColumn.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout.LayoutParams colLp = new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        colLp.leftMargin = dp(ctx, 8);
        colLp.rightMargin = dp(ctx, 8);
        v.row.addView(v.textColumn, colLp);

        v.headline = text(ctx, c.text, 13, Typeface.DEFAULT_BOLD, 1);
        v.textColumn.addView(v.headline, wrapLp());
        v.inlineBadge = createAdBadge(ctx);
        v.inlineBadge.setVisibility(View.GONE);
        v.textColumn.addView(v.inlineBadge, new LinearLayout.LayoutParams(dp(ctx, BADGE_W_DP), dp(ctx, BADGE_H_DP)));
        v.body = text(ctx, c.text, 11, Typeface.DEFAULT, 1);
        v.textColumn.addView(v.body, wrapLp());

        v.metaRow = new LinearLayout(ctx);
        v.metaRow.setOrientation(LinearLayout.HORIZONTAL);
        v.metaRow.setGravity(Gravity.CENTER_VERTICAL);
        v.rating = text(ctx, c.secondary, 10, Typeface.DEFAULT, 1);
        v.advertiser = text(ctx, c.secondary, 10, Typeface.DEFAULT, 1);
        v.store = text(ctx, c.secondary, 10, Typeface.DEFAULT, 1);
        v.price = text(ctx, c.secondary, 10, Typeface.DEFAULT, 1);
        v.separators = new TextView[3];
        TextView[] items = new TextView[] {v.rating, v.advertiser, v.store, v.price};
        for (int i = 0; i < items.length; i++) {
            if (i > 0) {
                TextView sep = text(ctx, c.secondary, 10, Typeface.DEFAULT, 1);
                sep.setText("  •  ");
                v.separators[i - 1] = sep;
                v.metaRow.addView(sep, wrapLp());
            }
            v.metaRow.addView(items[i], wrapLp());
        }
        v.textColumn.addView(v.metaRow, wrapLp());

        v.cta = text(ctx, c.ctaText, 13, Typeface.create("sans-serif-medium", Typeface.NORMAL), 1);
        v.cta.setGravity(Gravity.CENTER);
        v.cta.setPadding(dp(ctx, 4), 0, dp(ctx, 4), 0);
        v.cta.setBackground(rounded(c.cta, dp(ctx, 6), 0, 0));
        v.row.addView(v.cta, new LinearLayout.LayoutParams(0, 0));
    }

    /**
     * Sizes the row for a bar of rowDp: icon ~70% of the bar (24..72dp), CTA width ~1.2x (52..96dp).
     * Bars <= 60dp: icon = bar minus 8dp top/bottom, headline 12sp, body 10sp one line, CTA >= 32dp tall,
     * meta line dropped when it does not fit. Meta shows the rating always; advertiser + store when
     * showAdvertiserStore; price when showPrice (only the parts present). A row at the top of the ad view
     * starts right of the "AD" badge.
     */
    private static void layoutRow(Context ctx, Views v, int rowDp, int rowPx, int bodyLines,
                                  boolean showAdvertiserStore, boolean showPrice) {
        setHeight(v.row, rowPx);
        boolean small = rowDp <= 60;
        // Bars under 40dp (a landscape smart banner) are one line: headline, AD, body, meta, then the button.
        boolean oneLine = rowDp < 40 && v.inlineBadge != null;
        arrangeTextLine(ctx, v, oneLine);
        // The "AD" badge sits in the ad view's top-left corner. When the row is the top of the ad view
        // (banner, collapsed bar) it starts right of the badge so no asset is covered.
        boolean rowAtTop = !oneLine && (v.mediaBox == null || v.mediaBox.getVisibility() != View.VISIBLE);
        int padV = dp(ctx, 3);
        int padH = dp(ctx, 8);
        v.row.setPadding(rowAtTop ? dp(ctx, BADGE_MARGIN_DP + BADGE_W_DP + 4) : padH, padV, padH, padV);
        int innerPx = Math.max(0, rowPx - 2 * padV);

        // small bars: icon = bar height minus 8dp top + bottom (4dp under 40dp, e.g. a 32dp landscape smart
        // banner); otherwise ~70% of the bar (24..72dp)
        int iconPx = small ? Math.max(dp(ctx, 16), rowPx - 2 * dp(ctx, rowDp < 40 ? 4 : 8))
                : dp(ctx, clamp(Math.round(rowDp * 0.7f), 24, 72));
        LinearLayout.LayoutParams ilp = (LinearLayout.LayoutParams) v.icon.getLayoutParams();
        ilp.width = iconPx;
        ilp.height = iconPx;
        v.icon.setLayoutParams(ilp);

        LinearLayout.LayoutParams clp = (LinearLayout.LayoutParams) v.cta.getLayoutParams();
        // tiny bars (< 40dp) get a wider button: the label is the only text room left besides the headline
        clp.width = dp(ctx, rowDp < 40 ? 80 : clamp(Math.round(rowDp * 1.2f), 52, 96));
        clp.height = small ? Math.min(dp(ctx, Math.max(32, Math.round(rowDp * 0.55f))), innerPx)
                : dp(ctx, clamp(Math.round(rowDp * 0.55f), 24, 44));
        v.cta.setLayoutParams(clp);

        v.headline.setTextSize(TypedValue.COMPLEX_UNIT_SP, small ? 12 : 13);
        v.body.setTextSize(TypedValue.COMPLEX_UNIT_SP, small ? 10 : 11);
        float meta = 10;
        v.rating.setTextSize(TypedValue.COMPLEX_UNIT_SP, meta);
        v.advertiser.setTextSize(TypedValue.COMPLEX_UNIT_SP, meta);
        v.store.setTextSize(TypedValue.COMPLEX_UNIT_SP, meta);
        v.price.setTextSize(TypedValue.COMPLEX_UNIT_SP, meta);
        for (TextView sep : v.separators) {
            sep.setTextSize(TypedValue.COMPLEX_UNIT_SP, meta);
        }
        v.cta.setTextSize(TypedValue.COMPLEX_UNIT_SP, small ? 11 : 13);
        v.body.setMaxLines(small ? 1 : bodyLines);

        // asset visibility: present AND allowed in this layout
        setShown(v.advertiser, showAdvertiserStore && hasText(v.advertiser));
        setShown(v.store, showAdvertiserStore && hasText(v.store));
        setShown(v.price, showPrice && hasText(v.price));
        setShown(v.rating, hasText(v.rating));
        setShown(v.body, hasText(v.body));
        updateSeparators(v);

        if (oneLine) {
            setShown(v.price, showPrice && hasText(v.price)); // as on the other bars; fitOneLine drops what does not fit
            updateSeparators(v);
            v.textColumn.post(new Runnable() {
                @Override
                public void run() {
                    fitOneLine(v);
                }
            });
            return;
        }
        if (small && v.metaRow.getVisibility() == View.VISIBLE) {
            // small bar: drop the meta line (rating / store) when the three lines do not fit
            int need = v.rating.getLineHeight() + dp(ctx, 2);
            if (v.headline.getVisibility() == View.VISIBLE) {
                need += v.headline.getLineHeight();
            }
            if (v.body.getVisibility() == View.VISIBLE) {
                need += v.body.getLineHeight();
            }
            if (need > innerPx) {
                v.metaRow.setVisibility(View.GONE);
            }
        }
        if (small && v.body.getVisibility() == View.VISIBLE && v.headline.getVisibility() == View.VISIBLE
                && v.headline.getLineHeight() + v.body.getLineHeight() > innerPx) {
            // tiny bar (e.g. a 32dp smart banner in landscape): the headline alone
            v.body.setVisibility(View.GONE);
        }
    }

    /** Text column of the row: stacked lines, or (one-line bars) headline | AD | body | meta side by side. */
    private static void arrangeTextLine(Context ctx, Views v, boolean oneLine) {
        if (v.inlineBadge == null) {
            return;
        }
        v.textColumn.setOrientation(oneLine ? LinearLayout.HORIZONTAL : LinearLayout.VERTICAL);
        v.inlineBadge.setVisibility(oneLine ? View.VISIBLE : View.GONE);
        if (v.cornerBadge != null) {
            v.cornerBadge.setVisibility(oneLine ? View.GONE : View.VISIBLE);
        }
        int gap = dp(ctx, 8);
        LinearLayout.LayoutParams badge = (LinearLayout.LayoutParams) v.inlineBadge.getLayoutParams();
        badge.leftMargin = dp(ctx, 6);
        v.inlineBadge.setLayoutParams(badge);
        LinearLayout.LayoutParams body = oneLine
                ? new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                : wrapLp();
        body.leftMargin = oneLine ? gap : 0;
        v.body.setLayoutParams(body);
        LinearLayout.LayoutParams meta = wrapLp();
        meta.leftMargin = oneLine ? gap : 0;
        v.metaRow.setLayoutParams(meta);
    }

    /**
     * One-line bar, after layout: drops what does not fit, lowest priority first - price, store, rating, advertiser,
     * then the body (which otherwise takes the room left and ellipsizes). Headline, AD and the button always stay.
     */
    private static void fitOneLine(Views v) {
        int width = v.textColumn.getWidth();
        if (width <= 0 || v.textColumn.getOrientation() != LinearLayout.HORIZONTAL) {
            return;
        }
        Context ctx = v.textColumn.getContext();
        int fixed = wrapWidth(v.headline) + wrapWidth(v.inlineBadge) + dp(ctx, 6);
        int minBody = v.body.getVisibility() == View.VISIBLE ? dp(ctx, 60) + dp(ctx, 8) : 0;
        TextView[] dropOrder = new TextView[] {v.price, v.store, v.rating, v.advertiser};
        int i = 0;
        while (fixed + minBody + metaWidth(v, ctx) > width && i < dropOrder.length) {
            setShown(dropOrder[i++], false);
            updateSeparators(v);
        }
        boolean anyMeta = v.rating.getVisibility() == View.VISIBLE || v.advertiser.getVisibility() == View.VISIBLE
                || v.store.getVisibility() == View.VISIBLE || v.price.getVisibility() == View.VISIBLE;
        v.metaRow.setVisibility(anyMeta ? View.VISIBLE : View.GONE);
        if (fixed + minBody + metaWidth(v, ctx) > width) {
            v.body.setVisibility(View.GONE);
        }
        v.textColumn.requestLayout();
    }

    private static int metaWidth(Views v, Context ctx) {
        if (v.metaRow.getVisibility() != View.VISIBLE) {
            return 0;
        }
        return wrapWidth(v.metaRow) + dp(ctx, 8);
    }

    private static int wrapWidth(View view) {
        if (view.getVisibility() != View.VISIBLE) {
            return 0;
        }
        view.measure(View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED),
                View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED));
        return view.getMeasuredWidth();
    }

    private static void updateSeparators(Views v) {
        TextView[] items = new TextView[] {v.rating, v.advertiser, v.store, v.price};
        boolean anyBefore = items[0].getVisibility() == View.VISIBLE;
        for (int i = 1; i < items.length; i++) {
            boolean visible = items[i].getVisibility() == View.VISIBLE;
            v.separators[i - 1].setVisibility(visible && anyBefore ? View.VISIBLE : View.GONE);
            anyBefore |= visible;
        }
        v.metaRow.setVisibility(anyBefore ? View.VISIBLE : View.GONE);
    }

    // ------------------------------------------------------------------------------------------------
    // Show: interstitial (full-screen Dialog)
    // ------------------------------------------------------------------------------------------------

    private static void doShowInterstitial(final String key, int closeDelaySeconds, Colors c) {
        final State s = STATES.get(key);
        final Activity activity = liveActivity();
        if (activity == null) {
            emit(key, "show_failed", json("error", "no_activity"));
            return;
        }
        if (s != null && s.kind == KIND_INTERSTITIAL && s.dialog != null && s.dialog.isShowing()
                && s.pendingAd == null) {
            emit(key, "shown", "{}"); // same ad already on screen
            return;
        }
        NativeAd ad = takeAdForShow(s, key);
        if (ad == null) {
            return;
        }

        final Views v = new Views();
        final Dialog dialog;
        try {
            dialog = new Dialog(activity, android.R.style.Theme_Black_NoTitleBar_Fullscreen) {
                @Override
                public void onBackPressed() {
                    if (s.closable) {
                        closeInterstitial(s);
                    }
                    // else: swallowed until the close button is visible
                }
            };
            dialog.requestWindowFeature(Window.FEATURE_NO_TITLE);
            dialog.setCancelable(false);
            dialog.setCanceledOnTouchOutside(false);

            View root = buildInterstitial(activity, v, c);
            bindAssets(v, ad);
            v.adView.setNativeAd(ad);
            dialog.setContentView(root);

            Window w = dialog.getWindow();
            if (w != null) {
                w.setBackgroundDrawable(new ColorDrawable(0xFF000000));
                w.setLayout(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT);
                w.addFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN);
                if (Build.VERSION.SDK_INT >= 28) {
                    WindowManager.LayoutParams attrs = w.getAttributes();
                    attrs.layoutInDisplayCutoutMode =
                            WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES;
                    w.setAttributes(attrs);
                }
                makeImmersive(w);
            }
            dialog.setOnShowListener(new DialogInterface.OnShowListener() {
                @Override
                public void onShow(DialogInterface d) {
                    Window sw = ((Dialog) d).getWindow();
                    if (sw != null) {
                        makeImmersive(sw); // again once attached: the window now has its insets controller
                    }
                }
            });
            dialog.setOnDismissListener(new DialogInterface.OnDismissListener() {
                @Override
                public void onDismiss(DialogInterface d) {
                    // dismissed by the system (activity gone) while it was the shown dialog -> closed
                    if (s.dialog == d) {
                        s.dialog = null;
                        closeInterstitial(s);
                    }
                }
            });
        } catch (Throwable t) {
            Log.w(TAG, "interstitial build failed", t);
            ad.destroy();
            emit(key, "show_failed", json("error", "exception: " + t.getMessage()));
            return;
        }

        s.shownAd = ad;
        s.kind = KIND_INTERSTITIAL;
        s.views = v;
        s.dialog = dialog;
        s.closable = false;
        s.closedSent = false;
        try {
            dialog.show();
        } catch (Throwable t) {
            Log.w(TAG, "interstitial show failed", t);
            s.dialog = null;
            teardownShown(s);
            emit(key, "show_failed", json("error", "exception: " + t.getMessage()));
            return;
        }
        emit(key, "shown", "{}");
        startCountdown(s, closeDelaySeconds);
    }

    private static View buildInterstitial(Context ctx, final Views v, Colors c) {
        final LinearLayout root = new LinearLayout(ctx);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(0xFF000000);

        // Top bar (black) with the countdown / close button at the right. Kept above the ad so it never
        // overlaps the AdChoices icon in the ad's top-right corner.
        final FrameLayout topBar = new FrameLayout(ctx);
        v.countdown = new TextView(ctx);
        v.countdown.setTextColor(Color.WHITE);
        v.countdown.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        v.countdown.setGravity(Gravity.CENTER);
        v.countdown.setPadding(dp(ctx, 12), dp(ctx, 6), dp(ctx, 12), dp(ctx, 6));
        v.countdown.setBackground(rounded(0xAA000000, dp(ctx, 16), dp(ctx, 1), 0x66FFFFFF));
        FrameLayout.LayoutParams cdLp = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT,
                ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.RIGHT | Gravity.CENTER_VERTICAL);
        cdLp.rightMargin = dp(ctx, 12);
        topBar.addView(v.countdown, cdLp);

        v.closeButton = new RoundIconButton(ctx, RoundIconButton.CROSS, 0xAA000000, Color.WHITE);
        v.closeButton.setVisibility(View.GONE);
        int closePx = dp(ctx, 38);
        FrameLayout.LayoutParams cbLp = new FrameLayout.LayoutParams(closePx, closePx,
                Gravity.RIGHT | Gravity.CENTER_VERTICAL);
        cbLp.rightMargin = dp(ctx, 12);
        topBar.addView(v.closeButton, cbLp);
        final int barPx = dp(ctx, 54);
        root.addView(topBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, barPx));

        v.adView = new NativeAdView(ctx);
        v.adView.setBackgroundColor(0xFF000000);
        root.addView(v.adView, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        v.content = new LinearLayout(ctx);
        v.content.setOrientation(LinearLayout.VERTICAL);
        v.content.setClickable(true);
        v.adView.addView(v.content, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        v.mediaBox = new FrameLayout(ctx);
        v.mediaBox.setBackgroundColor(0xFF000000);
        v.media = new MediaView(ctx);
        v.media.setImageScaleType(ImageView.ScaleType.FIT_CENTER);
        v.mediaBox.addView(v.media, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT, Gravity.CENTER));
        v.content.addView(v.mediaBox, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        final LinearLayout panel = new LinearLayout(ctx);
        panel.setOrientation(LinearLayout.VERTICAL);
        panel.setBackgroundColor(c.bg);
        final int pad = dp(ctx, 16);
        panel.setPadding(pad, pad, pad, pad);
        v.content.addView(panel, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT));

        LinearLayout header = new LinearLayout(ctx);
        header.setOrientation(LinearLayout.HORIZONTAL);
        header.setGravity(Gravity.CENTER_VERTICAL);
        v.icon = new ImageView(ctx);
        v.icon.setScaleType(ImageView.ScaleType.FIT_CENTER);
        GradientDrawable iconShape = new GradientDrawable();
        iconShape.setColor(0x00000000);
        iconShape.setCornerRadius(dp(ctx, 10));
        v.icon.setBackground(iconShape);
        v.icon.setClipToOutline(true);
        int iconPx = dp(ctx, 56);
        header.addView(v.icon, new LinearLayout.LayoutParams(iconPx, iconPx));
        v.textColumn = new LinearLayout(ctx);
        v.textColumn.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams colLp = new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        colLp.leftMargin = dp(ctx, 12);
        header.addView(v.textColumn, colLp);
        v.headline = text(ctx, c.text, 18, Typeface.DEFAULT_BOLD, 2);
        v.textColumn.addView(v.headline, wrapLp());
        v.advertiser = text(ctx, c.secondary, 13, Typeface.DEFAULT, 1);
        v.textColumn.addView(v.advertiser, wrapLp());
        panel.addView(header, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT));

        v.body = text(ctx, c.text, 14, Typeface.DEFAULT, 3);
        LinearLayout.LayoutParams bodyLp = wrapLp();
        bodyLp.topMargin = dp(ctx, 10);
        panel.addView(v.body, bodyLp);

        v.metaRow = new LinearLayout(ctx);
        v.metaRow.setOrientation(LinearLayout.HORIZONTAL);
        v.rating = chip(ctx, c);
        v.store = chip(ctx, c);
        v.price = chip(ctx, c);
        v.metaRow.addView(v.rating, chipLp(ctx));
        v.metaRow.addView(v.store, chipLp(ctx));
        v.metaRow.addView(v.price, chipLp(ctx));
        LinearLayout.LayoutParams metaLp = wrapLp();
        metaLp.topMargin = dp(ctx, 10);
        panel.addView(v.metaRow, metaLp);

        v.cta = text(ctx, c.ctaText, 16, Typeface.create("sans-serif-medium", Typeface.NORMAL), 1);
        v.cta.setGravity(Gravity.CENTER);
        v.cta.setBackground(rounded(c.cta, dp(ctx, 6), 0, 0));
        LinearLayout.LayoutParams ctaLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,
                dp(ctx, 52));
        ctaLp.topMargin = dp(ctx, 14);
        panel.addView(v.cta, ctaLp);

        v.adView.addView(createAdBadge(ctx), badgeParams(ctx));
        registerAssetViews(v);

        // Safe area: the top bar moves below the status bar / cutout, the panel clears the nav bar.
        root.setOnApplyWindowInsetsListener(new View.OnApplyWindowInsetsListener() {
            @Override
            public WindowInsets onApplyWindowInsets(View view, WindowInsets insets) {
                int[] safe = safeInsets(insets);
                int bottom = safe[3];
                if (Build.VERSION.SDK_INT >= 29) {
                    // bars are hidden (immersive): keep the CTA panel clear of the bottom gesture area too
                    bottom = Math.max(bottom, insets.getMandatorySystemGestureInsets().bottom);
                    bottom = Math.max(bottom, insets.getSystemGestureInsets().bottom);
                }
                root.setPadding(safe[0], 0, safe[2], 0);
                topBar.setPadding(0, safe[1], 0, 0);
                setHeight(topBar, barPx + safe[1]);
                panel.setPadding(pad, pad, pad, pad + bottom);
                return insets;
            }
        });
        return root;
    }

    private static void startCountdown(final State s, final int seconds) {
        final Views v = s.views;
        final int viewGeneration = s.viewGeneration;
        v.closeButton.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View view) {
                if (s.viewGeneration == viewGeneration && s.closable) {
                    closeInterstitial(s);
                }
            }
        });
        Runnable tick = new Runnable() {
            int remaining = seconds;

            @Override
            public void run() {
                if (s.viewGeneration != viewGeneration || s.views != v) {
                    return;
                }
                if (remaining <= 0) {
                    v.countdown.setVisibility(View.GONE);
                    v.closeButton.setVisibility(View.VISIBLE);
                    s.closable = true;
                    return;
                }
                v.countdown.setText(String.format(Locale.US, "Close in %ds", remaining));
                remaining--;
                MAIN.postDelayed(this, 1000L);
            }
        };
        tick.run();
    }

    private static void closeInterstitial(State s) {
        if (s.closedSent) {
            return;
        }
        s.closedSent = true;
        teardownShown(s);
        emit(s.key, "closed", "{}");
    }

    // ------------------------------------------------------------------------------------------------
    // Binding / teardown
    // ------------------------------------------------------------------------------------------------

    private static void registerAssetViews(Views v) {
        v.adView.setHeadlineView(v.headline);
        v.adView.setBodyView(v.body);
        v.adView.setIconView(v.icon);
        v.adView.setCallToActionView(v.cta);
        v.adView.setAdvertiserView(v.advertiser);
        v.adView.setStoreView(v.store);
        v.adView.setPriceView(v.price);
        v.adView.setStarRatingView(v.rating);
        if (v.media != null) {
            // interstitial: always; collapsible: only while expanded (collapsed bar has no media view)
            boolean collapsible = v.wrapper != null;
            v.adView.setMediaView(!collapsible || v.expanded ? v.media : null);
        }
    }

    /** Fills the asset views; hides every view whose asset is missing. Call before setNativeAd. */
    private static void bindAssets(Views v, NativeAd ad) {
        setText(v.headline, ad.getHeadline());
        setText(v.body, ad.getBody());
        setText(v.cta, ad.getCallToAction());
        setText(v.advertiser, ad.getAdvertiser());
        setText(v.store, ad.getStore());
        setText(v.price, ad.getPrice());
        Double stars = ad.getStarRating();
        setText(v.rating, stars != null && stars > 0
                ? String.format(Locale.US, "%.1f★", stars) : null);

        NativeAd.Image icon = ad.getIcon();
        Drawable drawable = icon == null ? null : icon.getDrawable();
        v.icon.setImageDrawable(drawable);
        v.icon.setVisibility(drawable != null ? View.VISIBLE : View.GONE);

        if (v.media != null && ad.getMediaContent() != null) {
            v.media.setMediaContent(ad.getMediaContent());
        }

        if (v.separators != null) {
            updateSeparators(v); // banner / collapsible meta line; layoutRow refines per layout
        } else {
            boolean any = hasText(v.rating) || hasText(v.store) || hasText(v.price);
            v.metaRow.setVisibility(any ? View.VISIBLE : View.GONE);
        }
        if (v.wrapper != null) {
            // banner / collapsible: re-apply the per-layout visibility now that the texts are known
            if (v.mediaBox == null) {
                layoutRow(v.adView.getContext(), v, v.collapsedDp, v.collapsedPx, 1, true, false);
            } else {
                applyCollapsibleState(v.adView.getContext(), v);
            }
        }
    }

    /** Removes the shown view (if any) and destroys the shown ad: an ad is spent once shown. */
    private static void teardownShown(State s) {
        s.viewGeneration++;
        s.closable = false;
        if (s.insetsHost != null && s.insetsListener != null) {
            s.insetsHost.removeOnLayoutChangeListener(s.insetsListener);
        }
        s.insetsHost = null;
        s.insetsListener = null;

        Dialog dialog = s.dialog;
        s.dialog = null;
        if (dialog != null) {
            try {
                dialog.setOnDismissListener(null);
                dialog.dismiss();
            } catch (Throwable ignored) {
            }
        }
        Views v = s.views;
        s.views = null;
        if (v != null) {
            if (v.wrapper != null && v.wrapper.getParent() instanceof ViewGroup) {
                ((ViewGroup) v.wrapper.getParent()).removeView(v.wrapper);
            }
            try {
                v.adView.destroy();
            } catch (Throwable ignored) {
            }
        }
        if (s.shownAd != null) {
            s.shownAd.destroy();
            s.shownAd = null;
        }
        s.kind = KIND_NONE;
    }

    // ------------------------------------------------------------------------------------------------
    // Safe area for overlays
    // ------------------------------------------------------------------------------------------------

    /** Keeps the overlay inside the safe area: margins = part of android.R.id.content under bars / cutout. */
    private static void attachSafeAreaTracking(final State s, final ViewGroup content) {
        final Views v = s.views;
        View.OnLayoutChangeListener l = new View.OnLayoutChangeListener() {
            @Override
            public void onLayoutChange(View view, int left, int top, int right, int bottom, int oldLeft,
                                       int oldTop, int oldRight, int oldBottom) {
                if (s.views == v) {
                    applySafeMargins(content, v);
                }
            }
        };
        content.addOnLayoutChangeListener(l);
        s.insetsHost = content;
        s.insetsListener = l;
        applySafeMargins(content, v);
    }

    private static void applySafeMargins(ViewGroup content, Views v) {
        View decor = content.getRootView();
        WindowInsets insets = decor == null ? null : decor.getRootWindowInsets();
        if (insets == null || decor.getWidth() == 0) {
            return;
        }
        int[] safe = safeInsets(insets);
        int[] loc = new int[2];
        content.getLocationInWindow(loc);
        int left = Math.max(0, safe[0] - loc[0]);
        int top = Math.max(0, safe[1] - loc[1]);
        int right = Math.max(0, (loc[0] + content.getWidth()) - (decor.getWidth() - safe[2]));
        int bottom = Math.max(0, (loc[1] + content.getHeight()) - (decor.getHeight() - safe[3]));

        ViewGroup.LayoutParams raw = v.wrapper.getLayoutParams();
        if (!(raw instanceof FrameLayout.LayoutParams)) {
            return;
        }
        final FrameLayout.LayoutParams lp = (FrameLayout.LayoutParams) raw;
        int wantTop = v.top ? top : 0;
        int wantBottom = v.top ? 0 : bottom;
        if (lp.leftMargin != left || lp.rightMargin != right || lp.topMargin != wantTop
                || lp.bottomMargin != wantBottom) {
            lp.leftMargin = left;
            lp.rightMargin = right;
            lp.topMargin = wantTop;
            lp.bottomMargin = wantBottom;
            final View wrapper = v.wrapper;
            // posted: changing layout params from inside a layout pass
            wrapper.post(new Runnable() {
                @Override
                public void run() {
                    wrapper.setLayoutParams(lp);
                }
            });
        }
    }

    /** left, top, right, bottom = max(system window insets, display cutout safe insets). */
    @SuppressWarnings("deprecation")
    private static int[] safeInsets(WindowInsets insets) {
        int l = insets.getSystemWindowInsetLeft();
        int t = insets.getSystemWindowInsetTop();
        int r = insets.getSystemWindowInsetRight();
        int b = insets.getSystemWindowInsetBottom();
        if (Build.VERSION.SDK_INT >= 28) {
            DisplayCutout cutout = insets.getDisplayCutout();
            if (cutout != null) {
                l = Math.max(l, cutout.getSafeInsetLeft());
                t = Math.max(t, cutout.getSafeInsetTop());
                r = Math.max(r, cutout.getSafeInsetRight());
                b = Math.max(b, cutout.getSafeInsetBottom());
            }
        }
        return new int[] {l, t, r, b};
    }

    /**
     * Full-screen ad window: status + navigation bars hidden (swipe shows them transiently), content laid
     * out edge to edge (cutout mode SHORT_EDGES is set by the caller); the insets listener keeps the
     * close button / countdown and the CTA panel inside the safe area.
     */
    @SuppressWarnings("deprecation")
    private static void makeImmersive(Window w) {
        View decor = w.getDecorView();
        if (Build.VERSION.SDK_INT >= 30) {
            w.setDecorFitsSystemWindows(false);
            WindowInsetsController controller = w.getInsetsController();
            if (controller != null) {
                controller.hide(WindowInsets.Type.statusBars() | WindowInsets.Type.navigationBars());
                controller.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
            }
        } else {
            decor.setSystemUiVisibility(View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                    | View.SYSTEM_UI_FLAG_FULLSCREEN
                    | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                    | View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                    | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                    | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Small views
    // ------------------------------------------------------------------------------------------------

    /** Round button with a white glyph drawn in code: down chevron (collapse) or cross (close). */
    private static final class RoundIconButton extends View {
        static final int CHEVRON_DOWN = 0;
        static final int CROSS = 1;

        private final int glyph;
        private final Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint stroke = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final int circlePx; // visual circle diameter; 0 = fill the view (touch target = view size)

        RoundIconButton(Context ctx, int glyph, int fillColor, int glyphColor) {
            this(ctx, glyph, fillColor, glyphColor, 0);
        }

        boolean cornerAnchored;

        RoundIconButton(Context ctx, int glyph, int fillColor, int glyphColor, int circlePx) {
            super(ctx);
            this.glyph = glyph;
            this.circlePx = circlePx;
            fill.setColor(fillColor);
            fill.setStyle(Paint.Style.FILL);
            stroke.setColor(glyphColor);
            stroke.setStyle(Paint.Style.STROKE);
            stroke.setStrokeCap(Paint.Cap.ROUND);
            stroke.setStrokeWidth(2f * ctx.getResources().getDisplayMetrics().density);
            setClickable(true);
            setContentDescription(glyph == CROSS ? "Close ad" : "Collapse ad");
        }

        @Override
        protected void onDraw(Canvas canvas) {
            float w = getWidth();
            float h = getHeight();
            float r = Math.min(w, h) / 2f;
            if (circlePx > 0) {
                r = Math.min(r, circlePx / 2f);
            }
            // centred, or (cornerAnchored) in the view's top-right corner so the touch area extends left and down
            float cx = cornerAnchored ? w - r : w / 2f;
            float cy = cornerAnchored ? r : h / 2f;
            canvas.drawCircle(cx, cy, r, fill);
            float g = r * 0.42f;
            if (glyph == CROSS) {
                canvas.drawLine(cx - g, cy - g, cx + g, cy + g, stroke);
                canvas.drawLine(cx - g, cy + g, cx + g, cy - g, stroke);
            } else {
                float dy = g * 0.5f;
                canvas.drawLine(cx - g, cy - dy, cx, cy + dy, stroke);
                canvas.drawLine(cx, cy + dy, cx + g, cy - dy, stroke);
            }
        }
    }

    private static TextView createAdBadge(Context ctx) {
        TextView badge = new TextView(ctx);
        badge.setText("AD");
        badge.setTextSize(TypedValue.COMPLEX_UNIT_SP, 10);
        badge.setTypeface(Typeface.DEFAULT_BOLD);
        badge.setTextColor(0xFF333333);
        badge.setIncludeFontPadding(false);
        badge.setGravity(Gravity.CENTER);
        badge.setMaxLines(1);
        badge.setPadding(0, 0, 0, 0);
        badge.setBackground(rounded(0xFFF7F7F7, dp(ctx, 3), Math.max(1, dp(ctx, 0.5f)), 0xFFBDBDBD));
        return badge;
    }

    private static FrameLayout.LayoutParams badgeParams(Context ctx) {
        FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(dp(ctx, BADGE_W_DP), dp(ctx, BADGE_H_DP),
                Gravity.TOP | Gravity.LEFT);
        lp.leftMargin = dp(ctx, BADGE_MARGIN_DP);
        lp.topMargin = dp(ctx, BADGE_MARGIN_DP);
        return lp;
    }

    private static TextView chip(Context ctx, Colors c) {
        TextView t = text(ctx, c.secondary, 12, Typeface.DEFAULT, 1);
        t.setPadding(dp(ctx, 8), dp(ctx, 3), dp(ctx, 8), dp(ctx, 3));
        t.setBackground(rounded(0x00000000, dp(ctx, 10), Math.max(1, dp(ctx, 1)), c.secondary));
        return t;
    }

    private static LinearLayout.LayoutParams chipLp(Context ctx) {
        LinearLayout.LayoutParams lp = wrapLp();
        lp.rightMargin = dp(ctx, 8);
        return lp;
    }

    private static TextView text(Context ctx, int color, float sp, Typeface typeface, int maxLines) {
        TextView t = new TextView(ctx);
        t.setTextColor(color);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, sp);
        t.setTypeface(typeface);
        t.setMaxLines(maxLines);
        t.setEllipsize(TextUtils.TruncateAt.END);
        t.setIncludeFontPadding(false);
        return t;
    }

    private static GradientDrawable rounded(int color, float radius, int strokePx, int strokeColor) {
        GradientDrawable d = new GradientDrawable();
        d.setShape(GradientDrawable.RECTANGLE);
        d.setColor(color);
        d.setCornerRadius(radius);
        if (strokePx > 0) {
            d.setStroke(strokePx, strokeColor);
        }
        return d;
    }

    private static LinearLayout.LayoutParams wrapLp() {
        return new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT,
                ViewGroup.LayoutParams.WRAP_CONTENT);
    }

    private static void setText(TextView t, String value) {
        if (t == null) {
            return;
        }
        if (TextUtils.isEmpty(value)) {
            t.setText("");
            t.setVisibility(View.GONE);
        } else {
            t.setText(value);
            t.setVisibility(View.VISIBLE);
        }
    }

    private static boolean hasText(TextView t) {
        return t != null && !TextUtils.isEmpty(t.getText());
    }

    private static void setShown(View view, boolean shown) {
        view.setVisibility(shown ? View.VISIBLE : View.GONE);
    }

    private static void setHeight(View view, int px) {
        ViewGroup.LayoutParams lp = view.getLayoutParams();
        if (lp != null) {
            lp.height = px;
            view.setLayoutParams(lp);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Utils
    // ------------------------------------------------------------------------------------------------

    private static Activity liveActivity() {
        Activity a = activityRef.get();
        if (a == null || a.isFinishing()) {
            return null;
        }
        if (Build.VERSION.SDK_INT >= 17 && a.isDestroyed()) {
            return null;
        }
        return a;
    }

    private static void runOnUi(Runnable r) {
        if (Looper.myLooper() == Looper.getMainLooper()) {
            r.run();
        } else {
            MAIN.post(r);
        }
    }

    private static void emitOnUi(final String key, final String event, final String json) {
        runOnUi(new Runnable() {
            @Override
            public void run() {
                emit(key, event, json);
            }
        });
    }

    private static void emit(String key, String event, String json) {
        GameLibNativeAdsListener l = listener;
        if (l == null) {
            return;
        }
        try {
            l.onEvent(key, event, json == null ? "{}" : json);
        } catch (Throwable t) {
            Log.w(TAG, "listener threw on " + event, t);
        }
    }

    private static String json(String name, String value) {
        try {
            JSONObject o = new JSONObject();
            o.put(name, value == null ? "" : value);
            return o.toString();
        } catch (Throwable t) {
            return "{}";
        }
    }

    private static float density(Context ctx) {
        return ctx.getResources().getDisplayMetrics().density;
    }

    private static int dp(Context ctx, float dp) {
        return Math.round(dp * density(ctx));
    }

    private static int screenWidthPx(Context ctx) {
        return ctx.getResources().getDisplayMetrics().widthPixels;
    }

    private static int screenHeightPx(Context ctx) {
        DisplayMetrics dm = ctx.getResources().getDisplayMetrics();
        return dm.heightPixels;
    }

    private static int clamp(int value, int min, int max) {
        return Math.max(min, Math.min(max, value));
    }
}
