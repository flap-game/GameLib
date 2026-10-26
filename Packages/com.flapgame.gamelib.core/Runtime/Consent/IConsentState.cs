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
    }

    /// <summary>
    /// The CMP's game-facing side, registered only by a module that owns a consent form (MAX CMP, UMP).
    /// Show a "Privacy settings" button when <see cref="IsPrivacyOptionsRequired"/> is true (required by Google for EEA users).
    /// </summary>
    public interface IConsentService
    {
        bool IsPrivacyOptionsRequired { get; }

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
