#if GAMELIB_ADMOB
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// (0.9.0) C# side of GameLib's native ad views (NativeBanner, NativeCollapsible, NativeInterstitial). The views are
    /// built natively: Android <c>com.flapgame.gamelib.ads.nativeads.GameLibNativeAds</c> (Plugins/Android), iOS
    /// <c>GameLibNativeAds_*</c> (Plugins/iOS/GameLibNativeAds.mm). One native ad and one view per opaque key chosen by
    /// the adapter. Native events <c>(key, event, json)</c> arrive on any thread; they are parsed and delivered on the
    /// Unity main thread through the dispatcher given to the constructor. In the Editor and on other platforms every
    /// load / show reports <c>failed</c> / <c>show_failed</c> ("unsupported_platform") one frame later.
    /// </summary>
    internal sealed class AdMobNativeBridge : IDisposable
    {
        private const string LogPrefix = "[Ads/AdMob]";
        internal const string UnsupportedPlatform = "unsupported_platform";

        // Event names of the native protocol.
        internal const string EvLoaded     = "loaded";
        internal const string EvFailed     = "failed";
        internal const string EvShown      = "shown";
        internal const string EvShowFailed = "show_failed";
        internal const string EvImpression = "impression";
        internal const string EvClicked    = "clicked";
        internal const string EvClosed     = "closed";
        internal const string EvExpanded   = "expanded";
        internal const string EvCollapsed  = "collapsed";
        internal const string EvPaid       = "paid";

        /// <summary>Payload of a native event; fields absent from the json keep their defaults.</summary>
        [Serializable]
        internal sealed class EventData
        {
            public string error;
            public long valueMicros;
            public string currency;
            public int precision; // AdValue precision: 0 unknown, 1 estimated, 2 publisher provided, 3 precise
            public string adSource;
        }

        // Receiver of the static iOS callback (one adapter per app).
        private static AdMobNativeBridge current;

        private readonly Action<Action> runOnMain;
        private readonly Action<string, string, EventData> onEvent;
        private bool initializeTried;
        private bool ready;
        private bool disposed;


        /// <param name="runOnMain">Runs an action on the Unity main thread (the adapter's dispatcher).</param>
        /// <param name="onEvent">(key, event, data), always on the main thread.</param>
        public AdMobNativeBridge(Action<Action> runOnMain, Action<string, string, EventData> onEvent)
        {
            this.runOnMain = runOnMain ?? throw new ArgumentNullException(nameof(runOnMain));
            this.onEvent   = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
        }

        /// <summary>ARGB int (<c>(a&lt;&lt;24)|(r&lt;&lt;16)|(g&lt;&lt;8)|b</c>) of a Unity color, as the native side expects.</summary>
        public static int Argb(Color color)
        {
            Color32 c = color;
            return (c.a << 24) | (c.r << 16) | (c.g << 8) | c.b;
        }

        public void Load(string key, string adUnitId)
        {
            if (disposed) return;
            if (!EnsureInitialized()) { ReportLater(key, EvFailed, UnsupportedPlatform); return; }
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                plugin.CallStatic("load", key, adUnitId);
#elif UNITY_IOS && !UNITY_EDITOR
                GameLibNativeAds_Load(key, adUnitId);
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Native load {key} threw: {e.Message}");
                ReportLater(key, EvFailed, e.Message);
            }
        }

        public void ShowBanner(string key, int heightDp, bool top, int bg, int text, int cta, int ctaText)
        {
            if (disposed) return;
            if (!EnsureInitialized()) { ReportLater(key, EvShowFailed, UnsupportedPlatform); return; }
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                plugin.CallStatic("showBanner", key, heightDp, top, bg, text, cta, ctaText);
#elif UNITY_IOS && !UNITY_EDITOR
                GameLibNativeAds_ShowBanner(key, heightDp, top, bg, text, cta, ctaText);
#endif
                SetBottomBar(key, !top);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Native showBanner {key} threw: {e.Message}");
                ReportLater(key, EvShowFailed, e.Message);
            }
        }

        public void ShowCollapsible(string key, int collapsedDp, bool startExpanded, int buttonDelaySeconds,
                                    int bg, int text, int cta, int ctaText)
        {
            if (disposed) return;
            if (!EnsureInitialized()) { ReportLater(key, EvShowFailed, UnsupportedPlatform); return; }
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                plugin.CallStatic("showCollapsible", key, collapsedDp, startExpanded, buttonDelaySeconds, bg, text, cta, ctaText);
#elif UNITY_IOS && !UNITY_EDITOR
                GameLibNativeAds_ShowCollapsible(key, collapsedDp, startExpanded, buttonDelaySeconds, bg, text, cta, ctaText);
#endif
                SetBottomBar(key, true); // a collapsible always sits at the bottom
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Native showCollapsible {key} threw: {e.Message}");
                ReportLater(key, EvShowFailed, e.Message);
            }
        }

        public void ShowInterstitial(string key, int closeDelaySeconds, int bg, int text, int cta, int ctaText)
        {
            if (disposed) return;
            if (!EnsureInitialized()) { ReportLater(key, EvShowFailed, UnsupportedPlatform); return; }
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                plugin.CallStatic("showInterstitial", key, closeDelaySeconds, bg, text, cta, ctaText);
#elif UNITY_IOS && !UNITY_EDITOR
                GameLibNativeAds_ShowInterstitial(key, closeDelaySeconds, bg, text, cta, ctaText);
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Native showInterstitial {key} threw: {e.Message}");
                ReportLater(key, EvShowFailed, e.Message);
            }
        }

        /// <summary>Removes the key's view from the screen. No event.</summary>
        public void Hide(string key)
        {
            if (disposed || !ready) return;
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                plugin.CallStatic("hide", key);
#elif UNITY_IOS && !UNITY_EDITOR
                GameLibNativeAds_Hide(key);
#endif
                SetBottomBar(key, false);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Native hide {key} threw: {e.Message}");
            }
        }

        /// <summary>
        /// iOS: puts the shown NativeCollapsible views back on top of their superview, after a standard banner was
        /// shown (GMA adds it to the same root view, above them). Android needs none: the collapsible is raised by Z.
        /// </summary>
        public void KeepCollapsiblesOnTop()
        {
            if (disposed || !ready) return;
#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                GameLibNativeAds_RaiseCollapsibles();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Native raise threw: {e.Message}");
            }
#endif
        }

        /// <summary>Hides the view, destroys the native ad and forgets the key. No event.</summary>
        public void Destroy(string key)
        {
            if (disposed || !ready) return;
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                plugin.CallStatic("destroy", key);
#elif UNITY_IOS && !UNITY_EDITOR
                GameLibNativeAds_Destroy(key);
#endif
                SetBottomBar(key, false);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Native destroy {key} threw: {e.Message}");
            }
        }

        /// <summary>Stops delivering events. Destroy the keys first.</summary>
        public void Dispose()
        {
            HomeIndicator.ReleaseAll(IndicatorOwner);
            disposed = true;
            if (ReferenceEquals(current, this)) current = null;
        }

        // ---------------------------------------------------------------- events

        // Any thread. Parsing and delivery happen on the main thread.
        private void Receive(string key, string evt, string json)
        {
            if (disposed || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(evt)) return;
            runOnMain(() =>
            {
                if (disposed) return;
                if (evt == EvShowFailed) SetBottomBar(key, false);
                onEvent(key, evt, Parse(json));
            });
        }

        private static EventData Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return new EventData();
            try
            {
                return JsonUtility.FromJson<EventData>(json) ?? new EventData();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Native event json not parsed ({e.Message}): {json}");
                return new EventData();
            }
        }

        private const string IndicatorOwner = "native:";

        // iOS: a native bar at the bottom reaches the screen's bottom edge; the home indicator auto-hides while one is up.
        private static void SetBottomBar(string key, bool shownAtBottom) => HomeIndicator.Set(IndicatorOwner + key, shownAtBottom);

        // A failure found on the C# side is reported like a native one, one frame later: callers never see the event
        // inside the call that caused it.
        private void ReportLater(string key, string evt, string error) => _ = ReportLaterAsync(key, evt, error);

        private async Task ReportLaterAsync(string key, string evt, string error)
        {
            await Task.Yield();
            if (disposed) return;
            runOnMain(() =>
            {
                if (disposed) return;
                onEvent(key, evt, new EventData { error = error });
            });
        }

        // ---------------------------------------------------------------- platforms

#if UNITY_ANDROID && !UNITY_EDITOR
        private const string JavaClass    = "com.flapgame.gamelib.ads.nativeads.GameLibNativeAds";
        private const string JavaListener = "com.flapgame.gamelib.ads.nativeads.GameLibNativeAdsListener";

        private AndroidJavaClass plugin;
        private Listener listener; // kept alive: the Java side holds it

        private sealed class Listener : AndroidJavaProxy
        {
            private readonly AdMobNativeBridge owner;

            public Listener(AdMobNativeBridge owner) : base(JavaListener)
            {
                this.owner = owner;
            }

            // Called by Java (any thread) through the proxy: name and signature must match the Java interface.
            // ReSharper disable once InconsistentNaming
            public void onEvent(string key, string @event, string json) => owner.Receive(key, @event, json);
        }

        private bool EnsureInitialized()
        {
            if (initializeTried) return ready;
            initializeTried = true;
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    plugin   = new AndroidJavaClass(JavaClass);
                    listener = new Listener(this);
                    plugin.CallStatic("initialize", activity, listener);
                }
                ready = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Native ads bridge not available: {e.Message}");
                plugin = null;
                ready  = false;
            }
            return ready;
        }
#elif UNITY_IOS && !UNITY_EDITOR
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void NativeCallback(IntPtr key, IntPtr evt, IntPtr json);

        // Static and kept alive: the native side stores the pointer.
        private static readonly NativeCallback Callback = OnNativeEvent;

        [DllImport("__Internal")] private static extern void GameLibNativeAds_Initialize(NativeCallback callback);
        [DllImport("__Internal")] private static extern void GameLibNativeAds_Load(string key, string adUnitId);
        [DllImport("__Internal")] private static extern void GameLibNativeAds_ShowBanner(string key, int heightDp,
            [MarshalAs(UnmanagedType.I1)] bool top, int bg, int text, int cta, int ctaText);
        [DllImport("__Internal")] private static extern void GameLibNativeAds_ShowCollapsible(string key, int collapsedDp,
            [MarshalAs(UnmanagedType.I1)] bool startExpanded, int buttonDelaySeconds, int bg, int text, int cta, int ctaText);
        [DllImport("__Internal")] private static extern void GameLibNativeAds_ShowInterstitial(string key, int closeDelaySeconds,
            int bg, int text, int cta, int ctaText);
        [DllImport("__Internal")] private static extern void GameLibNativeAds_Hide(string key);
        [DllImport("__Internal")] private static extern void GameLibNativeAds_Destroy(string key);
        [DllImport("__Internal")] private static extern void GameLibNativeAds_RaiseCollapsibles();

        // The strings are valid only during the call: copied before anything else.
        [AOT.MonoPInvokeCallback(typeof(NativeCallback))]
        private static void OnNativeEvent(IntPtr key, IntPtr evt, IntPtr json)
        {
            try
            {
                string k = Utf8(key), e = Utf8(evt), j = Utf8(json);
                current?.Receive(k, e, j);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static string Utf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            int length = 0;
            while (Marshal.ReadByte(ptr, length) != 0) length++;
            if (length == 0) return string.Empty;
            var bytes = new byte[length];
            Marshal.Copy(ptr, bytes, 0, length);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        private bool EnsureInitialized()
        {
            if (initializeTried) return ready;
            initializeTried = true;
            try
            {
                current = this;
                GameLibNativeAds_Initialize(Callback);
                ready = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Native ads bridge not available: {e.Message}");
                ready = false;
            }
            return ready;
        }
#else
        // Editor and platforms without a native implementation.
        private bool EnsureInitialized()
        {
            if (!initializeTried)
            {
                initializeTried = true;
                ready = false;
                Debug.Log($"{LogPrefix} Native ads run on Android / iOS devices only: every native load here fails ({UnsupportedPlatform}).");
            }
            return ready; // always false here
        }
#endif
    }
}
#endif
