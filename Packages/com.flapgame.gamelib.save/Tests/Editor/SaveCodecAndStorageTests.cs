using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FlapGame.GameLib.Save.Tests
{
    public class SaveCodecTests
    {
        private const string Json = "{\"format\":1,\"entries\":{}}";

        [Test]
        public void Plain_RoundTrips_AndStaysReadable()
        {
            var codec = new SaveCodec("secret", encrypt: false);
            byte[] bytes = codec.Encode(Json);
            Assert.That(Encoding.UTF8.GetString(bytes), Does.StartWith("GLSAVE 1 json ").And.EndWith(Json));
            Assert.That(codec.Decode(bytes, out string json), Is.EqualTo(SaveDecodeStatus.Ok));
            Assert.That(json, Is.EqualTo(Json));
        }

        [Test]
        public void Encrypted_RoundTrips_AndHidesThePayload()
        {
            var codec = new SaveCodec("secret", encrypt: true);
            byte[] bytes = codec.Encode(Json);
            Assert.That(Encoding.UTF8.GetString(bytes), Does.StartWith("GLSAVE 1 aes ").And.Not.Contains("entries"));
            Assert.That(codec.Decode(bytes, out string json), Is.EqualTo(SaveDecodeStatus.Ok));
            Assert.That(json, Is.EqualTo(Json));
        }

        [Test]
        public void EitherMode_ReadsTheOther_SoEncryptionCanBeSwitched()
        {
            byte[] plain = new SaveCodec("secret", false).Encode(Json);
            byte[] encrypted = new SaveCodec("secret", true).Encode(Json);
            Assert.That(new SaveCodec("secret", true).Decode(plain, out _), Is.EqualTo(SaveDecodeStatus.Ok));
            Assert.That(new SaveCodec("secret", false).Decode(encrypted, out _), Is.EqualTo(SaveDecodeStatus.Ok));
        }

        [Test]
        public void EditedPlainFile_FailsIntegrity_ButKeepsTheJson()
        {
            var codec = new SaveCodec("secret", false);
            byte[] edited = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(codec.Encode(Json)).Replace("{}", "{\"coins\":{\"v\":0,\"d\":999}}"));
            Assert.That(codec.Decode(edited, out string json), Is.EqualTo(SaveDecodeStatus.IntegrityFailed));
            Assert.That(json, Does.Contain("999"));
        }

        [Test]
        public void EditedOrForeignEncryptedFile_IsMalformed_AndNeverDecrypted()
        {
            byte[] bytes = new SaveCodec("secret", true).Encode(Json);
            bytes[bytes.Length - 3] ^= 0x01;
            Assert.That(new SaveCodec("secret", true).Decode(bytes, out string json), Is.EqualTo(SaveDecodeStatus.Malformed));
            Assert.That(json, Is.Null);
            Assert.That(new SaveCodec("other", true).Decode(new SaveCodec("secret", true).Encode(Json), out _), Is.EqualTo(SaveDecodeStatus.Malformed));
        }

        [Test]
        public void HeaderlessJson_IsReadWithAFailedChecksum_AndGarbageIsMalformed()
        {
            var codec = new SaveCodec("secret", false);
            Assert.That(codec.Decode(Encoding.UTF8.GetBytes(Json), out string json), Is.EqualTo(SaveDecodeStatus.IntegrityFailed));
            Assert.That(json, Is.EqualTo(Json));
            Assert.That(codec.Decode(new byte[] { 0, 1, 2, 3 }, out _), Is.EqualTo(SaveDecodeStatus.Malformed));
            Assert.That(codec.Decode(Array.Empty<byte>(), out _), Is.EqualTo(SaveDecodeStatus.Malformed));
        }
    }

    public class FileSaveStorageTests
    {
        private const string V1 = "{\"format\":1,\"entries\":{\"n\":{\"v\":0,\"d\":1}}}";
        private const string V2 = "{\"format\":1,\"entries\":{\"n\":{\"v\":0,\"d\":2}}}";

        private string dir;
        private string path;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "gamelib-save-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, "test.save");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        private FileSaveStorage Storage(bool acceptEdited = false) => new(path, new SaveCodec("secret", false), acceptEdited);

        [Test]
        public void NoFile_IsNew()
        {
            Assert.That(Storage().Read().Status, Is.EqualTo(SaveLoadStatus.New));
        }

        [Test]
        public void Write_KeepsThePreviousFileAsBackup()
        {
            FileSaveStorage storage = Storage();
            storage.Write(V1);
            storage.Write(V2);
            SaveReadResult read = storage.Read();
            Assert.That(read.Status, Is.EqualTo(SaveLoadStatus.Loaded));
            Assert.That(read.Json, Is.EqualTo(V2));
            Assert.That(File.Exists(storage.BackupPath), Is.True);
            Assert.That(File.Exists(storage.TempPath), Is.False);
        }

        [Test]
        public void DamagedFile_RecoversFromBackup_AndIsMovedAside()
        {
            FileSaveStorage storage = Storage();
            storage.Write(V1);
            storage.Write(V2);
            File.WriteAllText(path, "GLSAVE 1 json 00\n{trunc");

            LogAssert.ignoreFailingMessages = true;
            SaveReadResult read = storage.Read();
            LogAssert.ignoreFailingMessages = false;

            Assert.That(read.Status, Is.EqualTo(SaveLoadStatus.RecoveredFromBackup));
            Assert.That(read.Json, Is.EqualTo(V1));
            Assert.That(File.Exists(path), Is.False, "the damaged file must not be rotated over the good .bak");
            Assert.That(Directory.GetFiles(dir, "test.save.corrupt-*"), Has.Length.EqualTo(1));

            storage.Write(V2);
            Assert.That(new SaveCodec("secret", false).Decode(File.ReadAllBytes(storage.BackupPath), out string backup), Is.EqualTo(SaveDecodeStatus.Ok));
            Assert.That(backup, Is.EqualTo(V1), "the good backup survives the next write");
        }

        [Test]
        public void CrashBetweenTheRenames_ReadsTheNewerTempFile()
        {
            FileSaveStorage storage = Storage();
            storage.Write(V1);
            // State after "file -> .bak" but before ".tmp -> file".
            File.Move(path, storage.BackupPath);
            File.WriteAllBytes(storage.TempPath, new SaveCodec("secret", false).Encode(V2));

            LogAssert.ignoreFailingMessages = true;
            SaveReadResult read = storage.Read();
            LogAssert.ignoreFailingMessages = false;

            Assert.That(read.Status, Is.EqualTo(SaveLoadStatus.RecoveredFromBackup));
            Assert.That(read.Json, Is.EqualTo(V2));
        }

        [Test]
        public void EveryCopyDamaged_IsCorrupted()
        {
            File.WriteAllText(path, "garbage");
            File.WriteAllText(path + ".bak", "garbage");
            LogAssert.ignoreFailingMessages = true;
            SaveReadResult read = Storage().Read();
            LogAssert.ignoreFailingMessages = false;
            Assert.That(read.Status, Is.EqualTo(SaveLoadStatus.Corrupted));
            Assert.That(read.Json, Is.Null);
        }

        [Test]
        public void HandEditedFile_IsDamaged_UnlessAccepted()
        {
            Storage().Write(V1);
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"d\":1", "\"d\":5"));

            LogAssert.ignoreFailingMessages = true;
            Assert.That(Storage(acceptEdited: true).Read().Json, Does.Contain("\"d\":5"));
            Assert.That(Storage().Read().Status, Is.EqualTo(SaveLoadStatus.Corrupted));
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void ReadError_BlocksEveryWrite_SoTheUnseenSaveSurvives()
        {
            Storage().Write(V1);
            byte[] before = File.ReadAllBytes(path);
            FileSaveStorage storage = Storage();
            storage.ReadAllBytes = _ => throw new IOException("disk busy");

            LogAssert.ignoreFailingMessages = true;
            Assert.That(storage.Read().Status, Is.EqualTo(SaveLoadStatus.ReadFailed));
            LogAssert.ignoreFailingMessages = false;

            Assert.Throws<InvalidOperationException>(() => storage.Write(V2));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [Test]
        public void CorruptCopies_AreCapped()
        {
            for (int i = 0; i < FileSaveStorage.MaxCorruptCopies + 2; i++)
                File.WriteAllText(path + ".corrupt-2026010100000" + i, "old");
            File.WriteAllText(path, "garbage");
            LogAssert.ignoreFailingMessages = true;
            Storage().Read();
            LogAssert.ignoreFailingMessages = false;
            Assert.That(Directory.GetFiles(dir, "test.save.corrupt-*").Length, Is.EqualTo(FileSaveStorage.MaxCorruptCopies));
            Assert.That(Directory.GetFiles(dir, "test.save.corrupt-*").Select(Path.GetFileName),
                        Has.None.EqualTo("test.save.corrupt-20260101000000"), "the oldest go first");
        }

        [Test]
        public void Delete_RemovesEveryCopy()
        {
            FileSaveStorage storage = Storage();
            storage.Write(V1);
            storage.Write(V2);
            storage.Delete();
            Assert.That(storage.Read().Status, Is.EqualTo(SaveLoadStatus.New));
        }
    }
}
