namespace FlapGame.GameLib
{
    /// <summary>
    /// Version shared by every GameLib package (lockstep). Tools/release.ps1 rewrites it;
    /// the Installer uses it to pick the matching git tag for the modules it adds.
    /// </summary>
    public static class GameLibVersion
    {
        public const string Current = "0.8.13";
    }
}
