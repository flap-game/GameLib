using System.Linq;
using FlapGame.GameLib.Editor;
using NUnit.Framework;

namespace FlapGame.GameLib.Tests
{
    public class FirebaseVersionCheckTests
    {
        [Test]
        public void SameSupportedVersion_IsFine()
        {
            Assert.IsEmpty(GameLibProject.CheckFirebaseVersions(new[]
            {
                ("com.google.firebase.app", "13.16.0"),
                ("com.google.firebase.analytics", "13.16.0"),
                ("com.flapgame.gamelib.core", "0.8.2"),
            }));
            Assert.IsEmpty(GameLibProject.CheckFirebaseVersions(new[] { ("com.google.firebase.app", "14.1.0") }));
            Assert.IsEmpty(GameLibProject.CheckFirebaseVersions(new[] { ("com.unity.purchasing", "5.4.4") }), "no Firebase: nothing to say");
        }

        [Test]
        public void MixedVersions_AreAnError()
        {
            GameLibIssue issue = GameLibProject.CheckFirebaseVersions(new[]
            {
                ("com.google.firebase.app", "14.1.0"),
                ("com.google.firebase.crashlytics", "13.16.0"),
            }).Single();
            Assert.AreEqual(GameLibIssueSeverity.Error, issue.Severity);
            StringAssert.Contains("com.google.firebase.crashlytics 13.16.0", issue.Message);
        }

        [Test]
        public void UncheckedMajor_IsAWarning()
        {
            GameLibIssue issue = GameLibProject.CheckFirebaseVersions(new[] { ("com.google.firebase.app", "12.10.1") }).Single();
            Assert.AreEqual(GameLibIssueSeverity.Warning, issue.Severity);
            StringAssert.Contains("13.x and 14.x", issue.Message);
        }

        [Test]
        public void NewInstalls_UseTheGitMirror_AtTheDefaultVersion()
        {
            SdkRequirement sdk = GameLibModuleCatalog.Find("analytics.firebase").Sdks.Single();
            Assert.AreEqual(SdkSource.Git, sdk.Source);
            Assert.AreEqual("https://github.com/GameWorkstore/com.google.firebase.analytics.git#" + GameLibModuleCatalog.FirebaseVersion, sdk.Location);
            Assert.That(GameLibModuleCatalog.SupportedFirebaseMajors, Does.Contain(int.Parse(GameLibModuleCatalog.FirebaseVersion.Split('.')[0])));
        }
    }
}
