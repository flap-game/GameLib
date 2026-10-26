using System;
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

        public static AppLifecycleBehaviour Create()
        {
            var go = new GameObject("[GameLib] Lifecycle");
            if (Application.isPlaying)
                DontDestroyOnLoad(go);
            return go.AddComponent<AppLifecycleBehaviour>();
        }

        public IDisposable BeginExternalActivity(string reason) => resumeTracker.BeginExternalActivity(reason);

        public void MarkExternalActivity(string reason) => resumeTracker.MarkExternalActivity(reason);

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus == IsPaused)
                return;

            IsPaused = pauseStatus;
            Raise(PauseChanged, pauseStatus);

            if (pauseStatus)
            {
                resumeTracker.OnPaused(Time.realtimeSinceStartupAsDouble);
            }
            else if (resumeTracker.TryResume(Time.realtimeSinceStartupAsDouble, out AppResumeInfo info))
            {
                Raise(Resumed, info);
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
