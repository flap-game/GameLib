using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
#if UNITY_IOS
using System.IO;
using UnityEditor.iOS.Xcode;
#endif

namespace FlapGame.GameLib.Att.Editor
{
    /// <summary>
    /// iOS builds: links AppTrackingTransparency.framework weakly (GameLibAtt.mm; devices before iOS 14 still launch) and
    /// writes the ATT module's NSUserTrackingUsageDescription into Info.plist - iOS kills an app that asks without it.
    /// Only when the module is listed in GameLibSettings: App Review questions a usage text for a prompt that never shows.
    /// A description already in Info.plist (e.g. from MAX's settings) is kept.
    /// </summary>
    public sealed class AttIosPostProcessor : IPostprocessBuildWithReport
    {
        public const string UsageDescriptionKey = "NSUserTrackingUsageDescription";

        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
                return;
#if UNITY_IOS
            string output = report.summary.outputPath;
            string projectPath = PBXProject.GetPBXProjectPath(output);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "AppTrackingTransparency.framework", true);
            project.WriteToFile(projectPath);

            AttModule module = FindListedModule();
            if (module == null)
                return;
            string description = string.IsNullOrWhiteSpace(module.UsageDescription)
                ? AttModule.DefaultUsageDescription
                : module.UsageDescription.Trim();
            string plistPath = Path.Combine(output, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            if (!plist.root.values.ContainsKey(UsageDescriptionKey))
                plist.root.SetString(UsageDescriptionKey, description);
            plist.WriteToFile(plistPath);
#endif
        }

        /// <summary>The AttModule listed in GameLibSettings (the one that runs); null when the game does not use it.</summary>
        public static AttModule FindListedModule()
        {
            GameLibSettings settings = GameLibSettings.Load();
            return settings != null ? settings.Modules.OfType<AttModule>().FirstOrDefault() : null;
        }
    }
}
