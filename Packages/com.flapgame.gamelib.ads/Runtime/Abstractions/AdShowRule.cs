using System;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// A game's own rule for fullscreen ads (interstitial, rewarded, app open), on top of <see cref="AdsPolicy"/> and the
    /// placement rules. Add it with <c>IAdsService.AddRule</c>; it is asked after every built-in rule passed, so it only
    /// decides what the shared rules allow.
    /// </summary>
    /// <example>
    /// <code>
    /// // "an interstitial only after a level end, never during the tutorial"
    /// ads.AddRule(AdShowRule.From(p => p.Format != AdFormat.Interstitial || (!tutorial.Active &amp;&amp; justFinishedLevel)));
    /// </code>
    /// </example>
    public abstract class AdShowRule
    {
        /// <summary>
        /// True = this rule lets <paramref name="placement"/> show now; false blocks it with <see cref="AdBlockReason.GameRule"/>.
        /// Called by every <c>CanShow</c>, which UI may poll each frame: keep it cheap and free of side effects (count in
        /// <see cref="OnRequested"/> instead).
        /// </summary>
        public abstract bool Allows(AdPlacement placement);

        /// <summary>
        /// Every <c>ShowAsync</c> of a fullscreen placement, after the rules were asked. <paramref name="reason"/> is why it
        /// is not shown, or <see cref="AdBlockReason.None"/> when it is about to show.
        /// </summary>
        public virtual void OnRequested(AdPlacement placement, AdBlockReason reason) { }

        /// <summary>A fullscreen ad of <paramref name="placement"/> was on screen (Completed or Skipped).</summary>
        public virtual void OnShown(AdPlacement placement, AdShowResult result) { }

        /// <summary>A rule from a condition only.</summary>
        public static AdShowRule From(Func<AdPlacement, bool> allows) =>
            new DelegateRule(allows ?? throw new ArgumentNullException(nameof(allows)));

        private sealed class DelegateRule : AdShowRule
        {
            private readonly Func<AdPlacement, bool> allows;
            public DelegateRule(Func<AdPlacement, bool> allows) => this.allows = allows;
            public override bool Allows(AdPlacement placement) => allows(placement);
        }
    }
}
