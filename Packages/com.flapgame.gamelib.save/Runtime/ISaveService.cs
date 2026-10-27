using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace FlapGame.GameLib.Save
{
    /// <summary>
    /// The game's save data: one file, kept in memory, written in the background a few seconds after a change and at
    /// once when the app goes to the background. Two ways to store data, side by side:
    /// <list type="bullet">
    /// <item><b>Sections</b> (<see cref="Section{T}"/>): one object per game system (player, inventory, settings), with its
    /// own schema version and migrations. Mutate <see cref="ISaveSection{T}.Data"/>, then <see cref="ISaveSection{T}.MarkDirty"/>.</item>
    /// <item><b>Values</b> (<see cref="Save{T}"/> / <see cref="Load{T}"/>): small typed values by key, stored as a copy.</item>
    /// </list>
    /// Keys and scope names must not be empty and must not contain '.'; nest with <see cref="ForScope"/>.
    /// Call from the main thread only.
    /// </summary>
    public interface ISaveService
    {
        /// <summary>What the startup load found (new install, loaded, recovered from the backup, ...).</summary>
        SaveLoadStatus LoadStatus { get; }

        /// <summary>Save counters and times: for a "last saved" label and for choosing between two copies (cloud).</summary>
        SaveMetadata Metadata { get; }

        /// <summary>True when changes are waiting to be written.</summary>
        bool IsDirty { get; }

        /// <summary>Raised on the main thread after each write: true = on disk, false = the write failed (kept dirty, retried).</summary>
        event Action<bool> Flushed;

        /// <summary>
        /// The section stored under <paramref name="key"/>, loaded and migrated on the first call; later calls return the same
        /// instance. A section that cannot be read starts from <c>new T()</c>; its stored data is kept aside in the file
        /// and <see cref="ISaveSection{T}.LoadError"/> says why.
        /// </summary>
        /// <param name="version">Current schema version of <typeparamref name="T"/>, 1 or more. Raise it when the shape changes.</param>
        /// <param name="migrations">Steps from older versions, run in order on the stored JSON before it is read.</param>
        ISaveSection<T> Section<T>(string key, int version = 1, SaveMigrations migrations = null) where T : class, new();

        /// <summary>Stores a copy of <paramref name="value"/> under <paramref name="key"/> and marks the save dirty.</summary>
        void Save<T>(string key, T value);

        /// <summary>The value under <paramref name="key"/>, or <paramref name="defaultValue"/> when missing or unreadable.</summary>
        T Load<T>(string key, T defaultValue = default);

        bool HasKey(string key);

        void Delete(string key);

        /// <summary>The keys of this scope, without its prefix and without the keys of nested scopes.</summary>
        IReadOnlyCollection<string> GetKeys();

        /// <summary>A view whose keys are prefixed with "<paramref name="scope"/>.": one scope per module avoids collisions.</summary>
        ISaveService ForScope(string scope);

        /// <summary>
        /// Deletes every key of this scope and of its nested scopes; on the root, the whole save (bound sections go back to
        /// <c>new T()</c>). Written at the next flush.
        /// </summary>
        void DeleteAll();

        /// <summary>Marks the save dirty: the autosave writes it after <see cref="SaveSettings.AutoSaveDelaySeconds"/>.</summary>
        void MarkDirty();

        /// <summary>Game progress (highest level, ...) kept in <see cref="Metadata"/>; a cloud conflict keeps the copy with more.</summary>
        void SetProgress(long progress);

        /// <summary>Writes now on the main thread and returns when the file is on disk. For pause / quit / a checkpoint.</summary>
        bool Flush();

        /// <summary>Writes in the background. Completes with false when the write failed.</summary>
        Task<bool> FlushAsync();

        /// <summary>The whole save as JSON plus its metadata: what a cloud backend uploads.</summary>
        SaveSnapshot ExportSnapshot();

        /// <summary>
        /// Replaces the whole save with <paramref name="snapshot"/> (a cloud download) and writes it. Bound sections get
        /// new <see cref="ISaveSection{T}.Data"/> objects and raise <see cref="ISaveSection{T}.Replaced"/>.
        /// </summary>
        /// <returns>False when the snapshot is not a GameLib save, or the save cannot be written this session
        /// (<see cref="SaveLoadStatus.ReadFailed"/>); nothing changes then.</returns>
        bool ImportSnapshot(SaveSnapshot snapshot);
    }

    /// <summary>One game system's saved object. Read <see cref="Data"/> each time instead of caching it: an import or a reset swaps it.</summary>
    public interface ISaveSection<T> where T : class
    {
        string Key { get; }

        /// <summary>The schema version this build writes.</summary>
        int Version { get; }

        /// <summary>The live object. Change it in place, then call <see cref="MarkDirty"/>.</summary>
        T Data { get; }

        /// <summary>Null when the stored data was read (or there was none); otherwise why it was not, and the section started fresh.</summary>
        string LoadError { get; }

        /// <summary>Raised after <see cref="Data"/> was swapped (<see cref="Set"/>, <see cref="Reset"/>, an import, a delete).</summary>
        event Action<T> Replaced;

        void MarkDirty();

        /// <summary>Replaces <see cref="Data"/> and marks the save dirty.</summary>
        void Set(T value);

        /// <summary>Replaces <see cref="Data"/> with <c>new T()</c> and marks the save dirty.</summary>
        void Reset();
    }

    public enum SaveLoadStatus
    {
        /// <summary>No save on disk: first launch (or the save was deleted).</summary>
        New = 0,
        /// <summary>The save was read.</summary>
        Loaded = 1,
        /// <summary>The save was damaged; the last good copy (.tmp or .bak) was read instead. The damaged file is kept as .corrupt-*.</summary>
        RecoveredFromBackup = 2,
        /// <summary>Every copy was damaged: the game starts fresh. The damaged file is kept as .corrupt-*.</summary>
        Corrupted = 3,
        /// <summary>The file exists but could not be read (I/O error). Nothing is written this session, so it is never overwritten.</summary>
        ReadFailed = 4,
    }

    /// <summary>Counters and times of a save, written with it.</summary>
    public sealed class SaveMetadata
    {
        /// <summary>Number of writes since the save was created.</summary>
        public long SaveCount { get; set; }

        public DateTime FirstSavedUtc { get; set; }

        public DateTime LastSavedUtc { get; set; }

        /// <summary>Time in the foreground since the save was created, in seconds.</summary>
        public double PlaytimeSeconds { get; set; }

        /// <summary><c>Application.version</c> of the build that wrote it.</summary>
        public string AppVersion { get; set; }

        /// <summary>Set by the game (<see cref="ISaveService.SetProgress"/>).</summary>
        public long Progress { get; set; }

        public SaveMetadata Clone() => (SaveMetadata)MemberwiseClone();
    }

    /// <summary>Migration steps of one section, applied to its stored JSON from the stored version up to the current one.</summary>
    public sealed class SaveMigrations
    {
        private readonly SortedDictionary<int, Func<JToken, JToken>> steps = new();

        /// <summary>Step that turns version <c>toVersion - 1</c> data into <paramref name="toVersion"/> data; returns the new JSON.</summary>
        public SaveMigrations Step(int toVersion, Func<JToken, JToken> migrate)
        {
            if (toVersion < 2) throw new ArgumentOutOfRangeException(nameof(toVersion), "The first migration goes to version 2.");
            if (steps.ContainsKey(toVersion)) throw new InvalidOperationException($"A step to version {toVersion} is already registered.");
            steps[toVersion] = migrate ?? throw new ArgumentNullException(nameof(migrate));
            return this;
        }

        /// <summary>Step that edits an object section in place (rename a field, set a default, ...).</summary>
        public SaveMigrations Step(int toVersion, Action<JObject> migrate)
        {
            if (migrate == null) throw new ArgumentNullException(nameof(migrate));
            return Step(toVersion, token =>
            {
                if (token is not JObject obj)
                    throw new InvalidOperationException($"Step to version {toVersion} expects a JSON object, found {token?.Type.ToString() ?? "null"}.");
                migrate(obj);
                return obj;
            });
        }

        internal JToken Run(JToken data, int fromVersion, int toVersion)
        {
            foreach (KeyValuePair<int, Func<JToken, JToken>> step in steps)
            {
                if (step.Key <= fromVersion || step.Key > toVersion) continue;
                data = step.Value(data);
            }
            return data;
        }
    }
}
