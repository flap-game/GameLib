using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Save
{
    /// <summary>
    /// Reads the save file (off the main thread) and registers <see cref="ISaveService"/>. A damaged or unreadable save is
    /// reported to <see cref="ICrashReporter"/> and as the analytics event <c>save_load_issue</c>. Created by GameLib > Setup.
    /// </summary>
    [CreateAssetMenu(menuName = "GameLib/Modules/Save", fileName = "SaveModule")]
    public sealed class SaveModule : GameLibModule
    {
        public const string LoadIssueEvent = "save_load_issue";

        [SerializeField] private SaveSettings settings = new();

        public SaveSettings Settings => settings;

        public override int Order => GameLibModuleOrder.Save;

        /// <summary>The storage these settings describe: the save file with its codec.</summary>
        public static FileSaveStorage CreateStorage(SaveSettings settings) =>
            new((settings.FileName ?? string.Empty).Trim(), new SaveCodec(settings.ResolveSecret(), settings.Encrypt),
                acceptIntegrityFailure: Application.isEditor && settings.AcceptEditedSaveInEditor);

        public override async ValueTask InstallAsync(IServiceRegistry services, CancellationToken cancellationToken = default)
        {
            SaveSettings copy = settings.Clone();
            FileSaveStorage storage = CreateStorage(copy);
#if UNITY_WEBGL && !UNITY_EDITOR
            SaveReadResult loaded = storage.Read();
#else
            SaveReadResult loaded = await Task.Run(() => storage.Read(), cancellationToken);
#endif
            services.TryResolve(out IAppLifecycle lifecycle);
            var save = new SaveService(storage, loaded, copy, lifecycle);
            services.Register<ISaveService>(save);

            if (save.LoadStatus is SaveLoadStatus.Loaded or SaveLoadStatus.New) return;
            if (services.TryResolve(out ICrashReporter crash))
            {
                crash.Log($"[Save] {save.LoadStatus}: {save.LoadError}");
                crash.RecordException(new SaveLoadException(save.LoadStatus, save.LoadError));
            }
            if (services.TryResolve(out IAnalytics analytics))
                analytics.Track(LoadIssueEvent, ("status", save.LoadStatus.ToString()));
        }

        public override void Validate(ICollection<GameLibIssue> issues)
        {
            string name = settings.FileName?.Trim();
            if (string.IsNullOrEmpty(name))
                issues.Add(GameLibIssue.Error("Save: File Name is empty."));
            else if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                issues.Add(GameLibIssue.Error($"Save: File Name '{name}' is not a plain file name."));
            if (settings.AutoSaveDelaySeconds <= 0f)
                issues.Add(GameLibIssue.Warning("Save: Auto Save Delay is 0 - changes are only written on pause / quit / Flush()."));
            if (!settings.FlushOnPause)
                issues.Add(GameLibIssue.Warning("Save: Flush On Pause is off - progress since the last autosave is lost when the OS kills the app in the background."));
        }
    }

    /// <summary>Non-fatal report of a save that could not be loaded cleanly.</summary>
    public sealed class SaveLoadException : Exception
    {
        public SaveLoadException(SaveLoadStatus status, string detail)
            : base($"Save load: {status} ({detail})")
        {
            Status = status;
        }

        public SaveLoadStatus Status { get; }
    }
}
