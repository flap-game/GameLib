using System;

namespace FlapGame.GameLib
{
    /// <summary>
    /// Pure logic behind <see cref="IAppLifecycle.Resumed"/>: time away and whether the trip to the background was
    /// game-initiated. Kept apart from the MonoBehaviour so it can be tested without Unity's pause callbacks.
    /// </summary>
    public sealed class AppResumeTracker
    {
        /// <summary>A mark only counts for a pause that comes at most this many seconds after it.</summary>
        public const double MarkLifetimeSeconds = 5;

        private int openScopes;
        private string scopeReason;
        private string pendingMark;
        private double markedAt = double.NaN;
        private double pausedAt = double.NaN;
        private bool externalWhilePaused;
        private string externalReasonWhilePaused;

        public IDisposable BeginExternalActivity(string reason)
        {
            openScopes++;
            scopeReason = reason;
            return new Scope(this);
        }

        /// <summary>
        /// Marks the next pause as game-initiated if it comes within <see cref="MarkLifetimeSeconds"/> of
        /// <paramref name="now"/>; a later pause (the click never left the app) is a real trip away.
        /// </summary>
        public void MarkExternalActivity(string reason, double now)
        {
            pendingMark = reason ?? "external";
            markedAt = now;
        }

        /// <summary>Untimed mark: never expires, applies to the next pause whenever it comes. Prefer the timed overload.</summary>
        public void MarkExternalActivity(string reason) => MarkExternalActivity(reason, double.NaN);

        public void OnPaused(double now)
        {
            string mark = pendingMark != null && (double.IsNaN(markedAt) || now - markedAt <= MarkLifetimeSeconds) ? pendingMark : null;
            pausedAt = now;
            externalWhilePaused = openScopes > 0 || mark != null;
            externalReasonWhilePaused = openScopes > 0 ? scopeReason : mark;
            pendingMark = null;
            markedAt = double.NaN;
        }

        /// <summary>Returns false when there was no matching pause (e.g. a resume delivered at startup).</summary>
        public bool TryResume(double now, out AppResumeInfo info)
        {
            if (double.IsNaN(pausedAt))
            {
                info = default;
                return false;
            }

            // A scope still open at resume time (ad still on screen after a click) also counts.
            bool external = externalWhilePaused || openScopes > 0;
            string reason = externalWhilePaused ? externalReasonWhilePaused : openScopes > 0 ? scopeReason : null;
            info = new AppResumeInfo(TimeSpan.FromSeconds(Math.Max(0, now - pausedAt)), external, reason);

            pausedAt = double.NaN;
            externalWhilePaused = false;
            externalReasonWhilePaused = null;
            return true;
        }

        private void EndScope()
        {
            if (openScopes > 0)
                openScopes--;
            if (openScopes == 0)
                scopeReason = null;
        }

        private sealed class Scope : IDisposable
        {
            private AppResumeTracker owner;

            public Scope(AppResumeTracker owner) => this.owner = owner;

            public void Dispose()
            {
                owner?.EndScope();
                owner = null;
            }
        }
    }
}
