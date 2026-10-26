using System.Threading;
using System.Threading.Tasks;

namespace FlapGame.GameLib
{
    /// <summary>
    /// iOS App Tracking Transparency, registered by the ATT module (<c>com.flapgame.gamelib.att</c>). Every answer is
    /// also written to <see cref="IConsentState"/> (<see cref="ConsentState.Att"/>). On Android, in the Editor and before
    /// iOS 14 the status is <see cref="AttStatus.NotApplicable"/> and nothing is shown.
    /// </summary>
    public interface IAttService
    {
        /// <summary>The live system status - also changes when another SDK (e.g. MAX's consent flow) shows the prompt.</summary>
        AttStatus Status { get; }

        /// <summary>
        /// Shows the system prompt when the status is not determined, and completes with the answer (at once with the
        /// current status otherwise). Leaving the app for the prompt is not a return from the background (no app open ad).
        /// </summary>
        Task<AttStatus> RequestAsync(CancellationToken cancellationToken = default);
    }
}
