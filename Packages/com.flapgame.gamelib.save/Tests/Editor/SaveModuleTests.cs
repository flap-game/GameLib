using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FlapGame.GameLib.Save.Tests
{
    /// <summary>The real pieces together: SaveModule, the registry, FileSaveStorage on disk, the codec.</summary>
    public class SaveModuleTests
    {
        public sealed class Player
        {
            public int Level = 1;
        }

        private sealed class FakeAnalytics : IAnalytics
        {
            public readonly List<AnalyticsEvent> Events = new();
            public void Track(AnalyticsEvent analyticsEvent) => Events.Add(analyticsEvent);
            public void SetUserProperty(string name, string value) { }
            public void SetUserId(string userId) { }
            public void TrackAdRevenue(AdRevenue revenue) { }
            public void TrackPurchase(PurchaseRecord purchase) { }
        }

        private sealed class FakeCrash : ICrashReporter
        {
            public readonly List<Exception> Recorded = new();
            public void Log(string message) { }
            public void RecordException(Exception exception) => Recorded.Add(exception);
            public void SetCustomKey(string key, string value) { }
        }

        private string dir;
        private string path;
        private readonly List<ServiceRegistry> registries = new();

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "gamelib-save-module-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, "game.save");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (ServiceRegistry registry in registries) registry.Dispose();
            registries.Clear();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        private async Task<(ISaveService Save, FakeAnalytics Analytics, FakeCrash Crash)> InstallAsync(bool encrypt = false)
        {
            var module = ScriptableObject.CreateInstance<SaveModule>();
            module.Settings.FileName = path; // a rooted path is used as-is
            module.Settings.Encrypt = encrypt;
            module.Settings.Secret = "test-secret";
            module.Settings.AutoSaveDelaySeconds = 0f;
            module.Settings.AcceptEditedSaveInEditor = false;

            var registry = new ServiceRegistry();
            registries.Add(registry);
            var analytics = new FakeAnalytics();
            var crash = new FakeCrash();
            registry.Register<IAnalytics>(analytics);
            registry.Register<ICrashReporter>(crash);
            await module.InstallAsync(registry);
            UnityEngine.Object.DestroyImmediate(module);
            return (registry.Resolve<ISaveService>(), analytics, crash);
        }

        [Test]
        public async Task Install_RegistersTheSave_AndItSurvivesARestart()
        {
            (ISaveService save, FakeAnalytics analytics, _) = await InstallAsync();
            Assert.That(save.LoadStatus, Is.EqualTo(SaveLoadStatus.New));
            save.Section<Player>("player").Data.Level = 12;
            save.Section<Player>("player").MarkDirty();
            Assert.That(save.Flush(), Is.True);
            Assert.That(File.ReadAllText(path), Does.StartWith("GLSAVE 1 json "));

            (ISaveService again, _, _) = await InstallAsync();
            Assert.That(again.LoadStatus, Is.EqualTo(SaveLoadStatus.Loaded));
            Assert.That(again.Section<Player>("player").Data.Level, Is.EqualTo(12));
            Assert.That(analytics.Events, Is.Empty, "a clean load reports nothing");
        }

        [Test]
        public async Task Encrypted_OnDisk_AndReadBack()
        {
            (ISaveService save, _, _) = await InstallAsync(encrypt: true);
            save.Save("secretCoins", 4242);
            save.Flush();
            string disk = File.ReadAllText(path);
            Assert.That(disk, Does.StartWith("GLSAVE 1 aes ").And.Not.Contains("4242").And.Not.Contains("secretCoins"));

            (ISaveService again, _, _) = await InstallAsync(encrypt: false);
            Assert.That(again.Load<int>("secretCoins"), Is.EqualTo(4242), "turning encryption off keeps the save");
        }

        [Test]
        public async Task DamagedSave_IsRecovered_AndReported()
        {
            (ISaveService save, _, _) = await InstallAsync();
            save.Save("coins", 1);
            save.Flush();
            save.Save("coins", 2);
            save.Flush();
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("GLSAVE 1 json 00\n{\"form"));

            LogAssert.ignoreFailingMessages = true;
            (ISaveService again, FakeAnalytics analytics, FakeCrash crash) = await InstallAsync();
            LogAssert.ignoreFailingMessages = false;

            Assert.That(again.LoadStatus, Is.EqualTo(SaveLoadStatus.RecoveredFromBackup));
            Assert.That(again.Load<int>("coins"), Is.EqualTo(1), "the previous write, from .bak");
            Assert.That(analytics.Events, Has.Count.EqualTo(1));
            Assert.That(analytics.Events[0].Name, Is.EqualTo(SaveModule.LoadIssueEvent));
            Assert.That(analytics.Events[0].Parameters["status"], Is.EqualTo("RecoveredFromBackup"));
            Assert.That(crash.Recorded, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task UnfinishedRename_ReadsTheNewerTempFile_InsteadOfTheOlderMain()
        {
            (ISaveService save, _, _) = await InstallAsync();
            save.Save("coins", 1);
            save.Flush();
            byte[] older = File.ReadAllBytes(path);
            save.Save("coins", 2);
            save.Flush();
            // Crash after .tmp was fsynced, before the renames: the main file is still the older write.
            File.Copy(path, path + ".tmp", overwrite: true);
            File.WriteAllBytes(path, older);

            LogAssert.ignoreFailingMessages = true;
            (ISaveService again, _, _) = await InstallAsync();
            LogAssert.ignoreFailingMessages = false;
            Assert.That(again.Load<int>("coins"), Is.EqualTo(2));
        }

        [Test]
        public async Task StaleTempFile_DoesNotWinOverANewerMain()
        {
            (ISaveService save, _, _) = await InstallAsync();
            save.Save("coins", 1);
            save.Flush();
            File.Copy(path, path + ".tmp", overwrite: true);
            save.Save("coins", 2);
            save.Flush();
            File.Copy(path + ".bak", path + ".tmp", overwrite: true);

            (ISaveService again, _, _) = await InstallAsync();
            Assert.That(again.Load<int>("coins"), Is.EqualTo(2));
        }

        [Test]
        public async Task SaveWrittenByANewerBuild_IsLeftAlone_AndImportIsRefused()
        {
            (ISaveService save, _, _) = await InstallAsync();
            save.Save("coins", 1);
            save.Flush();
            SaveSnapshot snapshot = save.ExportSnapshot();
            byte[] newer = new SaveCodec("test-secret", false).Encode("{\"format\":2,\"entries\":{}}");
            File.WriteAllBytes(path, newer);

            LogAssert.ignoreFailingMessages = true;
            (ISaveService again, _, _) = await InstallAsync();
            again.Save("coins", 9);
            Assert.That(again.Flush(), Is.False);
            Assert.That(again.ImportSnapshot(snapshot), Is.False);
            LogAssert.ignoreFailingMessages = false;

            Assert.That(again.LoadStatus, Is.EqualTo(SaveLoadStatus.ReadFailed));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(newer));
        }

        [Test]
        public void Validate_FlagsABadFileName()
        {
            var module = ScriptableObject.CreateInstance<SaveModule>();
            try
            {
                var issues = new List<GameLibIssue>();
                module.Settings.FileName = "a/b:c";
                module.Validate(issues);
                Assert.That(issues, Has.Some.Matches<GameLibIssue>(i => i.Severity == GameLibIssueSeverity.Error));

                issues.Clear();
                module.Settings.FileName = "gamelib.save";
                module.Validate(issues);
                Assert.That(issues, Has.None.Matches<GameLibIssue>(i => i.Severity == GameLibIssueSeverity.Error));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(module);
            }
        }
    }
}
