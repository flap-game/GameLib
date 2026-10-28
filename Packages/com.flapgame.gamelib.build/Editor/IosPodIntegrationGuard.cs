using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FlapGame.GameLib.Build
{
    /// <summary>
    /// iOS builds: fails the build right away when CocoaPods integration did not complete.
    /// Without it a failed `pod install` is silent: EDM4U logs the error, the Unity build still "succeeds", fastlane finds no
    /// .xcworkspace and falls back to `xcodebuild -project Unity-iPhone.xcodeproj`. That project has no pod headers, so the
    /// build dies ~5 minutes later inside an unrelated plugin file (AppsFlyeriOSWrapper.mm, MAUnityPlugin.mm, ...) and the
    /// real cause is buried thousands of lines up the log.
    /// </summary>
    public sealed class IosPodIntegrationGuard : IPostprocessBuildWithReport
    {
        public const string LogTag = "[GameLib][PodGuard]";

        // After EDM4U's pod install pass and every other post-process pass.
        public int callbackOrder => int.MaxValue;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
                return;
            // CocoaPods only runs on macOS: an Xcode project exported on Windows/Linux legitimately has no pods installed.
            if (Application.platform != RuntimePlatform.OSXEditor)
            {
                Debug.Log(LogTag + " skipped: not running on macOS, CocoaPods was never invoked.");
                return;
            }
            // EDM4U set to "None" only writes a Podfile and leaves `pod install` to the CI.
            if (!IsEdm4uCocoapodsIntegrationEnabled())
            {
                Debug.Log(LogTag + " skipped: EDM4U CocoaPods integration is disabled.");
                return;
            }

            string failure = FailureMessage(report.summary.outputPath);
            if (failure != null)
                throw new BuildFailedException(failure);
            Debug.Log(LogTag + " OK: CocoaPods integration complete.");
        }

        /// <summary>Null when the Xcode project at <paramref name="buildPath"/> has no Podfile or its pods are installed; otherwise why the build must stop.</summary>
        public static string FailureMessage(string buildPath)
        {
            if (!File.Exists(Path.Combine(buildPath, "Podfile")))
                return null;
            // Pods.xcodeproj is written at the very end of `pod install`; a run that aborts mid-download leaves a partial
            // Pods/ directory without it.
            string podsProjectPath = Path.Combine(buildPath, "Pods", "Pods.xcodeproj");
            if (Directory.Exists(podsProjectPath))
                return null;
            return LogTag + " CocoaPods integration FAILED - '" + podsProjectPath + "' is missing, so `pod install` never finished.\n" +
                   "Search this log upwards for \"[!] Error installing\" - the pod named there is the one that failed.\n" +
                   "Known cause on Unity Cloud Build: pods whose podspec downloads a zip over HTTP from github.com / " +
                   "raw.githubusercontent.com (IronSourceAdQualitySDK, IronSourceSDK, UnityAds, MintegralAdSDK) are fetched with curl, " +
                   "and the builder intermittently fails to resolve github.com (\"curl: (6) Could not resolve host: github.com\"). " +
                   "Git-cloned pods from the same host keep working, so this is usually a transient builder network issue - retry the build.\n" +
                   "Letting the build continue would archive Unity-iPhone.xcodeproj without any pod frameworks and fail later with a " +
                   "misleading Objective-C compile error.";
        }

        // Google.IOSResolver.CocoapodsIntegrationEnabled by reflection, so this compiles without EDM4U.
        private static bool IsEdm4uCocoapodsIntegrationEnabled()
        {
            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type resolver = assembly.GetType("Google.IOSResolver");
                    PropertyInfo property = resolver?.GetProperty("CocoapodsIntegrationEnabled", BindingFlags.Public | BindingFlags.Static);
                    if (property != null)
                        return (bool)property.GetValue(null, null);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogTag + " could not read EDM4U settings, assuming CocoaPods is in use: " + e.Message);
            }
            // EDM4U missing or its API changed: assume pods are expected and let the check run.
            return true;
        }
    }
}
