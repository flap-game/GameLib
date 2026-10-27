using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Save
{
    /// <summary>A whole save as JSON plus its metadata (<see cref="ISaveService.ExportSnapshot"/>).</summary>
    public sealed class SaveSnapshot
    {
        public SaveSnapshot(string json, SaveMetadata metadata)
        {
            Json = json ?? throw new ArgumentNullException(nameof(json));
            Metadata = metadata ?? new SaveMetadata();
        }

        /// <summary>The save document; plain JSON, not encrypted - the backend decides how it is stored.</summary>
        public string Json { get; }

        public SaveMetadata Metadata { get; }
    }

    /// <summary>
    /// Where a cloud copy of the save lives (Google Play Games Saved Games, iCloud, UGS Cloud Save, a game server).
    /// Implemented by a cloud package; <see cref="SaveSync.SyncAsync"/> uses it.
    /// </summary>
    public interface ICloudSaveBackend
    {
        string Id { get; }

        /// <summary>The cloud copy, or null when there is none yet.</summary>
        Task<SaveSnapshot> DownloadAsync(CancellationToken cancellationToken = default);

        Task UploadAsync(SaveSnapshot snapshot, CancellationToken cancellationToken = default);
    }

    /// <summary>Which copy a sync keeps.</summary>
    public enum SaveSyncResult
    {
        /// <summary>The two copies were the same save; nothing moved.</summary>
        InSync = 0,
        /// <summary>The local copy won and was uploaded.</summary>
        Uploaded = 1,
        /// <summary>The cloud copy won and replaced the local one.</summary>
        Downloaded = 2,
        /// <summary>Download, upload or import failed; the local save is unchanged.</summary>
        Failed = 3,
    }

    /// <summary>Local / cloud conflict rules and the sync built on them.</summary>
    public static class SaveSync
    {
        /// <summary>
        /// The copy with more <see cref="SaveMetadata.Progress"/>, then more playtime, then the later save - the rule
        /// mobile games use (Play Games' "longest playtime" with the game's own progress first). Ties keep the local copy.
        /// </summary>
        public static SaveSnapshot PreferMoreProgress(SaveSnapshot local, SaveSnapshot remote)
        {
            if (remote == null) return local;
            if (local == null) return remote;
            SaveMetadata l = local.Metadata, r = remote.Metadata;
            if (l.Progress != r.Progress) return r.Progress > l.Progress ? remote : local;
            if (Math.Abs(l.PlaytimeSeconds - r.PlaytimeSeconds) >= 1.0) return r.PlaytimeSeconds > l.PlaytimeSeconds ? remote : local;
            return r.LastSavedUtc > l.LastSavedUtc ? remote : local;
        }

        /// <summary>
        /// Downloads the cloud copy, keeps the winner of <paramref name="resolve"/> (default <see cref="PreferMoreProgress"/>)
        /// and moves it to the other side. Call it at launch after sign-in, and on pause if the backend is cheap.
        /// </summary>
        public static async Task<SaveSyncResult> SyncAsync(ISaveService save, ICloudSaveBackend backend,
                                                           Func<SaveSnapshot, SaveSnapshot, SaveSnapshot> resolve = null,
                                                           CancellationToken cancellationToken = default)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            resolve ??= PreferMoreProgress;
            try
            {
                SaveSnapshot remote = await backend.DownloadAsync(cancellationToken);
                SaveSnapshot local = save.ExportSnapshot();
                if (remote != null && remote.Json == local.Json) return SaveSyncResult.InSync;

                SaveSnapshot winner = resolve(local, remote);
                if (winner != null && winner == remote)
                {
                    if (!save.ImportSnapshot(remote)) return SaveSyncResult.Failed;
                    return SaveSyncResult.Downloaded;
                }
                await backend.UploadAsync(local, cancellationToken);
                return SaveSyncResult.Uploaded;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Cloud sync with '{backend.Id}' failed: {e.Message}");
                return SaveSyncResult.Failed;
            }
        }
    }
}
