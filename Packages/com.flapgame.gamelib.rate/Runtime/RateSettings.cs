using System;
using UnityEngine;

namespace FlapGame.GameLib.Rate
{
    /// <summary>When <see cref="IRateService.NotifyTrigger"/> says "ask now". Set on the <see cref="RateModule"/> asset.</summary>
    [Serializable]
    public sealed class RateSettings
    {
        [Tooltip("Ask on every Nth trigger, counted since the last prompt (or install). A trigger blocked by another rule still " +
                 "counts, so the next one asks. 1 = every trigger.")]
        [Min(1)] public int PromptEveryTriggers = 3;

        [Tooltip("Most prompts ever shown. 0 = no limit.")]
        [Min(0)] public int MaxPrompts;

        [Tooltip("Minimum hours between two prompts. 0 = none.")]
        [Min(0f)] public float MinHoursBetweenPrompts;

        [Tooltip("No more prompts once the player went to rate (review sheet or store page) or chose never.")]
        public bool StopAfterRated = true;

        [Tooltip("App Store id (the digits of apps.apple.com/app/id...), for the store page on iOS. Empty = no store page on iOS.")]
        public string AppStoreId;

        [Tooltip("Gradle coordinates of Google Play In-App Review, added to the Android build.")]
        public string AndroidReviewLibrary = "com.google.android.play:review:2.0.2";

        public RateSettings Clone() => (RateSettings)MemberwiseClone();
    }
}
