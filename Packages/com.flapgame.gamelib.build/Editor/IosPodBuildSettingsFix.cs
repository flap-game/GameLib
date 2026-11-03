using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FlapGame.GameLib.Build
{
    /// <summary>
    /// iOS builds: patches the CocoaPods project EDM4U just installed, so the exported Xcode workspace builds as it is.
    /// <list type="bullet">
    /// <item><b>Deployment target.</b> CocoaPods gives each pod target its podspec's minimum (10.0 / 12.0 for several MAX
    /// adapters). Xcode 27 rejects anything below 15.0 ("supported deployment target versions is 15.0 to ..."), which
    /// fails the Run button even when a command-line override passes. Every pod target below the game's own minimum
    /// (Player Settings > Target minimum iOS Version) is raised to it in Pods.xcodeproj.</item>
    /// <item><b>Library evolution, Yandex / AppMetrica only.</b> YandexMobileAds is a prebuilt static xcframework linked
    /// against a NON-resilient build of the AppMetricaLibraryAdapter Swift pod (it references the `shared` addressor).
    /// CocoaPods builds source pods with BUILD_LIBRARY_FOR_DISTRIBUTION = YES, which drops that symbol: "Undefined symbol
    /// ... AppMetricaLibraryAdapter.shared" at link time (Xcode 27 / Swift 6.4 also fails AppMetricaSynchronization with
    /// "@inlinable initializer must delegate"). When Pods/AppMetricaLibraryAdapter exists, every pod xcconfig is switched
    /// to NO - the same fix aframework2 shipped (FrameworkBuildPostProcess.DisableLibraryEvolutionForPods).</item>
    /// </list>
    /// A manual `pod install` in the exported project regenerates both files; rebuild from Unity afterwards.
    /// </summary>
    public sealed class IosPodBuildSettingsFix : IPostprocessBuildWithReport
    {
        public const string LogTag = "[GameLib][PodFix]";
        private const string DistributionYes = "BUILD_LIBRARY_FOR_DISTRIBUTION = YES";
        private const string DistributionNo = "BUILD_LIBRARY_FOR_DISTRIBUTION = NO";
        private static readonly Regex DeploymentTarget = new Regex(@"IPHONEOS_DEPLOYMENT_TARGET = ([0-9.]+);");

        // Well after EDM4U's pod install pass (the order aframework2 and the games' own fixes used), before
        // IosPodIntegrationGuard (int.MaxValue).
        public int callbackOrder => 10001;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
                return;
            string buildPath = report.summary.outputPath;
            if (!Directory.Exists(Path.Combine(buildPath, "Pods")))
                return; // no pods installed (Windows export, or EDM4U set to leave pod install to CI)

            int raised = RaisePodsDeploymentTarget(buildPath, PlayerSettings.iOS.targetOSVersionString);
            Debug.Log(LogTag + " IPHONEOS_DEPLOYMENT_TARGET raised to " + PlayerSettings.iOS.targetOSVersionString +
                      " in " + raised + " Pods.xcodeproj setting(s).");
            if (UsesAppMetrica(buildPath))
            {
                int changed = DisableLibraryEvolution(buildPath);
                Debug.Log(LogTag + " AppMetrica pods present: BUILD_LIBRARY_FOR_DISTRIBUTION set to NO in " + changed +
                          " pod xcconfig file(s).");
            }
        }

        /// <summary>Raises every IPHONEOS_DEPLOYMENT_TARGET below <paramref name="minimum"/> in Pods.xcodeproj; returns how many.</summary>
        public static int RaisePodsDeploymentTarget(string buildPath, string minimum)
        {
            string pbxPath = Path.Combine(buildPath, "Pods", "Pods.xcodeproj", "project.pbxproj");
            if (!File.Exists(pbxPath))
                return 0;
            int changed;
            string patched = RaiseDeploymentTargets(File.ReadAllText(pbxPath), minimum, out changed);
            if (changed > 0)
                File.WriteAllText(pbxPath, patched);
            return changed;
        }

        /// <summary>The pbxproj text with every deployment target below <paramref name="minimum"/> set to it.</summary>
        public static string RaiseDeploymentTargets(string pbxText, string minimum, out int changed)
        {
            int count = 0;
            Version min;
            if (!Version.TryParse(minimum, out min))
            {
                changed = 0;
                return pbxText;
            }
            string result = DeploymentTarget.Replace(pbxText, m =>
            {
                Version v;
                if (!Version.TryParse(m.Groups[1].Value, out v) || v >= min)
                    return m.Value;
                count++;
                return "IPHONEOS_DEPLOYMENT_TARGET = " + minimum + ";";
            });
            changed = count;
            return result;
        }

        public static bool UsesAppMetrica(string buildPath) =>
            Directory.Exists(Path.Combine(buildPath, "Pods", "AppMetricaLibraryAdapter"));

        /// <summary>Switches BUILD_LIBRARY_FOR_DISTRIBUTION to NO in every pod xcconfig; returns how many files changed.</summary>
        public static int DisableLibraryEvolution(string buildPath)
        {
            string supportFiles = Path.Combine(buildPath, "Pods", "Target Support Files");
            if (!Directory.Exists(supportFiles))
                return 0;
            int changed = 0;
            foreach (string xcconfig in Directory.GetFiles(supportFiles, "*.xcconfig", SearchOption.AllDirectories))
            {
                string contents = File.ReadAllText(xcconfig);
                if (!contents.Contains(DistributionYes))
                    continue;
                File.WriteAllText(xcconfig, contents.Replace(DistributionYes, DistributionNo));
                changed++;
            }
            return changed;
        }
    }
}
