namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Optional capability of an adapter: a debug screen about the ads shown recently - which network and creative, and a
    /// way to report a bad one (AppLovin's Creative Debugger). Open it through <c>ads.OpenCreativeDebugger()</c>.
    /// </summary>
    public interface IAdsCreativeDebugger
    {
        /// <summary>Opens it. False when it could not (SDK not initialized).</summary>
        bool OpenCreativeDebugger();
    }
}
