using System;
using System.Collections;
using UnityEngine;

namespace FlapGame.GameLib.Extensions
{
    public static class CoroutineHelper
    {
        /// <summary>
        /// Runs <paramref name="action"/> after <paramref name="seconds"/> on <paramref name="owner"/>: it is cancelled when
        /// the owner is disabled or destroyed. Unscaled time keeps counting while the game is paused (timeScale 0).
        /// Keep the returned Coroutine to cancel it with StopCoroutine.
        /// <para>0 seconds or less runs the action right away, before Delay returns. Like every coroutine it needs an
        /// active owner: on an inactive one Unity logs an error and nothing runs.</para>
        /// </summary>
        public static Coroutine Delay(this MonoBehaviour owner, float seconds, Action action, bool unscaledTime = false) =>
            owner.StartCoroutine(DelayRoutine(seconds, action, unscaledTime));

        /// <summary>Runs <paramref name="action"/> on the next frame.</summary>
        public static Coroutine NextFrame(this MonoBehaviour owner, Action action) => owner.StartCoroutine(NextFrameRoutine(action));

        /// <summary>
        /// Runs <paramref name="onDone"/> once <paramref name="condition"/> is true (checked every frame), or
        /// <paramref name="onTimeout"/> when <paramref name="timeoutSeconds"/> (unscaled) pass first. A timeout of 0 or less waits forever.
        /// </summary>
        public static Coroutine WaitUntil(this MonoBehaviour owner, Func<bool> condition, Action onDone,
            float timeoutSeconds = 0f, Action onTimeout = null) =>
            owner.StartCoroutine(WaitUntilRoutine(condition, onDone, timeoutSeconds, onTimeout));

        public static IEnumerator DelayRoutine(float seconds, Action action, bool unscaledTime = false)
        {
            if (seconds > 0f)
            {
                if (unscaledTime) yield return new WaitForSecondsRealtime(seconds);
                else yield return new WaitForSeconds(seconds);
            }
            action?.Invoke();
        }

        private static IEnumerator NextFrameRoutine(Action action)
        {
            yield return null;
            action?.Invoke();
        }

        public static IEnumerator WaitUntilRoutine(Func<bool> condition, Action onDone, float timeoutSeconds = 0f, Action onTimeout = null)
        {
            // Checked here, not inside the iterator, so a null condition throws at the call instead of on the first frame.
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            return WaitUntilIterator(condition, onDone, timeoutSeconds, onTimeout);
        }

        private static IEnumerator WaitUntilIterator(Func<bool> condition, Action onDone, float timeoutSeconds, Action onTimeout)
        {
            float deadline = timeoutSeconds > 0f ? Time.realtimeSinceStartup + timeoutSeconds : float.PositiveInfinity;
            while (!condition())
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    onTimeout?.Invoke();
                    yield break;
                }
                yield return null;
            }
            onDone?.Invoke();
        }
    }
}
