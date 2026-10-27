using System;
using UnityEngine;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Pauses the game while a fullscreen ad is on screen: <c>Time.timeScale = 0</c> and / or <c>AudioListener.pause</c>,
    /// both put back when the ad closes. Created by <c>AdsModule</c> when <c>PauseGameDuringFullscreenAds</c> or
    /// <c>MuteAudioDuringFullscreenAds</c> is on.
    /// <para>
    /// The values from before the ad are put back, except when the game changed them itself while the ad was up (then
    /// the game's value stays). A game that already pauses on <c>FullscreenShowingChanged</c> leaves these switches off.
    /// </para>
    /// </summary>
    public sealed class FullscreenAdPause : IDisposable
    {
        private readonly CompositeAdsService ads;
        private readonly bool pauseTime;
        private readonly bool muteAudio;
        private bool active;
        private float timeScaleBefore;
        private bool audioPausedBefore;

        public FullscreenAdPause(CompositeAdsService ads, bool pauseTime, bool muteAudio)
        {
            this.ads = ads ?? throw new ArgumentNullException(nameof(ads));
            this.pauseTime = pauseTime;
            this.muteAudio = muteAudio;
            ads.FullscreenShowingChanged += OnShowingChanged;
            if (ads.IsShowingFullscreen) OnShowingChanged(true);
        }

        private void OnShowingChanged(bool showing)
        {
            if (showing == active) return;
            active = showing;
            if (showing)
            {
                if (pauseTime)
                {
                    timeScaleBefore = Time.timeScale;
                    Time.timeScale = 0f;
                }
                if (muteAudio)
                {
                    audioPausedBefore = AudioListener.pause;
                    AudioListener.pause = true;
                }
            }
            else
            {
                // Only undo what is still ours: a game that set its own pause during the ad keeps it.
                if (pauseTime && Time.timeScale == 0f) Time.timeScale = timeScaleBefore;
                if (muteAudio && AudioListener.pause) AudioListener.pause = audioPausedBefore;
            }
        }

        public void Dispose()
        {
            ads.FullscreenShowingChanged -= OnShowingChanged;
            if (active) OnShowingChanged(false);
        }
    }
}
