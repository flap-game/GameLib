using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace FlapGame.GameLib.Build
{
    /// <summary>
    /// iOS builds: stops the build before it starts when the Editor's own scripts are not compiled for iOS.
    /// iOS post-processors (GameLib ATT's Info.plist / framework step, MAX's GADApplicationIdentifier and SKAdNetwork
    /// entries, ...) sit inside <c>#if UNITY_IOS</c>. An iOS player built while the Editor is still compiled for another
    /// target (seen when a build was started through MCP with StandaloneOSX active) compiles them all away: Info.plist
    /// ships without NSUserTrackingUsageDescription, iOS kills the app when the ATT prompt shows, and the build still
    /// reports "succeeded" without a single warning.
    /// </summary>
    public sealed class IosEditorTargetGuard : IPreprocessBuildWithReport
    {
        public const string LogTag = "[GameLib][IosTargetGuard]";

        public int callbackOrder => int.MinValue;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
                return;
#if UNITY_IOS
            const bool editorCompiledForIos = true;
#else
            const bool editorCompiledForIos = false;
#endif
            string failure = FailureMessage(editorCompiledForIos, EditorUserBuildSettings.activeBuildTarget);
            if (failure != null)
                throw new BuildFailedException(failure);
        }

        /// <summary>Null when an iOS build can run; otherwise why it must stop.</summary>
        public static string FailureMessage(bool editorCompiledForIos, BuildTarget activeTarget)
        {
            if (editorCompiledForIos)
                return null;
            return LogTag + " iOS build refused: the Editor scripts are compiled for " + activeTarget + ", not iOS, so every " +
                   "#if UNITY_IOS post-processor (ATT usage text, MAX Info.plist keys, ...) would be skipped and the build " +
                   "would still report success. Switch first - File > Build Profiles > iOS > Switch Platform, or " +
                   "EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS) and wait for " +
                   "the domain reload, or start Unity with -buildTarget iOS - then build again.";
        }
    }
}
