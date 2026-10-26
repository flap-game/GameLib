using System;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace FlapGame.GameLib
{
    /// <summary>
    /// Default <see cref="IAppLifecycle"/>: a persistent MonoBehaviour that forwards Unity's application callbacks.
    /// Created by <see cref="GameLibBootstrap"/>; destroyed when the registry is disposed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AppLifecycleBehaviour : MonoBehaviour, IAppLifecycle, IDisposable
    {
        private readonly AppResumeTracker resumeTracker = new();

        public bool IsPaused { get; private set; }

        public bool HasFocus { get; private set; } = true;

        public event Action<bool> PauseChanged;

        public event Action<bool> FocusChanged;

        public event Action<AppResumeInfo> Resumed;

        public event Action Quitting;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void _GameLibLifecycle_ResetEnteredBackground();
        [DllImport("__Internal")] private static extern int _GameLibLifecycle_EnteredBackground();

        private static void ResetEnteredBackground() => _GameLibLifecycle_ResetEnteredBackground();

        // iOS pauses Unity for Control Center, Notification Center and system alerts too; only a real trip counts.
        private static bool EnteredBackground() => _GameLibLifecycle_EnteredBackground() != 0;
#else
        private static void ResetEnteredBackground()
        {
        }

        // Android pauses only when the app really goes to the background (the notification shade is a focus change).
        private static bool EnteredBackground() => true;
#endif

        public static AppLifecycleBehaviour Create()
        {
            var go = new GameObject("[GameLib] Lifecycle");
            if (Application.isPlaying)
                DontDestroyOnLoad(go);
            return go.AddComponent<AppLifecycleBehaviour>();
        }

        public IDisposable BeginExternalActivity(string reason) => resumeTracker.BeginExternalActivity(reason);

        public void MarkExternalActivity(string reason) => resumeTracker.MarkExternalActivity(reason, Time.realtimeSinceStartupAsDouble);

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus == IsPaused)
                return;

            IsPaused = pauseStatus;
            Raise(PauseChanged, pauseStatus);

            if (pauseStatus)
            {
                ResetEnteredBackground();
                resumeTracker.OnPaused(Time.realtimeSinceStartupAsDouble);
            }
            else if (resumeTracker.TryResume(Time.realtimeSinceStartupAsDouble, out AppResumeInfo info))
            {
                if (EnteredBackground()) Raise(Resumed, info);
                else Debug.Log($"[GameLib] Back from an overlay without leaving the app ({info}) - not a return.");
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            HasFocus = hasFocus;
            Raise(FocusChanged, hasFocus);
        }

        private void OnApplicationQuit()
        {
            Action handlers = Quitting;
            if (handlers == null)
                return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try { ((Action)handler)(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        public void Dispose()
        {
            PauseChanged = null;
            FocusChanged = null;
            Resumed = null;
            Quitting = null;
            if (this == null)
                return;

            if (Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);
        }

        // Invoke each subscriber separately so one throwing handler cannot starve the others.
        private static void Raise<T>(Action<T> handlers, T value)
        {
            if (handlers == null)
                return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try { ((Action<T>)handler)(value); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
