namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Switch for the info logs written on every ad load, show and retry. On a device each <c>Debug.Log</c> also captures
    /// a stack trace, so release builds keep only warnings and errors unless <see cref="AdsModule.VerboseLogs"/> is on.
    /// Call sites check <see cref="Verbose"/> before building the message.
    /// </summary>
    public static class AdsLog
    {
        /// <summary>Set by <see cref="AdsModule"/> at install: on in the Editor and development builds, or with Verbose Logs.</summary>
        public static bool Verbose { get; set; } = true;
    }
}
