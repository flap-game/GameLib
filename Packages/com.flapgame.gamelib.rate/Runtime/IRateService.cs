using System.Threading.Tasks;

namespace FlapGame.GameLib.Rate
{
    /// <summary>
    /// Rate-us prompts. The game owns its popup; this service decides when to show it (<see cref="NotifyTrigger"/>) and
    /// opens the store's review flow. Registered by <see cref="RateModule"/>.
    /// </summary>
    public interface IRateService
    {
        /// <summary>
        /// One moment where the game could ask (back on Home, after a win...). True = show your rate popup now; the prompt
        /// is counted. False = not now (not the Nth trigger, too soon, cap reached, already rated).
        /// </summary>
        bool NotifyTrigger();

        /// <summary>True once the player went to rate (<see cref="RequestReviewAsync"/> / <see cref="OpenStorePage"/>) or chose "never".</summary>
        bool IsDone { get; }

        /// <summary>Prompts shown since install.</summary>
        int PromptCount { get; }

        /// <summary>
        /// The store's own review sheet (iOS StoreKit, Google Play In-App Review); the store page when it is not available.
        /// The store decides whether the sheet really appears (quotas), so there is no "rated" answer. Marks <see cref="IsDone"/>.
        /// </summary>
        Task RequestReviewAsync();

        /// <summary>Opens the app's page in the store (write a review). Marks <see cref="IsDone"/>.</summary>
        void OpenStorePage();

        /// <summary>The player chose "never": no more prompts.</summary>
        void StopPrompting();

        /// <summary>Clears the counters and <see cref="IsDone"/> (debug menu, tests).</summary>
        void ResetState();
    }
}
