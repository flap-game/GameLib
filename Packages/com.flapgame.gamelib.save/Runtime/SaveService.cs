using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlapGame.GameLib.Save.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlapGame.GameLib.Save
{
    /// <summary>
    /// <see cref="ISaveService"/> over an <see cref="ISaveStorage"/>. The document stays in memory; a write serializes it on
    /// the main thread and hands the bytes to the storage (a background thread for the autosave and
    /// <see cref="FlushAsync"/>, the main thread for <see cref="Flush"/>). Writes are numbered, so an older autosave that
    /// finishes late never replaces a newer pause flush.
    /// <para>On disk:</para>
    /// <code>
    /// {"format":1,"meta":{...},"entries":{"player":{"v":3,"d":{...}},"ads.muted":{"v":0,"d":true}},"quarantine":{...}}
    /// </code>
    /// <c>quarantine</c> keeps the last stored data of a section that could not be read, for support to recover by hand.
    /// </summary>
    public sealed class SaveService : ISaveService, IDisposable
    {
        public const char ScopeSeparator = '.';
        /// <summary>Document layout version. A file with a higher one was written by a newer GameLib and is never overwritten.</summary>
        public const int DocumentFormat = 1;

        private const string LogPrefix = "[Save]";

        private readonly ISaveStorage storage;
        private readonly SaveSettings settings;
        private readonly IAppLifecycle lifecycle;
        private readonly Func<double> clock;
        private readonly Func<DateTime> utcNow;
        private readonly JsonSerializer serializer;

        private Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ISectionBinding> sections = new(StringComparer.Ordinal);
        private JObject quarantine = new();
        private SaveMetadata metadata = new();

        private readonly object writeGate = new();
        private long nextWriteId;
        private long writtenId;
        private bool writesBlocked;
        private bool autoSaveScheduled;
        private bool paused;
        private double playtimeMark;
        private bool disposed;

        /// <param name="loaded">What <see cref="ISaveStorage.Read"/> returned for <paramref name="storage"/>.</param>
        /// <param name="lifecycle">Flushes on pause / focus loss / quit and stops the playtime clock in the background. Optional.</param>
        /// <param name="clock">Seconds since start, for playtime. Null = <see cref="Time.realtimeSinceStartupAsDouble"/>.</param>
        /// <param name="utcNow">Null = <see cref="DateTime.UtcNow"/>.</param>
        /// <param name="serializerSettings">Null = <see cref="SaveJson.DefaultSettings"/>.</param>
        public SaveService(ISaveStorage storage, SaveReadResult loaded, SaveSettings settings = null, IAppLifecycle lifecycle = null,
                           Func<double> clock = null, Func<DateTime> utcNow = null, JsonSerializerSettings serializerSettings = null)
        {
            this.storage   = storage ?? throw new ArgumentNullException(nameof(storage));
            this.settings  = settings ?? new SaveSettings();
            this.lifecycle = lifecycle;
            this.clock     = clock ?? (() => Time.realtimeSinceStartupAsDouble);
            this.utcNow    = utcNow ?? (() => DateTime.UtcNow);
            serializer     = JsonSerializer.Create(serializerSettings ?? SaveJson.DefaultSettings);
            playtimeMark   = this.clock();

            LoadStatus = loaded.Status;
            LoadError  = loaded.Error;
            if (loaded.Json != null && !TryReadDocument(loaded.Json, out string error))
            {
                // Storage checked the layout; only a newer document format ends up here.
                LoadStatus = SaveLoadStatus.ReadFailed;
                LoadError  = error;
            }
            writesBlocked = LoadStatus == SaveLoadStatus.ReadFailed;
            if (writesBlocked)
                Debug.LogError($"{LogPrefix} {LoadError}. The save is not written this session, so it is not lost.");
            else
                Debug.Log($"{LogPrefix} {LoadStatus} '{loaded.Source}' ({entries.Count} keys).");

            if (lifecycle != null)
            {
                lifecycle.PauseChanged += OnPauseChanged;
                lifecycle.FocusChanged += OnFocusChanged;
                lifecycle.Quitting     += OnQuitting;
            }
        }

        /// <summary>Reads <paramref name="storage"/> and opens the save on it.</summary>
        public static SaveService Open(ISaveStorage storage, SaveSettings settings = null, IAppLifecycle lifecycle = null) =>
            new(storage, storage.Read(), settings, lifecycle);

        public SaveLoadStatus LoadStatus { get; }

        /// <summary>Why the startup load was not a clean <see cref="SaveLoadStatus.Loaded"/>, or null.</summary>
        public string LoadError { get; }

        public string Location => storage.Location;

        public SaveMetadata Metadata
        {
            get
            {
                SaveMetadata copy = metadata.Clone();
                copy.PlaytimeSeconds += UncountedPlaytime();
                return copy;
            }
        }

        public bool IsDirty { get; private set; }

        public event Action<bool> Flushed;

        // ------------------------------------------------------------------ ISaveService (root = empty prefix)

        public ISaveSection<T> Section<T>(string key, int version = 1, SaveMigrations migrations = null) where T : class, new() =>
            SectionAt<T>(Scoped(string.Empty, key), version, migrations);

        public void Save<T>(string key, T value) => SaveAt(Scoped(string.Empty, key), value);

        public T Load<T>(string key, T defaultValue = default) => LoadAt(Scoped(string.Empty, key), defaultValue);

        public bool HasKey(string key) => HasKeyAt(Scoped(string.Empty, key));

        public void Delete(string key) => DeleteAt(Scoped(string.Empty, key));

        public IReadOnlyCollection<string> GetKeys() => KeysIn(string.Empty);

        public ISaveService ForScope(string scope) => new ScopedSave(this, Scoped(string.Empty, scope));

        public void DeleteAll() => DeleteIn(string.Empty);

        public void MarkDirty()
        {
            if (disposed) return;
            IsDirty = true;
            ScheduleAutoSave();
        }

        public void SetProgress(long progress)
        {
            if (metadata.Progress == progress) return;
            metadata.Progress = progress;
            MarkDirty();
        }

        public bool Flush()
        {
            if (!IsDirty) return true;
            if (!TryBeginWrite(out string json, out long id)) return false;
            bool ok = WriteNumbered(json, id);
            EndWrite(ok);
            return ok;
        }

        public async Task<bool> FlushAsync()
        {
            if (!IsDirty) return true;
            if (!TryBeginWrite(out string json, out long id)) return false;
            bool ok;
#if UNITY_WEBGL && !UNITY_EDITOR
            ok = WriteNumbered(json, id); // no threads
            await Task.CompletedTask;
#else
            ok = await Task.Run(() => WriteNumbered(json, id));
#endif
            EndWrite(ok);
            return ok;
        }

        public SaveSnapshot ExportSnapshot()
        {
            CaptureSections();
            SaveMetadata meta = Metadata;
            return new SaveSnapshot(BuildDocument(meta).ToString(Formatting.None), meta);
        }

        public bool ImportSnapshot(SaveSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (writesBlocked)
            {
                // It could not be written, and the next launch would bring the old save back.
                Debug.LogWarning($"{LogPrefix} Import refused: the save cannot be written this session ({LoadError}).");
                return false;
            }
            if (!FileSaveStorage.IsSaveDocument(snapshot.Json, out string error))
            {
                Debug.LogWarning($"{LogPrefix} Import refused: {error}.");
                return false;
            }
            if (!TryReadDocument(snapshot.Json, out error))
            {
                Debug.LogWarning($"{LogPrefix} Import refused: {error}.");
                return false;
            }
            playtimeMark = clock();
            foreach (ISectionBinding section in sections.Values) section.Reload();
            IsDirty = true;
            Flush();
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            if (lifecycle != null)
            {
                lifecycle.PauseChanged -= OnPauseChanged;
                lifecycle.FocusChanged -= OnFocusChanged;
                lifecycle.Quitting     -= OnQuitting;
            }
            Flush();
            disposed = true;
        }

        // ------------------------------------------------------------------ keyed access (full keys)

        internal ISaveSection<T> SectionAt<T>(string fullKey, int version, SaveMigrations migrations) where T : class, new()
        {
            if (version < 1) throw new ArgumentOutOfRangeException(nameof(version), "A section version is 1 or more.");
            if (sections.TryGetValue(fullKey, out ISectionBinding bound))
            {
                if (bound is not SectionBinding<T> same)
                    throw new InvalidOperationException($"'{fullKey}' is already a section of {bound.DataType.Name}, not {typeof(T).Name}.");
                if (same.Version != version)
                    throw new InvalidOperationException($"'{fullKey}' is already open at version {same.Version}, not {version}.");
                return same;
            }
            var section = new SectionBinding<T>(this, fullKey, version, migrations);
            sections[fullKey] = section;
            section.Reload();
            return section;
        }

        internal void SaveAt<T>(string fullKey, T value)
        {
            ThrowIfSection(fullKey);
            entries[fullKey] = new Entry(0, ToToken(value));
            MarkDirty();
        }

        internal T LoadAt<T>(string fullKey, T defaultValue)
        {
            ThrowIfSection(fullKey);
            if (!entries.TryGetValue(fullKey, out Entry entry)) return defaultValue;
            try
            {
                T value = entry.Data.ToObject<T>(serializer);
                return value == null && defaultValue != null ? defaultValue : value;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} '{fullKey}' could not be read as {typeof(T).Name}: {e.Message}. Using the default.");
                return defaultValue;
            }
        }

        internal bool HasKeyAt(string fullKey) => entries.ContainsKey(fullKey) || sections.ContainsKey(fullKey);

        internal void DeleteAt(string fullKey)
        {
            bool removed = entries.Remove(fullKey);
            if (sections.TryGetValue(fullKey, out ISectionBinding section))
            {
                section.Reload();
                removed = true;
            }
            if (removed) MarkDirty();
        }

        internal IReadOnlyCollection<string> KeysIn(string prefix)
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string key in entries.Keys.Concat(sections.Keys))
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string rest = key.Substring(prefix.Length);
                if (rest.Length > 0 && rest.IndexOf(ScopeSeparator) < 0) keys.Add(rest);
            }
            return keys;
        }

        internal void DeleteIn(string prefix)
        {
            foreach (string key in entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                entries.Remove(key);
            if (prefix.Length == 0)
            {
                quarantine = new JObject();
                metadata = new SaveMetadata { SaveCount = metadata.SaveCount };
                playtimeMark = clock();
            }
            foreach (KeyValuePair<string, ISectionBinding> section in sections)
                if (section.Key.StartsWith(prefix, StringComparison.Ordinal)) section.Value.Reload();
            MarkDirty();
        }

        internal static string Scoped(string prefix, string segment)
        {
            if (string.IsNullOrEmpty(segment)) throw new ArgumentException("Keys and scopes must not be empty.", nameof(segment));
            if (segment.IndexOf(ScopeSeparator) >= 0)
                throw new ArgumentException($"'{segment}' must not contain '{ScopeSeparator}'; nest with ForScope instead.", nameof(segment));
            return prefix + segment;
        }

        private void ThrowIfSection(string fullKey)
        {
            if (sections.ContainsKey(fullKey))
                throw new InvalidOperationException($"'{fullKey}' is a section; use its Data / Set instead of Save / Load.");
        }

        // ------------------------------------------------------------------ writing

        private bool TryBeginWrite(out string json, out long id)
        {
            json = null;
            id = 0;
            if (writesBlocked)
            {
                IsDirty = false; // nothing can be written; do not keep the autosave spinning
                return false;
            }
            CaptureSections();
            AddPlaytime();
            metadata.SaveCount++;
            DateTime now = utcNow();
            if (metadata.FirstSavedUtc == default) metadata.FirstSavedUtc = now;
            metadata.LastSavedUtc = now;
            metadata.AppVersion = Application.version;
            bool pretty = settings.PrettyPrintInEditor && Application.isEditor;
            json = BuildDocument(metadata).ToString(pretty ? Formatting.Indented : Formatting.None);
            id = ++nextWriteId;
            IsDirty = false;
            return true;
        }

        private bool WriteNumbered(string json, long id)
        {
            lock (writeGate)
            {
                if (id <= writtenId) return true; // a newer write already landed
                try
                {
                    storage.Write(json);
                    writtenId = id;
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogError($"{LogPrefix} Write to '{storage.Location}' failed: {e.Message}");
                    return false;
                }
            }
        }

        private void EndWrite(bool ok)
        {
            if (!ok) MarkDirty(); // retried by the next autosave / flush
            try { Flushed?.Invoke(ok); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void ScheduleAutoSave()
        {
            if (autoSaveScheduled || settings.AutoSaveDelaySeconds <= 0f || SynchronizationContext.Current == null) return;
            autoSaveScheduled = true;
            _ = AutoSaveAsync();
        }

        private async Task AutoSaveAsync()
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.AutoSaveDelaySeconds));
                autoSaveScheduled = false;
                if (!disposed && IsDirty) await FlushAsync();
            }
            catch (Exception e)
            {
                autoSaveScheduled = false;
                Debug.LogException(e);
            }
        }

        private void CaptureSections()
        {
            foreach (KeyValuePair<string, ISectionBinding> section in sections)
            {
                try { entries[section.Key] = section.Value.Capture(); }
                catch (Exception e) { Debug.LogError($"{LogPrefix} Section '{section.Key}' could not be serialized: {e.Message}. Its last stored data is kept."); }
            }
        }

        private JObject BuildDocument(SaveMetadata meta)
        {
            var stored = new JObject();
            foreach (KeyValuePair<string, Entry> entry in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
                stored[entry.Key] = new JObject { ["v"] = entry.Value.Version, ["d"] = entry.Value.Data.DeepClone() };
            var document = new JObject
            {
                ["format"] = DocumentFormat,
                ["meta"] = new JObject
                {
                    ["saveCount"]     = meta.SaveCount,
                    ["firstSavedUtc"] = Iso(meta.FirstSavedUtc),
                    ["lastSavedUtc"]  = Iso(meta.LastSavedUtc),
                    ["playtime"]      = Math.Round(meta.PlaytimeSeconds, 1),
                    ["appVersion"]    = meta.AppVersion ?? string.Empty,
                    ["progress"]      = meta.Progress,
                },
                ["entries"] = stored,
            };
            if (quarantine.Count > 0) document["quarantine"] = quarantine.DeepClone();
            return document;
        }

        private bool TryReadDocument(string json, out string error)
        {
            JObject document;
            try { document = (JObject)SaveJson.Parse(json); }
            catch (Exception e)
            {
                error = "invalid JSON: " + e.Message;
                return false;
            }
            int format = document.Value<int?>("format") ?? 0;
            if (format > DocumentFormat)
            {
                error = $"the save has format {format}, this build reads up to {DocumentFormat} (written by a newer build)";
                return false;
            }

            var read = new Dictionary<string, Entry>(StringComparer.Ordinal);
            if (document["entries"] is JObject stored)
                foreach (JProperty property in stored.Properties())
                {
                    if (property.Value is not JObject entry) continue;
                    read[property.Name] = new Entry(entry.Value<int?>("v") ?? 0, entry["d"] ?? JValue.CreateNull());
                }
            entries = read;
            quarantine = document["quarantine"] as JObject ?? new JObject();

            JObject meta = document["meta"] as JObject ?? new JObject();
            metadata = new SaveMetadata
            {
                SaveCount       = meta.Value<long?>("saveCount") ?? 0,
                FirstSavedUtc   = ParseIso(meta.Value<string>("firstSavedUtc")),
                LastSavedUtc    = ParseIso(meta.Value<string>("lastSavedUtc")),
                PlaytimeSeconds = meta.Value<double?>("playtime") ?? 0,
                AppVersion      = meta.Value<string>("appVersion"),
                Progress        = meta.Value<long?>("progress") ?? 0,
            };
            error = null;
            return true;
        }

        private JToken ToToken<T>(T value) => value == null ? JValue.CreateNull() : JToken.FromObject(value, serializer);

        private static string Iso(DateTime utc) => utc == default ? string.Empty : utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

        private static DateTime ParseIso(string value) =>
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime utc)
                ? utc
                : default;

        // ------------------------------------------------------------------ playtime and lifecycle

        private double UncountedPlaytime() => paused ? 0 : Math.Max(0, clock() - playtimeMark);

        private void AddPlaytime()
        {
            metadata.PlaytimeSeconds += UncountedPlaytime();
            playtimeMark = clock();
        }

        private void OnPauseChanged(bool isPaused)
        {
            if (isPaused)
            {
                // The OS may kill a backgrounded app without another callback: write now, on this thread.
                if (!paused && settings.FlushOnPause && IsDirty) Flush();
                else AddPlaytime();
                paused = true;
                return;
            }
            paused = false;
            playtimeMark = clock();
        }

        private void OnFocusChanged(bool hasFocus)
        {
            if (!hasFocus && settings.FlushOnPause) Flush();
        }

        private void OnQuitting() => Flush();

        // ------------------------------------------------------------------ sections

        private readonly struct Entry
        {
            public Entry(int version, JToken data)
            {
                Version = version;
                Data = data;
            }

            public int Version { get; }
            public JToken Data { get; }
        }

        private interface ISectionBinding
        {
            Type DataType { get; }

            /// <summary>Reads the section from the entries again (startup, import, delete): Data becomes a new object.</summary>
            void Reload();

            Entry Capture();
        }

        private sealed class SectionBinding<T> : ISaveSection<T>, ISectionBinding where T : class, new()
        {
            private readonly SaveService owner;
            private readonly SaveMigrations migrations;
            private T data;

            public SectionBinding(SaveService owner, string key, int version, SaveMigrations migrations)
            {
                this.owner = owner;
                this.migrations = migrations;
                Key = key;
                Version = version;
            }

            public string Key { get; }
            public int Version { get; }
            public T Data => data;
            public string LoadError { get; private set; }
            public Type DataType => typeof(T);
            public event Action<T> Replaced;

            public void MarkDirty() => owner.MarkDirty();

            public void Set(T value)
            {
                Swap(value ?? throw new ArgumentNullException(nameof(value)));
                owner.MarkDirty();
            }

            public void Reset() => Set(new T());

            public Entry Capture() => new(Version, owner.ToToken(data));

            public void Reload()
            {
                bool initial = data == null;
                LoadError = null;
                T loaded = null;
                if (owner.entries.TryGetValue(Key, out Entry stored) && stored.Data.Type != JTokenType.Null)
                {
                    try
                    {
                        JToken token = stored.Data.DeepClone();
                        if (stored.Version > Version)
                            Debug.LogWarning($"{LogPrefix} '{Key}' is version {stored.Version}, newer than this build's {Version}; fields it does not know are dropped at the next save.");
                        else if (stored.Version < Version && migrations != null)
                            token = migrations.Run(token, stored.Version, Version);
                        loaded = token.ToObject<T>(owner.serializer);
                    }
                    catch (Exception e)
                    {
                        LoadError = $"{e.GetType().Name}: {e.Message}";
                        owner.quarantine[Key] = new JObject
                        {
                            ["v"] = stored.Version,
                            ["d"] = stored.Data.DeepClone(),
                            ["error"] = LoadError,
                            ["at"] = Iso(owner.utcNow()),
                        };
                        Debug.LogError($"{LogPrefix} Section '{Key}' could not be read ({LoadError}); it starts fresh and the stored data is kept in 'quarantine'.");
                        owner.MarkDirty(); // persist the quarantine copy
                    }
                }
                if (initial)
                    data = loaded ?? new T();
                else
                    Swap(loaded ?? new T());
            }

            private void Swap(T value)
            {
                data = value;
                try { Replaced?.Invoke(value); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        // ------------------------------------------------------------------ scopes

        private sealed class ScopedSave : ISaveService
        {
            private readonly SaveService root;
            private readonly string prefix;

            public ScopedSave(SaveService root, string scope)
            {
                this.root = root;
                prefix = scope + ScopeSeparator;
            }

            public SaveLoadStatus LoadStatus => root.LoadStatus;
            public SaveMetadata Metadata => root.Metadata;
            public bool IsDirty => root.IsDirty;

            public event Action<bool> Flushed
            {
                add => root.Flushed += value;
                remove => root.Flushed -= value;
            }

            public ISaveSection<T> Section<T>(string key, int version = 1, SaveMigrations migrations = null) where T : class, new() =>
                root.SectionAt<T>(Scoped(prefix, key), version, migrations);

            public void Save<T>(string key, T value) => root.SaveAt(Scoped(prefix, key), value);
            public T Load<T>(string key, T defaultValue = default) => root.LoadAt(Scoped(prefix, key), defaultValue);
            public bool HasKey(string key) => root.HasKeyAt(Scoped(prefix, key));
            public void Delete(string key) => root.DeleteAt(Scoped(prefix, key));
            public IReadOnlyCollection<string> GetKeys() => root.KeysIn(prefix);
            public ISaveService ForScope(string scope) => new ScopedSave(root, Scoped(prefix, scope));
            public void DeleteAll() => root.DeleteIn(prefix);
            public void MarkDirty() => root.MarkDirty();
            public void SetProgress(long progress) => root.SetProgress(progress);
            public bool Flush() => root.Flush();
            public Task<bool> FlushAsync() => root.FlushAsync();
            public SaveSnapshot ExportSnapshot() => root.ExportSnapshot();
            public bool ImportSnapshot(SaveSnapshot snapshot) => root.ImportSnapshot(snapshot);
        }
    }
}
