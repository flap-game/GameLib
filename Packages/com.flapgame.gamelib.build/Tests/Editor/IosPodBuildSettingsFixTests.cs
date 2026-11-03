using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace FlapGame.GameLib.Build.Tests
{
    public class IosPodBuildSettingsFixTests
    {
        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "gamelib-podfix-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }

        [Test]
        public void DeploymentTargetsBelowTheMinimumAreRaised_OthersKept()
        {
            const string pbx = "IPHONEOS_DEPLOYMENT_TARGET = 10.0;\nIPHONEOS_DEPLOYMENT_TARGET = 12.0;\n" +
                               "IPHONEOS_DEPLOYMENT_TARGET = 15.0;\nIPHONEOS_DEPLOYMENT_TARGET = 16.4;\n";
            int changed;
            string result = IosPodBuildSettingsFix.RaiseDeploymentTargets(pbx, "15.0", out changed);
            Assert.AreEqual(2, changed);
            Assert.AreEqual("IPHONEOS_DEPLOYMENT_TARGET = 15.0;\nIPHONEOS_DEPLOYMENT_TARGET = 15.0;\n" +
                            "IPHONEOS_DEPLOYMENT_TARGET = 15.0;\nIPHONEOS_DEPLOYMENT_TARGET = 16.4;\n", result);
        }

        [Test]
        public void UnparsableMinimum_ChangesNothing()
        {
            int changed;
            const string pbx = "IPHONEOS_DEPLOYMENT_TARGET = 10.0;";
            Assert.AreEqual(pbx, IosPodBuildSettingsFix.RaiseDeploymentTargets(pbx, "", out changed));
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void PbxprojOnDisk_IsRewrittenOnlyWhenSomethingChanged()
        {
            string dir = Path.Combine(tempDir, "Pods", "Pods.xcodeproj");
            Directory.CreateDirectory(dir);
            string pbx = Path.Combine(dir, "project.pbxproj");
            File.WriteAllText(pbx, "IPHONEOS_DEPLOYMENT_TARGET = 12.0;");
            Assert.AreEqual(1, IosPodBuildSettingsFix.RaisePodsDeploymentTarget(tempDir, "15.0"));
            Assert.AreEqual("IPHONEOS_DEPLOYMENT_TARGET = 15.0;", File.ReadAllText(pbx));
            Assert.AreEqual(0, IosPodBuildSettingsFix.RaisePodsDeploymentTarget(tempDir, "15.0"));
        }

        [Test]
        public void LibraryEvolutionIsSwitchedOffInEveryPodXcconfig()
        {
            string support = Path.Combine(tempDir, "Pods", "Target Support Files");
            Directory.CreateDirectory(Path.Combine(support, "AppMetricaCore"));
            Directory.CreateDirectory(Path.Combine(support, "Pods-UnityFramework"));
            string a = Path.Combine(support, "AppMetricaCore", "AppMetricaCore.release.xcconfig");
            string b = Path.Combine(support, "Pods-UnityFramework", "Pods-UnityFramework.release.xcconfig");
            File.WriteAllText(a, "BUILD_LIBRARY_FOR_DISTRIBUTION = YES\nOTHER = 1\n");
            File.WriteAllText(b, "OTHER = 2\n");
            Assert.AreEqual(1, IosPodBuildSettingsFix.DisableLibraryEvolution(tempDir));
            Assert.AreEqual("BUILD_LIBRARY_FOR_DISTRIBUTION = NO\nOTHER = 1\n", File.ReadAllText(a));
            Assert.AreEqual("OTHER = 2\n", File.ReadAllText(b));
        }

        [Test]
        public void AppMetricaIsDetectedByItsPodFolder()
        {
            Assert.IsFalse(IosPodBuildSettingsFix.UsesAppMetrica(tempDir));
            Directory.CreateDirectory(Path.Combine(tempDir, "Pods", "AppMetricaLibraryAdapter"));
            Assert.IsTrue(IosPodBuildSettingsFix.UsesAppMetrica(tempDir));
        }

        [Test]
        public void IosBuildWithTheEditorCompiledForAnotherTarget_IsRefused()
        {
            Assert.IsNull(IosEditorTargetGuard.FailureMessage(true, BuildTarget.iOS));
            StringAssert.Contains("StandaloneOSX, not iOS",
                                  IosEditorTargetGuard.FailureMessage(false, BuildTarget.StandaloneOSX));
        }
    }
}
