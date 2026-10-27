using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FlapGame.GameLib.Save.Tests
{
    public class SaveServiceTests
    {
        public sealed class Player
        {
            public int Level = 1;
            public int Coins;
            public List<string> Skins = new() { "default" };
            public Vector3 Spawn;
            public string Note;
        }

        public sealed class Settings
        {
            public bool Music = true;
        }

        /// <summary>In-memory storage: what was written is what the next service reads.</summary>
        private sealed class MemoryStorage : ISaveStorage
        {
            public string Stored;
            public int Writes;
            public bool Fail;

            public string Location => "memory";

            public SaveReadResult Read() => Stored == null
                ? new SaveReadResult(SaveLoadStatus.New, null, Location, null)
                : new SaveReadResult(SaveLoadStatus.Loaded, Stored, Location, null);

            public void Write(string json)
            {
                if (Fail) throw new System.IO.IOException("disk full");
                Stored = json;
                Interlocked.Increment(ref Writes);
            }

            public void Delete() => Stored = null;
        }

        private sealed class FakeLifecycle : IAppLifecycle
        {
            public bool IsPaused { get; private set; }
            public bool HasFocus => true;
            public event Action<bool> PauseChanged;
            public event Action<bool> FocusChanged;
            public event Action<AppResumeInfo> Resumed { add { } remove { } }
            public event Action Quitting;
            public IDisposable BeginExternalActivity(string reason) => Disposable.Create(() => { });
            public void MarkExternalActivity(string reason) { }

            public void Pause(bool paused)
            {
                IsPaused = paused;
                PauseChanged?.Invoke(paused);
            }

            public void LoseFocus() => FocusChanged?.Invoke(false);
            public void Quit() => Quitting?.Invoke();
        }

        private static readonly SaveSettings NoAutoSave = new() { AutoSaveDelaySeconds = 0f, PrettyPrintInEditor = false };

        private MemoryStorage storage;
        private double now;

        [SetUp]
        public void SetUp()
        {
            storage = new MemoryStorage();
            now = 0;
        }

        private SaveService Open(IAppLifecycle lifecycle = null) =>
            new(storage, storage.Read(), NoAutoSave, lifecycle, clock: () => now,
                utcNow: () => new DateTime(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc));

        [Test]
        public void Values_SurviveAReopen()
        {
            SaveService save = Open();
            save.Save("coins", 120);
            save.Save("name", "An");
            save.Save("when", "2026-10-27T10:00:00");
            Assert.That(save.IsDirty, Is.True);
            Assert.That(save.Flush(), Is.True);

            SaveService again = Open();
            Assert.That(again.LoadStatus, Is.EqualTo(SaveLoadStatus.Loaded));
            Assert.That(again.Load<int>("coins"), Is.EqualTo(120));
            Assert.That(again.Load<string>("name"), Is.EqualTo("An"));
            Assert.That(again.Load<string>("when"), Is.EqualTo("2026-10-27T10:00:00"), "a date-like string is not reformatted");
            Assert.That(again.Load("missing", 7), Is.EqualTo(7));
        }

        [Test]
        public void UnreadableValue_ReturnsTheDefault()
        {
            SaveService save = Open();
            save.Save("coins", "lots");
            Assert.That(save.Load("coins", 5), Is.EqualTo(5));
        }

        [Test]
        public void Scopes_PrefixKeys_AndListOnlyTheirOwn()
        {
            SaveService save = Open();
            ISaveService ads = save.ForScope("ads");
            ads.Save("muted", true);
            ads.ForScope("banner").Save("height", 50);
            save.Save("coins", 1);

            Assert.That(save.Load<bool>("muted"), Is.False);
            Assert.That(ads.Load<bool>("muted"), Is.True);
            Assert.That(ads.GetKeys(), Is.EqualTo(new[] { "muted" }));
            Assert.That(save.GetKeys(), Is.EqualTo(new[] { "coins" }));

            ads.DeleteAll();
            Assert.That(ads.HasKey("muted"), Is.False);
            Assert.That(ads.ForScope("banner").HasKey("height"), Is.False);
            Assert.That(save.HasKey("coins"), Is.True);
        }

        [Test]
        public void KeysWithTheSeparator_AreRejected()
        {
            SaveService save = Open();
            Assert.Throws<ArgumentException>(() => save.Save("ads.muted", true));
            Assert.Throws<ArgumentException>(() => save.ForScope(""));
        }

        [Test]
        public void Section_IsLive_AndSurvivesAReopen()
        {
            SaveService save = Open();
            ISaveSection<Player> player = save.Section<Player>("player");
            Assert.That(player.Data.Level, Is.EqualTo(1));
            player.Data.Level = 9;
            player.Data.Skins.Add("gold");
            player.Data.Spawn = new Vector3(1, 2, 3);
            player.MarkDirty();
            save.Flush();

            Player loaded = Open().Section<Player>("player").Data;
            Assert.That(loaded.Level, Is.EqualTo(9));
            Assert.That(loaded.Skins, Is.EqualTo(new[] { "default", "gold" }), "initialised lists are replaced, not appended to");
            Assert.That(loaded.Spawn, Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(storage.Stored, Does.Not.Contain("normalized"));
        }

        [Test]
        public void Section_SameKeyTwice_IsTheSameInstance_AndAnotherTypeThrows()
        {
            SaveService save = Open();
            Assert.That(save.Section<Player>("player"), Is.SameAs(save.Section<Player>("player")));
            Assert.Throws<InvalidOperationException>(() => save.Section<Settings>("player"));
            Assert.Throws<InvalidOperationException>(() => save.Section<Player>("player", version: 2));
            Assert.Throws<InvalidOperationException>(() => save.Save("player", 1));
        }

        [Test]
        public void Migrations_RunFromTheStoredVersion()
        {
            SaveService v1 = Open();
            ISaveSection<JObject> old = v1.Section<JObject>("player");
            old.Set(new JObject { ["lvl"] = 4, ["gold"] = 30 });
            v1.Flush();

            var ran = new List<int>();
            var migrations = new SaveMigrations()
                .Step(2, (JObject o) => { ran.Add(2); o["Level"] = o["lvl"]; o.Remove("lvl"); })
                .Step(3, (JObject o) => { ran.Add(3); o["Coins"] = o.Value<int>("gold") * 10; o.Remove("gold"); });
            Player player = Open().Section<Player>("player", version: 3, migrations).Data;

            Assert.That(ran, Is.EqualTo(new[] { 2, 3 }));
            Assert.That(player.Level, Is.EqualTo(4));
            Assert.That(player.Coins, Is.EqualTo(300));
        }

        [Test]
        public void Migrations_DoNotRunAgain_OnceWrittenAtTheNewVersion()
        {
            SaveService v1 = Open();
            v1.Section<Player>("player").Data.Level = 2;
            v1.Section<Player>("player").MarkDirty();
            v1.Flush();

            int runs = 0;
            var migrations = new SaveMigrations().Step(2, (JObject o) => runs++);
            SaveService v2 = Open();
            v2.Section<Player>("player", 2, migrations).MarkDirty();
            v2.Flush();
            Open().Section<Player>("player", 2, migrations);
            Assert.That(runs, Is.EqualTo(1));
        }

        [Test]
        public void BrokenSection_StartsFresh_KeepsTheDataInQuarantine_AndLeavesTheOthers()
        {
            SaveService first = Open();
            first.Save("coins", 5);
            first.Flush();
            JObject document = JObject.Parse(storage.Stored);
            document["entries"]["player"] = new JObject { ["v"] = 1, ["d"] = "not an object" };
            storage.Stored = document.ToString();

            LogAssert.ignoreFailingMessages = true;
            SaveService save = Open();
            ISaveSection<Player> player = save.Section<Player>("player");
            LogAssert.ignoreFailingMessages = false;

            Assert.That(player.LoadError, Is.Not.Null);
            Assert.That(player.Data.Level, Is.EqualTo(1));
            Assert.That(save.Load<int>("coins"), Is.EqualTo(5));
            save.Flush();
            Assert.That(JObject.Parse(storage.Stored)["quarantine"]["player"]["d"].Value<string>(), Is.EqualTo("not an object"));
        }

        [Test]
        public void DeleteAll_ResetsSections_AndRaisesReplaced()
        {
            SaveService save = Open();
            ISaveSection<Player> player = save.Section<Player>("player");
            player.Data.Level = 50;
            save.SetProgress(50);
            Player replaced = null;
            player.Replaced += p => replaced = p;

            save.DeleteAll();

            Assert.That(player.Data.Level, Is.EqualTo(1));
            Assert.That(replaced, Is.SameAs(player.Data));
            Assert.That(save.Metadata.Progress, Is.EqualTo(0));
        }

        [Test]
        public void NewerDocumentFormat_IsNeverOverwritten()
        {
            storage.Stored = "{\"format\":99,\"entries\":{}}";
            LogAssert.ignoreFailingMessages = true;
            SaveService save = Open();
            LogAssert.ignoreFailingMessages = false;

            Assert.That(save.LoadStatus, Is.EqualTo(SaveLoadStatus.ReadFailed));
            save.Save("coins", 1);
            Assert.That(save.Flush(), Is.False);
            Assert.That(storage.Stored, Is.EqualTo("{\"format\":99,\"entries\":{}}"));
            Assert.That(save.IsDirty, Is.False, "no autosave loop on a save that cannot be written");
        }

        [Test]
        public void FailedWrite_StaysDirty_AndRaisesFlushedFalse()
        {
            SaveService save = Open();
            var results = new List<bool>();
            save.Flushed += results.Add;
            storage.Fail = true;
            save.Save("coins", 1);

            LogAssert.ignoreFailingMessages = true;
            Assert.That(save.Flush(), Is.False);
            LogAssert.ignoreFailingMessages = false;
            Assert.That(save.IsDirty, Is.True);

            storage.Fail = false;
            Assert.That(save.Flush(), Is.True);
            Assert.That(results, Is.EqualTo(new[] { false, true }));
        }

        [Test]
        public async Task LateBackgroundWrite_NeverReplacesANewerOne()
        {
            SaveService save = Open();
            save.Save("coins", 1);
            Task<bool> background = save.FlushAsync();
            save.Save("coins", 2);
            save.Flush();
            await background;
            Assert.That(Open().Load<int>("coins"), Is.EqualTo(2));
        }

        [Test]
        public void Pause_FlushesAtOnce_AndPlaytimeSkipsTheBackground()
        {
            var lifecycle = new FakeLifecycle();
            SaveService save = Open(lifecycle);
            save.Save("coins", 1);
            now = 30;
            lifecycle.Pause(true);
            Assert.That(storage.Writes, Is.EqualTo(1));

            now = 1000; // in the background
            lifecycle.Pause(false);
            now = 1010;
            save.Save("coins", 2);
            lifecycle.LoseFocus();
            Assert.That(storage.Writes, Is.EqualTo(2));

            SaveMetadata meta = Open().Metadata;
            Assert.That(meta.PlaytimeSeconds, Is.EqualTo(40).Within(0.2));
            Assert.That(meta.SaveCount, Is.EqualTo(2));
            Assert.That(meta.LastSavedUtc, Is.EqualTo(new DateTime(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc)));
        }

        [Test]
        public void Quit_Flushes_AndCleanStateDoesNotWrite()
        {
            var lifecycle = new FakeLifecycle();
            Open(lifecycle);
            lifecycle.Quit();
            Assert.That(storage.Writes, Is.EqualTo(0));

            SaveService save = Open(lifecycle);
            save.Save("coins", 3);
            lifecycle.Quit();
            Assert.That(storage.Writes, Is.EqualTo(1));
        }

        [Test]
        public void Snapshot_MovesTheWholeSave_AndSwapsSectionData()
        {
            SaveService source = Open();
            source.Section<Player>("player").Data.Level = 30;
            source.SetProgress(30);
            SaveSnapshot snapshot = source.ExportSnapshot();
            Assert.That(snapshot.Metadata.Progress, Is.EqualTo(30));

            var otherStorage = new MemoryStorage();
            var target = new SaveService(otherStorage, otherStorage.Read(), NoAutoSave);
            ISaveSection<Player> player = target.Section<Player>("player");
            int replaced = 0;
            player.Replaced += _ => replaced++;

            Assert.That(target.ImportSnapshot(snapshot), Is.True);
            Assert.That(player.Data.Level, Is.EqualTo(30));
            Assert.That(replaced, Is.EqualTo(1));
            Assert.That(otherStorage.Stored, Is.Not.Null, "an import is written at once");

            LogAssert.ignoreFailingMessages = true;
            Assert.That(target.ImportSnapshot(new SaveSnapshot("{\"x\":1}", new SaveMetadata())), Is.False);
            LogAssert.ignoreFailingMessages = false;
            Assert.That(player.Data.Level, Is.EqualTo(30));
        }

        private sealed class FakeCloud : ICloudSaveBackend
        {
            public SaveSnapshot Remote;
            public SaveSnapshot Uploaded;
            public string Id => "fake";
            public Task<SaveSnapshot> DownloadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Remote);

            public Task UploadAsync(SaveSnapshot snapshot, CancellationToken cancellationToken = default)
            {
                Uploaded = snapshot;
                return Task.CompletedTask;
            }
        }

        [Test]
        public async Task Sync_KeepsTheCopyWithMoreProgress()
        {
            var cloudStorage = new MemoryStorage();
            var device = new SaveService(cloudStorage, cloudStorage.Read(), NoAutoSave);
            device.Section<Player>("player").Data.Level = 80;
            device.SetProgress(80);
            var cloud = new FakeCloud { Remote = device.ExportSnapshot() };

            SaveService save = Open();
            save.SetProgress(3);
            Assert.That(await SaveSync.SyncAsync(save, cloud), Is.EqualTo(SaveSyncResult.Downloaded));
            Assert.That(save.Section<Player>("player").Data.Level, Is.EqualTo(80));

            save.SetProgress(81);
            Assert.That(await SaveSync.SyncAsync(save, cloud), Is.EqualTo(SaveSyncResult.Uploaded));
            Assert.That(cloud.Uploaded.Metadata.Progress, Is.EqualTo(81));

            cloud.Remote = null;
            Assert.That(await SaveSync.SyncAsync(save, cloud), Is.EqualTo(SaveSyncResult.Uploaded), "no cloud copy yet");
        }

        [Test]
        public void PreferMoreProgress_ThenPlaytime_ThenTheLaterSave()
        {
            SaveSnapshot Snap(long progress, double playtime, int day) => new("{}", new SaveMetadata
            {
                Progress = progress, PlaytimeSeconds = playtime, LastSavedUtc = new DateTime(2026, 10, day, 0, 0, 0, DateTimeKind.Utc),
            });
            SaveSnapshot local = Snap(5, 100, 1);
            Assert.That(SaveSync.PreferMoreProgress(local, Snap(6, 0, 1)).Metadata.Progress, Is.EqualTo(6));
            Assert.That(SaveSync.PreferMoreProgress(local, Snap(5, 200, 1)).Metadata.PlaytimeSeconds, Is.EqualTo(200));
            Assert.That(SaveSync.PreferMoreProgress(local, Snap(5, 100, 2)).Metadata.LastSavedUtc.Day, Is.EqualTo(2));
            Assert.That(SaveSync.PreferMoreProgress(local, Snap(5, 100, 1)), Is.SameAs(local));
        }
    }
}
