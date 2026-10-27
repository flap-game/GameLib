using System.Threading.Tasks;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Optional capability of an adapter: the SDK's own debug screen, which lists every mediated network, whether its
    /// adapter is integrated and initialized, and which ad loaded from where - AdMob's Ad Inspector, MAX's Mediation
    /// Debugger. Open it through <c>ads.OpenDebuggerAsync()</c>, from a QA / cheat menu.
    /// </summary>
    public interface IAdsDebugger
    {
        /// <summary>
        /// Opens the debug screen. Completes with true once it closed (AdMob) or right after it opened (MAX reports no
        /// close), false when it could not open (SDK not initialized; AdMob: the device is not a test device).
        /// </summary>
        Task<bool> OpenDebuggerAsync();
    }
}
