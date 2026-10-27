using System.IO;
using System.Linq;
using FlapGame.GameLib.Editor;
using NUnit.Framework;

namespace FlapGame.GameLib.Tests
{
    public class GameLibCliTests
    {
        [Test]
        public void Install_UnknownModule_IsAnErrorAndTouchesNothing()
        {
            string before = File.ReadAllText(ManifestManager.ManifestPath);

            GameLibInstallReport report = GameLibCli.InstallModules(new[] { "ads.unity" });

            Assert.AreEqual(GameLibInstallStatus.Error, report.Status);
            StringAssert.Contains("Unknown module 'ads.unity'", report.Errors.Single());
            Assert.IsEmpty(report.Changes);
            Assert.AreEqual(before, File.ReadAllText(ManifestManager.ManifestPath));
        }

        [Test]
        public void Install_UnknownOptionalSdk_IsAnErrorAndTouchesNothing()
        {
            string before = File.ReadAllText(ManifestManager.ManifestPath);

            GameLibInstallReport report = GameLibCli.InstallModules(new[] { "ads.admob" }, new[] { "admob-net" });

            Assert.AreEqual(GameLibInstallStatus.Error, report.Status);
            StringAssert.Contains("Unknown optional SDK 'admob-net'", report.Errors.Single());
            StringAssert.Contains("pangle", report.Errors.Single());
            Assert.AreEqual(before, File.ReadAllText(ManifestManager.ManifestPath));
        }

        [Test]
        public void AdMobMediationAdapters_ArePinnedOnTheGoogleScope()
        {
            GameLibModuleInfo admob = GameLibModuleCatalog.Find("ads.admob");
            SdkRequirement gma = admob.Sdks.Single(s => s.PackageName == "com.google.ads.mobile");
            Assert.That(admob.OptionalSdks, Is.Not.Empty);
            foreach (SdkRequirement sdk in admob.OptionalSdks)
            {
                StringAssert.StartsWith("com.google.ads.mobile.mediation.", sdk.PackageName);
                Assert.That(sdk.Location, Is.Not.Empty, sdk.PackageName);
                Assert.AreEqual(gma.RegistryUrl, sdk.RegistryUrl, "the GMA registry serves them");
                CollectionAssert.AreEqual(gma.RegistryScopes, sdk.RegistryScopes);
            }
            Assert.AreEqual("pangle", GameLibModuleCatalog.OptionalKey(admob.OptionalSdks.Single(s => s.DisplayName == "Pangle")));
        }

        [Test]
        public void Install_NoModule_IsAnError()
        {
            GameLibInstallReport report = GameLibCli.InstallModules(new[] { " ", "" });

            Assert.AreEqual(GameLibInstallStatus.Error, report.Status);
            StringAssert.Contains("No module given", report.Errors.Single());
        }

        [Test]
        public void Install_EmbeddedPackages_AreNotAddedAgain()
        {
            // This repository has every package embedded (not in the manifest's dependencies); "build" has no module asset.
            string before = File.ReadAllText(ManifestManager.ManifestPath);

            GameLibInstallReport report = GameLibCli.InstallModules(new[] { "build" });

            Assert.AreEqual(GameLibInstallStatus.Done, report.Status, string.Join("\n", report.Errors.Concat(report.Pending)));
            Assert.IsEmpty(report.Changes);
            Assert.AreEqual(before, File.ReadAllText(ManifestManager.ManifestPath));
        }

        [Test]
        public void Doctor_ListsEveryCatalogModule()
        {
            GameLibDoctorReport report = GameLibCli.BuildDoctorReport();

            CollectionAssert.AreEqual(GameLibModuleCatalog.All.Select(m => m.Id), report.Modules.Select(m => m.Id));
            Assert.IsTrue(report.Modules.Single(m => m.Id == "build").Installed);
            Assert.AreEqual(report.Issues.Count(i => i.Severity == "Error"), report.ErrorCount);
        }

        [Test]
        public void Manifest_HasScopedRegistry_IgnoresTrailingSlash()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "{\"dependencies\":{},\"scopedRegistries\":[{\"name\":\"a\",\"url\":\"https://unity.packages.applovin.com/\",\"scopes\":[\"com.applovin\"]}]}");
                var manifest = new ManifestManager(path);

                Assert.IsTrue(manifest.HasScopedRegistry("https://unity.packages.applovin.com"));
                Assert.IsFalse(manifest.HasScopedRegistry("https://package.openupm.com"));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
