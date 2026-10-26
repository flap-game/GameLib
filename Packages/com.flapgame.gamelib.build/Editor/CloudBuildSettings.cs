using System.IO;
using UnityEditor;
using UnityEngine;

namespace FlapGame.GameLib.Build
{
    /// <summary>
    /// Per-game Unity Cloud Build options, read by <see cref="GameLibCloudBuild.PreExport"/>. Editor-only asset at
    /// <see cref="AssetPath"/>; created on first use (GameLib > Cloud Build > Settings). Commit it.
    /// </summary>
    public sealed class CloudBuildSettings : ScriptableObject
    {
        public const string AssetPath = "Assets/GameLib/Editor/CloudBuildSettings.asset";

        [Tooltip("Turn the Unity splash screen off in Cloud Builds (allowed on every Unity 6 plan).")]
        public bool HideUnitySplashScreen = true;

        public static CloudBuildSettings Load() => AssetDatabase.LoadAssetAtPath<CloudBuildSettings>(AssetPath);

        public static CloudBuildSettings LoadOrCreate()
        {
            CloudBuildSettings settings = Load();
            if (settings != null) return settings;

            settings = CreateInstance<CloudBuildSettings>();
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath) ?? "Assets");
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        [MenuItem("GameLib/Cloud Build/Settings", false, 200)]
        private static void Select()
        {
            Selection.activeObject = LoadOrCreate();
            EditorGUIUtility.PingObject(Selection.activeObject);
        }
    }
}
