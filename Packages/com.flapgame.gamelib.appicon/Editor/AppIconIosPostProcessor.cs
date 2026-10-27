using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace FlapGame.GameLib.AppIcon.Editor
{
    /// <summary>
    /// iOS build step: writes every icon of the listed <see cref="AppIconModule"/> into
    /// <c>Unity-iPhone/Images.xcassets</c> and names them in <c>ASSETCATALOG_COMPILER_ALTERNATE_APPICON_NAMES</c>.
    /// </summary>
    public static class AppIconIosPostProcessor
    {
        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string output)
        {
            if (target != BuildTarget.iOS) return;
#if UNITY_IOS
            GameLibSettings settings = GameLibSettings.Load();
            AppIconModule module = settings != null ? settings.Modules.OfType<AppIconModule>().FirstOrDefault() : null;
            List<(string Name, string Path)> icons = AppIconXcode.BuildableIcons(module);
            string catalog = Path.Combine(output, "Unity-iPhone", "Images.xcassets");
            if (!Directory.Exists(catalog))
            {
                if (icons.Count > 0) Debug.LogError($"[GameLib.AppIcon] {catalog} not found; alternate icons not added.");
                return;
            }
            // An "Append" build keeps the sets of icons removed since: they would still be compiled into the app.
            List<string> removed = AppIconXcode.RemoveStaleIconSets(catalog, icons.Select(i => i.Name).ToList());
            if (icons.Count == 0 && removed.Count == 0) return;
            foreach ((string name, string path) in icons) AppIconXcode.WriteIconSet(catalog, name, path);

            string projectPath = PBXProject.GetPBXProjectPath(output);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string app = project.GetUnityMainTargetGuid();
            const string namesKey = "ASSETCATALOG_COMPILER_ALTERNATE_APPICON_NAMES";
            // Kept: names another plugin listed before this step.
            string merged = AppIconXcode.MergeIconNames(project.GetBuildPropertyForAnyConfig(app, namesKey), icons.Select(i => i.Name), removed);
            project.SetBuildProperty(app, namesKey, merged);
            if (merged.Length > 0) project.SetBuildProperty(app, "ASSETCATALOG_COMPILER_INCLUDE_ALL_APPICON_ASSETS", "YES");
            project.WriteToFile(projectPath);
            Debug.Log($"[GameLib.AppIcon] Alternate icons added: {string.Join(", ", icons.Select(i => i.Name))}.");
#endif
        }
    }
}
