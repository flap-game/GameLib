using System;

namespace FlapGame.GameLib
{
    /// <summary>
    /// Application lifecycle events (pause/resume, focus, quit) exposed as a service so modules and gameplay do not
    /// each need their own MonoBehaviour. Registered by <see cref="GameLibBootstrap"/>.
    /// <para>
    /// It also tells a real return to the game apart from coming back from something the game itself opened
    /// (a full-screen ad, an ad click, a purchase sheet, a link): wrap those in <see cref="BeginExternalActivity"/>
    /// or call <see cref="MarkExternalActivity"/>, and the matching <see cref="Resumed"/> carries
    /// <see cref="AppResumeInfo.FromExternalActivity"/> = true. The app open ad uses this.
    /// </para>
    /// </summary>
    public interface IAppLifecycle
    {
        /// <summary>True while the app is in the background (after OnApplicationPause(true)).</summary>
        bool IsPaused { get; }

        /// <summary>True while the app window has input focus.</summary>
        bool HasFocus { get; }

        /// <summary>Raised on OnApplicationPause; the argument is true when going to the background.</summary>
        event Action<bool> PauseChanged;

        /// <summary>Raised on OnApplicationFocus; the argument is true when focus was gained.</summary>
        event Action<bool> FocusChanged;

        /// <summary>
        /// Raised when the app comes back from the background (after a PauseChanged(true) / PauseChanged(false) pair). Only
        /// a real trip to the background counts: on iOS, Control Center, Notification Center or a system alert pause Unity
        /// too but raise no Resumed - the same as on Android, where they only change focus.
        /// </summary>
        event Action<AppResumeInfo> Resumed;

        /// <summary>Raised once when the application is quitting.</summary>
        event Action Quitting;

        /// <summary>
        /// Marks everything until the returned scope is disposed as game-initiated: any return from the background in
        /// that window is reported with <see cref="AppResumeInfo.FromExternalActivity"/>. Use around ad shows and purchases.
        /// </summary>
        IDisposable BeginExternalActivity(string reason);

        /// <summary>One-shot variant of <see cref="BeginExternalActivity"/>: the next return from the background only.
        /// Call right before opening a URL, the store page, a share sheet, ... The mark expires: it is dropped when the
        /// app does not go to the background within <see cref="AppResumeTracker.MarkLifetimeSeconds"/>, so a click that
        /// never left the app cannot swallow the player's next real return.</summary>
        void MarkExternalActivity(string reason);
    }

    public readonly struct AppResumeInfo
    {
        public AppResumeInfo(TimeSpan timeAway, bool fromExternalActivity, string externalReason)
        {
            TimeAway = timeAway;
            FromExternalActivity = fromExternalActivity;
            ExternalReason = externalReason;
        }

        /// <summary>Real time spent in the background.</summary>
        public TimeSpan TimeAway { get; }

        /// <summary>True when the app left for something it opened itself (ad, purchase, link) - not a return by the player.</summary>
        public bool FromExternalActivity { get; }

        /// <summary>The reason given to <see cref="IAppLifecycle.BeginExternalActivity"/>/<see cref="IAppLifecycle.MarkExternalActivity"/>, or null.</summary>
        public string ExternalReason { get; }

        public override string ToString() =>
            $"away={TimeAway.TotalSeconds:0.#}s external={FromExternalActivity}{(ExternalReason != null ? $" ({ExternalReason})" : "")}";
    }
}
