using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib
{
    /// <summary>
    /// The current <see cref="ConsentState"/>, always registered by <see cref="GameLibBootstrap"/>. The module that owns
    /// the CMP writes it; every SDK module reads <see cref="Current"/> at init and follows <see cref="Changed"/>.
    /// </summary>
    public interface IConsentState
    {
        /// <summary>A copy of the current state; never null.</summary>
        ConsentState Current { get; }

        /// <summary>Raised with a copy of the new state when <see cref="Update"/> changes anything.</summary>
        event Action<ConsentState> Changed;

        /// <summary>Replaces the state. Marks it resolved. No event when nothing changed.</summary>
        void Update(ConsentState state);

        /// <summary>
        /// Sets only <see cref="ConsentState.Att"/> - written by whoever asks for App Tracking Transparency, which is not
        /// the CMP - without marking the CMP answer resolved. No event when nothing changed.
        /// </summary>
        void SetAtt(AttStatus status);
    }

    public static class ConsentStateExtensions
    {
        /// <summary>
        /// Completes with true once the CMP answer is in (at once when it already is), or false after
        /// <paramref name="timeout"/>. <paramref name="delay"/> replaces <see cref="Task.Delay(TimeSpan, CancellationToken)"/> in tests.
        /// </summary>
        public static async Task<bool> WaitUntilResolvedAsync(this IConsentState consent, TimeSpan timeout, CancellationToken cancellationToken = default,
                                                              Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            if (consent == null) throw new ArgumentNullException(nameof(consent));
            var answered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged(ConsentState state)
            {
                if (state.IsResolved) answered.TrySetResult(true);
            }

            consent.Changed += OnChanged;
            try
            {
                if (consent.Current.IsResolved) return true;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task timer = (delay ?? Task.Delay)(timeout, cts.Token);
                bool resolved = await Task.WhenAny(answered.Task, timer) == answered.Task;
                cts.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return resolved;
            }
            finally
            {
                consent.Changed -= OnChanged;
            }
        }
    }

    public enum ConsentStatus
    {
        Unknown     = 0,
        NotRequired = 1,
        Required    = 2,
        Obtained    = 3,
    }

    /// <summary>
    /// A consent platform (CMP: MAX CMP, Google UMP). Registered only by a module that owns one; the Ads module runs the
    /// initial flow itself. Gameplay only needs it for a "Privacy settings" button: show it when
    /// <see cref="IsPrivacyOptionsRequired"/> is true and call <see cref="ShowPrivacyOptionsAsync"/> on click
    /// (required by Google for EEA users).
    /// </summary>
    public interface IConsentService
    {
        ConsentStatus Status { get; }

        /// <summary>True when ads can be requested (NotRequired region, or consent already obtained).</summary>
        bool CanRequestAds { get; }

        /// <summary>True when the platform requires the app to offer a privacy-options entry point (EEA users under UMP).</summary>
        bool IsPrivacyOptionsRequired { get; }

        /// <summary>Gathers consent information and shows the form if required. Must complete before ad SDKs initialize.</summary>
        ValueTask RequestAsync(CancellationToken cancellationToken = default);

        /// <summary>Re-opens the consent / privacy options form for a user who already answered. No-op when unavailable.</summary>
        ValueTask ShowPrivacyOptionsAsync(CancellationToken cancellationToken = default);
    }

    public sealed class ConsentStateStore : IConsentState
    {
        private ConsentState current = new();

        public ConsentState Current => current.Clone();

        public event Action<ConsentState> Changed;

        public void Update(ConsentState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            ConsentState next = state.Clone();
            next.IsResolved = true;
            Publish(next);
        }

        public void SetAtt(AttStatus status)
        {
            ConsentState next = current.Clone();
            next.Att = status;
            Publish(next);
        }

        private void Publish(ConsentState next)
        {
            if (next.Equals(current))
                return;

            current = next;
            Action<ConsentState> handlers = Changed;
            if (handlers == null)
                return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try { ((Action<ConsentState>)handler)(next.Clone()); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
