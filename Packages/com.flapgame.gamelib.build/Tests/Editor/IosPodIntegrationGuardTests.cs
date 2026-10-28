using System;
using System.IO;
using NUnit.Framework;

namespace FlapGame.GameLib.Build.Tests
{
    public class IosPodIntegrationGuardTests
    {
        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "gamelib-podguard-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }

        [Test]
        public void NoPodfile_Passes()
        {
            Assert.IsNull(IosPodIntegrationGuard.FailureMessage(tempDir));
        }

        [Test]
        public void PodsProjectWritten_Passes()
        {
            File.WriteAllText(Path.Combine(tempDir, "Podfile"), "pod 'AppLovinSDK'");
            Directory.CreateDirectory(Path.Combine(tempDir, "Pods", "Pods.xcodeproj"));
            Assert.IsNull(IosPodIntegrationGuard.FailureMessage(tempDir));
        }

        [Test]
        public void PodInstallAborted_Fails()
        {
            File.WriteAllText(Path.Combine(tempDir, "Podfile"), "pod 'AppLovinSDK'");
            Directory.CreateDirectory(Path.Combine(tempDir, "Pods", "AppLovinSDK"));
            StringAssert.Contains("Pods.xcodeproj' is missing", IosPodIntegrationGuard.FailureMessage(tempDir));
        }
    }
}
