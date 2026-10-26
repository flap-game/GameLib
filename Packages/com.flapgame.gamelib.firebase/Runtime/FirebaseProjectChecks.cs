using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

namespace FlapGame.GameLib.Firebase
{
    /// <summary>Configuration checks shared by the Firebase modules' <see cref="GameLibModule.Validate"/>.</summary>
    public static class FirebaseProjectChecks
    {
        public const string AndroidConfigFile = "google-services.json";
        public const string IosConfigFile = "GoogleService-Info.plist";

        /// <summary>
        /// Adds an error when the SDK type <paramref name="sdkTypeName"/> (assembly-qualified) cannot be found, and a
        /// problem for each missing config file: an error for the active build target, a warning for the other platform.
        /// </summary>
        public static void Validate(ICollection<GameLibIssue> issues, string moduleName, string sdkTypeName, string sdkPackage)
        {
            if (Type.GetType(sdkTypeName) == null)
                issues.Add(GameLibIssue.Error($"{moduleName}: {sdkPackage} is not installed - install it from GameLib > Setup."));
#if UNITY_EDITOR
            CheckFile(issues, moduleName, AndroidConfigFile, BuildTarget.Android, "Android");
            CheckFile(issues, moduleName, IosConfigFile, BuildTarget.iOS, "iOS");
#endif
        }

#if UNITY_EDITOR
        /// <summary>True when a file named <paramref name="fileName"/> exists anywhere under Assets/.</summary>
        public static bool HasConfigFile(string fileName)
        {
            string stem = Path.GetFileNameWithoutExtension(fileName);
            foreach (string guid in AssetDatabase.FindAssets(stem, new[] { "Assets" }))
                if (string.Equals(Path.GetFileName(AssetDatabase.GUIDToAssetPath(guid)), fileName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static void CheckFile(ICollection<GameLibIssue> issues, string moduleName, string fileName, BuildTarget target, string platform)
        {
            if (HasConfigFile(fileName)) return;
            string message = $"{moduleName}: no {fileName} under Assets/ - download it from the Firebase console, or Firebase cannot start on {platform}.";
            issues.Add(EditorUserBuildSettings.activeBuildTarget == target ? GameLibIssue.Error(message) : GameLibIssue.Warning(message));
        }
#endif
    }
}
