using NUnit.Framework;
using UnityEditor.PackageManager;

namespace FlapGame.GameLib.Tests
{
    public class GameLibVersionTests
    {
        [Test]
        public void Current_MatchesCorePackageJson()
        {
            var info = PackageInfo.FindForAssembly(typeof(GameLibVersion).Assembly);

            Assert.IsNotNull(info);
            Assert.AreEqual(info.version, GameLibVersion.Current);
        }
    }
}
