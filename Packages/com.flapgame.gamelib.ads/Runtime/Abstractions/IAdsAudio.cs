namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Optional capability of an adapter: tells the SDK the game's own sound setting, so video ads start at that volume
    /// (AdMob <c>SetApplicationVolume</c> / <c>SetApplicationMuted</c>) or muted (MAX <c>SetMuted</c>). Set it through
    /// <c>ads.SetAdAudio(volume, muted)</c>; the service calls this once the SDK initialized and again on every change.
    /// </summary>
    public interface IAdsAudio
    {
        /// <param name="volume">0..1, already clamped. MAX has no volume: it only mutes at 0.</param>
        /// <param name="muted">The game's sound is off.</param>
        void ApplyAdAudio(float volume, bool muted);
    }
}
