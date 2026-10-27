using System.IO;
using System.Linq;
using System.Text;
using FlapGame.GameLib.Save.Json;
using UnityEditor;
using UnityEngine;

namespace FlapGame.GameLib.Save.Editor
{
    /// <summary>GameLib > Save: find, read and delete the Editor's save file (the settings of the project's SaveModule asset).</summary>
    internal static class SaveMenu
    {
        private const string Root = "GameLib/Save/";

        [MenuItem(Root + "Open Save Folder")]
        private static void OpenFolder()
        {
            FileSaveStorage storage = Storage();
            Directory.CreateDirectory(Path.GetDirectoryName(storage.FilePath));
            EditorUtility.RevealInFinder(File.Exists(storage.FilePath) ? storage.FilePath : Path.GetDirectoryName(storage.FilePath));
        }

        [MenuItem(Root + "Show Save As JSON")]
        private static void ShowJson()
        {
            SaveSettings settings = Settings();
            FileSaveStorage storage = SaveModule.CreateStorage(settings);
            // Decoded here rather than through storage.Read(), which would move a damaged file aside.
            string file = new[] { storage.FilePath, storage.TempPath, storage.BackupPath }.FirstOrDefault(File.Exists);
            if (file == null)
            {
                Debug.Log($"[Save] No save at '{storage.FilePath}'.");
                return;
            }
            SaveDecodeStatus status = new SaveCodec(settings.ResolveSecret(), settings.Encrypt).Decode(File.ReadAllBytes(file), out string json);
            if (json == null)
            {
                Debug.LogWarning($"[Save] '{file}' is {status}: not readable with this project's secret.");
                return;
            }
            // Decrypted and indented, in Temp/ so it is never mistaken for the save itself.
            string path = Path.Combine("Temp", "gamelib-save-view.json");
            File.WriteAllText(path, SaveJson.Parse(json).ToString(Newtonsoft.Json.Formatting.Indented), new UTF8Encoding(false));
            Debug.Log($"[Save] '{file}' ({status}) -> {Path.GetFullPath(path)}");
            EditorUtility.OpenWithDefaultApp(path);
        }

        [MenuItem(Root + "Delete Save")]
        private static void DeleteSave()
        {
            FileSaveStorage storage = Storage();
            if (!EditorUtility.DisplayDialog("Delete save", $"Delete '{storage.FilePath}' and its .tmp / .bak copies?", "Delete", "Cancel"))
                return;
            storage.Delete();
            Debug.Log($"[Save] Deleted '{storage.FilePath}'.");
        }

        [MenuItem(Root + "Delete Save", true)]
        private static bool CanDeleteSave() => !EditorApplication.isPlaying;

        private static SaveSettings Settings() => FindModule()?.Settings ?? new SaveSettings();

        private static FileSaveStorage Storage() => SaveModule.CreateStorage(Settings());

        private static SaveModule FindModule() =>
            AssetDatabase.FindAssets("t:" + nameof(SaveModule))
                         .Select(guid => AssetDatabase.LoadAssetAtPath<SaveModule>(AssetDatabase.GUIDToAssetPath(guid)))
                         .FirstOrDefault(module => module != null);
    }
}
