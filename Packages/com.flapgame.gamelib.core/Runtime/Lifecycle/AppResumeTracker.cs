using System;

namespace FlapGame.GameLib
{
    /// <summary>
    /// Pure logic behind <see cref="IAppLifecycle.Resumed"/>: time away and whether the trip to the background was
    /// game-initiated. Kept apart from the MonoBehaviour so it can be tested without Unity's pause callbacks.
    /// </summary>
    public sealed class AppResumeTracker
    {
        private int openScopes;
        private string scopeReason;
        private string pendingMark;
        private double pausedAt = double.NaN;
        private bool externalWhilePaused;
        private string externalReasonWhilePaused;

        public IDisposable BeginExternalActivity(string reason)
        {
            openScopes++;
            scopeReason = reason;
            return new Scope(this);
        }

        public void MarkExternalActivity(string reason) => pendingMark = reason ?? "external";

        public void OnPaused(double now)
        {
            pausedAt = now;
            externalWhilePaused = openScopes > 0 || pendingMark != null;
            externalReasonWhilePaused = openScopes > 0 ? scopeReason : pendingMark;
            pendingMark = null;
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
