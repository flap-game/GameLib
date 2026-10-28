using System.Collections.Generic;
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
    /// iOS builds: takes AppsFlyerLib.framework / PurchaseConnector.framework back out of the Unity-iPhone
    /// "Embed Frameworks" phase when CocoaPods provides them. AppsFlyer plugin 7.0.3's AppsFlyerPostBuildProcessor
    /// (order 101) always embeds them from BUILT_PRODUCTS_DIR, which is only right when EDM4U resolved AppsFlyer through
    /// Swift Package Manager (EDM4U 1.2.187+). With the Podfile path the static PurchaseConnector is not there and the
    /// archive fails: "Release-iphoneos/PurchaseConnector.framework: No such file or directory". CocoaPods' own
    /// "[CP] Embed Pods Frameworks" phase already embeds what the pods need.
    /// </summary>
    public sealed class AppsFlyerIosEmbedFix : IPostprocessBuildWithReport
    {
        /// <summary>Framework the plugin embeds -> pod that provides it when EDM4U uses CocoaPods.</summary>
        public static readonly IReadOnlyDictionary<string, string> FrameworkPods = new Dictionary<string, string>
        {
            { "AppsFlyerLib.framework", "AppsFlyerFramework" },
            { "PurchaseConnector.framework", "PurchaseConnector" },
        };

        // After AppsFlyerPostBuildProcessor (101); EDM4U has written the Podfile by then.
        public int callbackOrder => 102;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
                return;
#if UNITY_IOS
            string output = report.summary.outputPath;
            string podfilePath = Path.Combine(output, "Podfile");
            if (!File.Exists(podfilePath))
                return;
            List<string> frameworks = FrameworksProvidedByPods(File.ReadAllText(podfilePath));
            if (frameworks.Count == 0)
                return;

            string projectPath = PBXProject.GetPBXProjectPath(output);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            bool changed = false;
            foreach (string framework in frameworks)
            {
                string fileGuid = project.FindFileGuidByProjectPath("Frameworks/" + framework);
                if (fileGuid == null)
                    continue;
                project.RemoveFile(fileGuid);
                changed = true;
                UnityEngine.Debug.Log($"[GameLib] AppsFlyer: {framework} removed from Embed Frameworks (pod '{FrameworkPods[framework]}' provides it).");
            }
            if (changed)
                project.WriteToFile(projectPath);
#endif
        }

        /// <summary>The frameworks whose pod the Podfile lists. None when AppsFlyer came through SPM, where the embed is needed.</summary>
        public static List<string> FrameworksProvidedByPods(string podfile)
        {
            var frameworks = new List<string>();
            foreach (KeyValuePair<string, string> pair in FrameworkPods)
            {
                if (podfile.Contains("pod '" + pair.Value + "'"))
                    frameworks.Add(pair.Key);
            }
            return frameworks;
        }
    }
}
