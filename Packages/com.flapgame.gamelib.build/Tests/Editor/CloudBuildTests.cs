using System;
using System.IO;
using NUnit.Framework;

namespace FlapGame.GameLib.Build.Tests
{
    public class CloudBuildTests
    {
        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "gamelib-build-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }

        [Test]
        public void InstallUploadScript_WritesLfScriptAndGitAttributes()
        {
            File.WriteAllText(Path.Combine(tempDir, ".gitattributes"), "* text=auto");

            CloudBuildScripts.InstallAppStoreUpload(tempDir);

            string script = File.ReadAllText(Path.Combine(tempDir, "BuildScripts", CloudBuildScripts.AppStoreUploadScript));
            StringAssert.StartsWith("#!/bin/bash\n", script);
            StringAssert.DoesNotContain("\r", script);
            StringAssert.Contains("xcrun altool --upload-app", script);
            Assert.AreEqual("* text=auto\n" + CloudBuildScripts.GitAttributesLine + "\n",
                            File.ReadAllText(Path.Combine(tempDir, ".gitattributes")));
        }

        [Test]
        public void GitAttributes_LineAddedOnce()
        {
            string path = Path.Combine(tempDir, ".gitattributes");

            CloudBuildScripts.EnsureGitAttributes(path);
            CloudBuildScripts.EnsureGitAttributes(path);

            Assert.AreEqual(CloudBuildScripts.GitAttributesLine + "\n", File.ReadAllText(path));
        }
    }
}
