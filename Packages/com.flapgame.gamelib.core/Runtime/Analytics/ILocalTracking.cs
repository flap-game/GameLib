namespace FlapGame.GameLib
{
    /// <summary>
    /// Registered by the local tracking module (<c>com.flapgame.gamelib.analytics.local</c>) when this build runs on one
    /// of the test devices it recognizes (the office LAN, or the Editor with its toggle on). It installs before the online
    /// sinks, which ask <see cref="LocalTracking.SuppressesOnline"/> and stay off while it is active, so events from a
    /// test session do not land in the production dashboards.
    /// Nothing registers it in a normal build: <see cref="LocalTracking.SuppressesOnline"/> is false and every sink installs.
    /// </summary>
    public interface ILocalTracking
    {
        /// <summary>
        /// True when local tracking took over this session. Decided once, while the module installs - it stays true even if
        /// the local sink later turns itself off (no ATT consent, the server unreachable), because the online sinks have
        /// already been skipped by then.
        /// </summary>
        bool IsActive { get; }

        /// <summary>True when the analytics sinks (Firebase Analytics, ...) must stay off while <see cref="IsActive"/>.</summary>
        bool SuppressesOnlineTracking { get; }

        /// <summary>
        /// True when the attribution SDKs (AppsFlyer) must stay off too. Off by default: attribution is not a dashboard
        /// the test traffic spoils, and a test install still has to attribute like a real one.
        /// </summary>
        bool SuppressesAttribution { get; }
    }

    /// <summary>What a sink module asks before installing itself. Safe with no local tracking module in the project.</summary>
    public static class LocalTracking
    {
        /// <summary>True when an analytics sink must stay off because local tracking is handling this session.</summary>
        public static bool SuppressesOnline(IServiceRegistry services) =>
            services != null && services.TryResolve(out ILocalTracking local) && local != null && local.IsActive && local.SuppressesOnlineTracking;

        /// <summary>True when an attribution SDK must stay off because local tracking is handling this session.</summary>
        public static bool SuppressesAttribution(IServiceRegistry services) =>
            services != null && services.TryResolve(out ILocalTracking local) && local != null && local.IsActive && local.SuppressesAttribution;
    }
}
