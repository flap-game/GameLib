using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.AppIcon
{
    /// <summary>Platform side of <see cref="AppIconService"/>.</summary>
    public interface IAppIconBackend
    {
        bool IsSupported { get; }

        /// <summary>Alternate icon name in use, or null for the default.</summary>
        string CurrentIcon { get; }

        /// <summary>Switches the icon (null = default). Completes with null on success, else the error text.</summary>
        Task<string> SetIconAsync(string iconName);
    }

    public static class AppIconBackends
    {
        /// <summary>UIKit on iOS; a PlayerPrefs simulation in the Editor; none elsewhere.</summary>
        public static IAppIconBackend ForPlatform()
        {
#if UNITY_EDITOR
            return new EditorAppIconBackend();
#elif UNITY_IOS
            return new IosAppIconBackend();
#else
            return null;
#endif
        }
    }

    /// <summary>Remembers the choice in PlayerPrefs so icon pickers can be tested in Play mode.</summary>
    internal sealed class EditorAppIconBackend : IAppIconBackend
    {
        private const string Key = "gamelib.appicon.editor_icon";

        public bool IsSupported => true;

        public string CurrentIcon
        {
            get
            {
                string icon = PlayerPrefs.GetString(Key, string.Empty);
                return icon.Length == 0 ? null : icon;
            }
        }

        public Task<string> SetIconAsync(string iconName)
        {
            if (iconName == null) PlayerPrefs.DeleteKey(Key);
            else PlayerPrefs.SetString(Key, iconName);
            Debug.Log($"[AppIcon] Editor: icon set to '{iconName ?? "default"}' (the iOS build changes the real icon).");
            return Task.FromResult<string>(null);
        }
    }

#if UNITY_IOS && !UNITY_EDITOR
    /// <summary>UIApplication alternate icons through GameLibAppIcon.mm.</summary>
    internal sealed class IosAppIconBackend : IAppIconBackend
    {
        private delegate void SetIconCallback(int requestId, string error);

        private static readonly Dictionary<int, TaskCompletionSource<string>> Pending = new();
        private static int nextRequest;

        [DllImport("__Internal")] private static extern int _GameLibAppIcon_IsSupported();
        [DllImport("__Internal")] private static extern string _GameLibAppIcon_Current();
        [DllImport("__Internal")] private static extern void _GameLibAppIcon_Set(string iconName, int requestId, SetIconCallback callback);

        public bool IsSupported => _GameLibAppIcon_IsSupported() != 0;

        public string CurrentIcon => _GameLibAppIcon_Current();

        public Task<string> SetIconAsync(string iconName)
        {
            var done = new TaskCompletionSource<string>();
            int id = ++nextRequest;
            Pending[id] = done;
            _GameLibAppIcon_Set(iconName, id, OnSet);
            return done.Task;
        }

        // Called on the main thread by the plugin.
        [AOT.MonoPInvokeCallback(typeof(SetIconCallback))]
        private static void OnSet(int requestId, string error)
        {
            if (!Pending.TryGetValue(requestId, out TaskCompletionSource<string> done)) return;
            Pending.Remove(requestId);
            done.TrySetResult(string.IsNullOrEmpty(error) ? null : error);
        }
    }
#endif
}
