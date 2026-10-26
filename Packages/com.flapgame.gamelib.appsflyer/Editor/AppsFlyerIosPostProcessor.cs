using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
#if UNITY_IOS
using System.IO;
using UnityEditor.iOS.Xcode;
#endif

namespace FlapGame.GameLib.AppsFlyer.Editor
{
    /// <summary>
    /// iOS builds: links AppTrackingTransparency.framework weakly (GameLibAtt.mm; devices before iOS 14 still launch) and,
    /// when the AppsFlyer module shows the ATT prompt itself, writes its NSUserTrackingUsageDescription into Info.plist -
    /// iOS kills an app that asks without it. A description already in Info.plist is kept.
    /// </summary>
    public sealed class AppsFlyerIosPostProcessor : IPostprocessBuildWithReport
    {
        public const string UsageDescriptionKey = "NSUserTrackingUsageDescription";

        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
                return;
#if UNITY_IOS
            AppsFlyerModule module = FindModule();
            if (module == null)
                return;

            string output = report.summary.outputPath;
            string projectPath = PBXProject.GetPBXProjectPath(output);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "AppTrackingTransparency.framework", true);
            project.WriteToFile(projectPath);

            if (module.Att != AttMode.Request || string.IsNullOrWhiteSpace(module.AttUsageDescription))
                return;
            string plistPath = Path.Combine(output, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            if (!plist.root.values.ContainsKey(UsageDescriptionKey))
                plist.root.SetString(UsageDescriptionKey, module.AttUsageDescription.Trim());
            plist.WriteToFile(plistPath);
#endif
        }

        /// <summary>The module listed in GameLibSettings, else any AppsFlyerModule asset; null when the game has none.</summary>
        public static AppsFlyerModule FindModule()
        {
            GameLibSettings settings = GameLibSettings.Load();
            AppsFlyerModule listed = settings != null ? settings.Modules.OfType<AppsFlyerModule>().FirstOrDefault() : null;
            if (listed != null)
                return listed;

            string guid = AssetDatabase.FindAssets("t:" + nameof(AppsFlyerModule)).FirstOrDefault();
            return guid != null ? AssetDatabase.LoadAssetAtPath<AppsFlyerModule>(AssetDatabase.GUIDToAssetPath(guid)) : null;
        }
    }
}
