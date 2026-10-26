using System;
using UnityEngine;

namespace FlapGame.GameLib
{
    /// <summary>
    /// Starts GameLib before the first scene loads when <see cref="GameLibSettings.AutoInitialize"/> is on, so no
    /// bootstrap object is needed in any scene. Services become available asynchronously; await
    /// <see cref="GameLibBootstrap.WhenReadyAsync()"/>.
    /// </summary>
    internal static class GameLibAutoStart
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Enter Play Mode without domain reload keeps statics; start every play session clean.
            GameLibBootstrap.Reset();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void HookPlayModeExit()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        // Without domain reload the last session's registry (and its handlers) would live on until the next Play. Reset
        // once back in Edit mode: at ExitingPlayMode the scene's OnDisable/OnDestroy and Quitting handlers have not run yet.
        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
                GameLibBootstrap.Reset();
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static async void Start()
        {
            GameLibSettings settings = GameLibSettings.Load();
            if (settings == null || !settings.AutoInitialize)
                return;

            try
            {
                await GameLibBootstrap.InitializeAsync(settings, Application.exitCancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Play mode stopped or Reset() mid-init - nothing to report.
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
