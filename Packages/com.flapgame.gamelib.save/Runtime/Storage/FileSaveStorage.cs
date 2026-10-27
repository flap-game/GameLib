using System;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlapGame.GameLib.Save
{
    /// <summary>What <see cref="ISaveStorage.Read"/> found.</summary>
    public readonly struct SaveReadResult
    {
        public SaveReadResult(SaveLoadStatus status, string json, string source, string error)
        {
            Status = status;
            Json = json;
            Source = source;
            Error = error;
        }

        public SaveLoadStatus Status { get; }

        /// <summary>The save document, or null for <see cref="SaveLoadStatus.New"/> / <see cref="SaveLoadStatus.Corrupted"/> / <see cref="SaveLoadStatus.ReadFailed"/>.</summary>
        public string Json { get; }

        /// <summary>The file that was read, for logs.</summary>
        public string Source { get; }

        /// <summary>Why the main file was not used, or null.</summary>
        public string Error { get; }
    }

    /// <summary>Where the save document is kept. <see cref="Write"/> may be called from a background thread, never twice at once.</summary>
    public interface ISaveStorage
    {
        /// <summary>Human-readable location, for logs and the Editor menu.</summary>
        string Location { get; }

        SaveReadResult Read();

        /// <summary>Replaces the stored save with <paramref name="json"/>. Throws when it cannot.</summary>
        void Write(string json);

        /// <summary>Deletes every copy of the save.</summary>
        void Delete();
    }

    /// <summary>
    /// The save as one file, written so that a crash or power loss at any point leaves a readable copy:
    /// <list type="number">
    /// <item>write <c>&lt;file&gt;.tmp</c> and fsync it;</item>
    /// <item>the current file becomes <c>&lt;file&gt;.bak</c>;</item>
    /// <item><c>.tmp</c> becomes the file.</item>
    /// </list>
    /// Read tries the file, then <c>.tmp</c>, then <c>.bak</c>, each checked by <see cref="SaveCodec"/> and parsed. A damaged
    /// file is moved to <c>.corrupt-&lt;utc&gt;</c> (the newest <see cref="MaxCorruptCopies"/> are kept), so the next write
    /// does not rotate it over the good <c>.bak</c>. A file that exists but cannot
    /// be read (I/O error) blocks every write of the session, so a save that was never seen is never overwritten.
    /// </summary>
    public sealed class FileSaveStorage : ISaveStorage
    {
        public const int MaxCorruptCopies = 3;
        private const string CorruptSuffix = ".corrupt-";
        private const int ReadAttempts = 3;
        private const int RetryDelayMs = 20;
        private const string LogPrefix = "[Save]";

        private readonly SaveCodec codec;
        private readonly bool acceptIntegrityFailure;
        private volatile string readFailure;

        /// <param name="path">Absolute path, or a file name inside <c>Application.persistentDataPath</c>.</param>
        /// <param name="acceptIntegrityFailure">Read a plain file whose checksum fails (hand-edited) instead of treating it as damaged. Editor only, normally.</param>
        public FileSaveStorage(string path, SaveCodec codec, bool acceptIntegrityFailure = false)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("The save path must not be empty.", nameof(path));
            FilePath = Path.IsPathRooted(path) ? path : Path.Combine(Application.persistentDataPath, path);
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
            this.acceptIntegrityFailure = acceptIntegrityFailure;
        }

        public string FilePath { get; }

        public string TempPath => FilePath + ".tmp";

        public string BackupPath => FilePath + ".bak";

        public string Location => FilePath;

        /// <summary>Test seam: reads a file's bytes (I/O errors are simulated through it).</summary>
        internal Func<string, byte[]> ReadAllBytes = File.ReadAllBytes;

        public SaveReadResult Read()
        {
            readFailure = null;
            bool mainExists = File.Exists(FilePath);
            string mainError = null;
            if (mainExists)
            {
                byte[] bytes;
                try { bytes = ReadWithRetry(FilePath); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    readFailure = e.Message;
                    Debug.LogError($"{LogPrefix} Could not read '{FilePath}': {e.Message}. Nothing is written this session.");
                    return new SaveReadResult(SaveLoadStatus.ReadFailed, null, FilePath, e.Message);
                }
                if (TryDecode(bytes, out string json, out mainError))
                {
                    // A crash after .tmp was fsynced but before the renames leaves a complete newer write in .tmp.
                    if (TryReadNewerTemp(json, out string newer))
                    {
                        Debug.LogWarning($"{LogPrefix} The last write did not finish its rename; reading the newer '{TempPath}'.");
                        return new SaveReadResult(SaveLoadStatus.Loaded, newer, TempPath, null);
                    }
                    return new SaveReadResult(SaveLoadStatus.Loaded, json, FilePath, null);
                }
                Debug.LogWarning($"{LogPrefix} '{FilePath}' is damaged ({mainError}).");
            }

            foreach (string fallback in new[] { TempPath, BackupPath })
            {
                if (!File.Exists(fallback)) continue;
                byte[] bytes;
                try { bytes = ReadWithRetry(fallback); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Debug.LogWarning($"{LogPrefix} Could not read '{fallback}': {e.Message}.");
                    continue;
                }
                if (!TryDecode(bytes, out string json, out string error))
                {
                    Debug.LogWarning($"{LogPrefix} '{fallback}' is damaged too ({error}).");
                    continue;
                }
                if (mainExists) QuarantineMain();
                Debug.LogWarning($"{LogPrefix} Recovered the save from '{fallback}'.");
                return new SaveReadResult(SaveLoadStatus.RecoveredFromBackup, json, fallback, mainError ?? "the save file was missing");
            }

            if (!mainExists && !File.Exists(TempPath) && !File.Exists(BackupPath))
                return new SaveReadResult(SaveLoadStatus.New, null, FilePath, null);

            if (mainExists) QuarantineMain();
            Debug.LogError($"{LogPrefix} No readable copy of the save in '{Path.GetDirectoryName(FilePath)}'; starting fresh.");
            return new SaveReadResult(SaveLoadStatus.Corrupted, null, FilePath, mainError ?? "every copy is damaged");
        }

        public void Write(string json)
        {
            if (readFailure != null)
                throw new InvalidOperationException($"'{FilePath}' could not be read at startup ({readFailure}); it is not overwritten.");

            byte[] bytes = codec.Encode(json);
            string dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // fsync before the rename: without it a power loss can leave a zero-length file that the rename promotes.
            using (var fs = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }

            // Manual rotation instead of File.Replace (not reliable on every Mono/IL2CPP target). A crash between the two
            // moves leaves only .tmp (newest, checked) and .bak: Read tries .tmp first.
            if (File.Exists(FilePath))
            {
                if (File.Exists(BackupPath)) File.Delete(BackupPath);
                File.Move(FilePath, BackupPath);
            }
            File.Move(TempPath, FilePath);
        }

        public void Delete()
        {
            foreach (string file in new[] { FilePath, TempPath, BackupPath })
                if (File.Exists(file)) File.Delete(file);
            readFailure = null;
        }

        private bool TryDecode(byte[] bytes, out string json, out string error)
        {
            SaveDecodeStatus status = codec.Decode(bytes, out json);
            if (status == SaveDecodeStatus.IntegrityFailed && acceptIntegrityFailure && json != null)
            {
                Debug.LogWarning($"{LogPrefix} The save checksum does not match (hand-edited?); reading it anyway (Editor).");
                status = SaveDecodeStatus.Ok;
            }
            if (status != SaveDecodeStatus.Ok)
            {
                error = status == SaveDecodeStatus.IntegrityFailed ? "checksum mismatch" : "not a readable save";
                json = null;
                return false;
            }
            if (!IsSaveDocument(json, out error))
            {
                json = null;
                return false;
            }
            return true;
        }

        private bool TryReadNewerTemp(string mainJson, out string tempJson)
        {
            tempJson = null;
            if (!File.Exists(TempPath)) return false;
            try
            {
                if (!TryDecode(ReadWithRetry(TempPath), out string json, out _)) return false; // a write cut short: ignore
                if (SaveCountOf(json) <= SaveCountOf(mainJson)) return false;
                tempJson = json;
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static long SaveCountOf(string json)
        {
            try { return (Json.SaveJson.Parse(json)["meta"] as JObject)?.Value<long?>("saveCount") ?? 0; }
            catch (Exception) { return 0; }
        }

        internal static bool IsSaveDocument(string json, out string error)
        {
            try
            {
                if (Json.SaveJson.Parse(json) is JObject obj && obj["entries"] is JObject)
                {
                    error = null;
                    return true;
                }
                error = "not a GameLib save document";
            }
            catch (Exception e) { error = "invalid JSON: " + e.Message; }
            return false;
        }

        private byte[] ReadWithRetry(string path)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { return ReadAllBytes(path); }
                catch (IOException) when (attempt < ReadAttempts) { Thread.Sleep(RetryDelayMs); }
            }
        }

        // Moves the damaged file out of the way, so the next write does not rotate it over the good .bak.
        private void QuarantineMain()
        {
            KeepCorruptCopy(FilePath);
            try { File.Delete(FilePath); }
            catch (Exception e) { Debug.LogWarning($"{LogPrefix} Could not remove the damaged save: {e.Message}"); }
        }

        private void KeepCorruptCopy(string path)
        {
            try
            {
                string copy = path + CorruptSuffix + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                File.Copy(path, copy, overwrite: true);
                string dir = Path.GetDirectoryName(path);
                string pattern = Path.GetFileName(path) + CorruptSuffix + "*";
                foreach (string old in Directory.GetFiles(string.IsNullOrEmpty(dir) ? "." : dir, pattern)
                                                .OrderByDescending(f => f, StringComparer.Ordinal)
                                                .Skip(MaxCorruptCopies))
                    File.Delete(old);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Could not keep a copy of the damaged save: {e.Message}");
            }
        }
    }
}
