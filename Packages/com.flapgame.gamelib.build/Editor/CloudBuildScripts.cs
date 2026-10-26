using System.IO;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace FlapGame.GameLib.Build
{
    /// <summary>Copies the Cloud Build shell scripts shipped in this package into the project, where Cloud Build can run them.</summary>
    public static class CloudBuildScripts
    {
        public const string ProjectFolder = "BuildScripts";
        public const string AppStoreUploadScript = "post-build-upload-appstore.bash";
        public const string GitAttributesLine = ProjectFolder + "/*.bash text eol=lf";

        /// <summary>Path of the script relative to the project root, as entered in the Cloud Build dashboard.</summary>
        public static string AppStoreUploadPath => ProjectFolder + "/" + AppStoreUploadScript;

        [MenuItem("GameLib/Cloud Build/Install App Store Upload Script", false, 202)]
        private static void InstallFromMenu()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            bool exists = File.Exists(Path.Combine(projectRoot, AppStoreUploadPath));
            if (exists && !EditorUtility.DisplayDialog("Install App Store upload script",
                                                       $"{AppStoreUploadPath} already exists. Replace it with the version from GameLib Build?",
                                                       "Replace", "Cancel"))
                return;

            InstallAppStoreUpload(projectRoot);
            EditorUtility.DisplayDialog("Install App Store upload script",
                                        $"Installed {AppStoreUploadPath}. Commit it, then in Unity Cloud Build set it as the iOS " +
                                        "target's Post-build script and add ITUNES_USERNAME / ITUNES_PASSWORD (see the GameLib Build README).",
                                        "OK");
        }

        /// <summary>Writes the upload script into <c>&lt;projectRoot&gt;/BuildScripts</c> with LF line endings and makes git keep them.</summary>
        public static void InstallAppStoreUpload(string projectRoot)
        {
            string text = File.ReadAllText(TemplatePath(AppStoreUploadScript)).Replace("\r\n", "\n");
            string target = Path.Combine(projectRoot, ProjectFolder, AppStoreUploadScript);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, text);
            EnsureGitAttributes(Path.Combine(projectRoot, ".gitattributes"));
            Debug.Log($"[CloudBuild] Installed {AppStoreUploadPath}.");
        }

        /// <summary>Appends <see cref="GitAttributesLine"/> to the file unless it is there already. A CRLF checkout of a
        /// bash script fails on the Cloud Build Mac with "$'\r': command not found".</summary>
        public static void EnsureGitAttributes(string path)
        {
            string current = File.Exists(path) ? File.ReadAllText(path) : "";
            foreach (string line in current.Split('\n'))
            {
                if (line.Trim() == GitAttributesLine)
                    return;
            }
            string separator = current.Length == 0 || current.EndsWith("\n") ? "" : "\n";
            File.WriteAllText(path, current + separator + GitAttributesLine + "\n");
        }

        private static string TemplatePath(string file)
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(CloudBuildScripts).Assembly);
            string root = package != null ? package.resolvedPath : Path.GetFullPath("Packages/com.flapgame.gamelib.build");
            return Path.Combine(root, "Editor", "Templates~", file);
        }
    }
}
