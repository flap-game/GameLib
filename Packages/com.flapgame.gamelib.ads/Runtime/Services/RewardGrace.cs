using System;
using System.Threading.Tasks;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Rewarded ads report "reward earned" and "ad closed" as two separate SDK callbacks, and the reward one may land
    /// AFTER the close (seen on both AdMob and MAX, especially with mediated networks). Deciding at close time would
    /// turn a paid reward into Skipped. Adapters call <see cref="ResolveAsync"/> with the close outcome and a task that
    /// completes when the reward callback fires; a close without reward waits a short grace for a late reward.
    /// </summary>
    public static class RewardGrace
    {
        /// <summary>Default grace: long enough for a late callback hop, short enough not to be noticed after the ad closed.</summary>
        public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(0.5);

        /// <param name="closeOutcome">What the close/fail callback said: Failed (display failed), Skipped (closed, reward unknown) or Completed.</param>
        /// <param name="rewardEarned">Completes when the SDK's reward callback fires. Never needs to complete.</param>
        /// <param name="grace">How long to wait for a late reward after a close without one.</param>
        public static async Task<AdShowResult> ResolveAsync(AdShowResult closeOutcome, Task rewardEarned, TimeSpan grace)
        {
            if (closeOutcome != AdShowResult.Skipped || rewardEarned == null) return closeOutcome;
            if (rewardEarned.IsCompleted) return AdShowResult.Completed;
            if (grace > TimeSpan.Zero)
            {
                // ConfigureAwait(false): the caller resumes on its own context; nothing here touches Unity objects.
                await Task.WhenAny(rewardEarned, Task.Delay(grace)).ConfigureAwait(false);
            }
            return rewardEarned.IsCompleted ? AdShowResult.Completed : AdShowResult.Skipped;
        }
    }
}
