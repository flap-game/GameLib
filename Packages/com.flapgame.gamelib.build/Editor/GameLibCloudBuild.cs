using FlapGame.GameLib.Build;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unity Cloud Build hooks. In the build target's Advanced settings set <b>Pre-Export Method Name</b> to
/// <c>GameLibCloudBuild.PreExport</c>. No namespace on purpose: Cloud Build looks the method up by
/// <c>ClassName.MethodName</c>. No <c>BuildManifestObject</c> parameter either: Cloud Build injects that type into the
/// project, where this package's assembly may not see it.
/// <para>Version name and build numbers are not touched: they are set on GameHub.</para>
/// </summary>
public static class GameLibCloudBuild
{
    public static void PreExport() => Apply();

    /// <summary>What <see cref="PreExport"/> does (also GameLib > Cloud Build > Run Pre-Export Locally).</summary>
    public static void Apply()
    {
        CloudBuildSettings settings = CloudBuildSettings.Load();
        if (settings == null || settings.HideUnitySplashScreen)
        {
            PlayerSettings.SplashScreen.show = false;
            Debug.Log("[CloudBuild] Unity splash screen off.");
        }
    }

    [MenuItem("GameLib/Cloud Build/Run Pre-Export Locally", false, 201)]
    private static void RunLocally()
    {
        if (!EditorUtility.DisplayDialog("Run Cloud Build pre-export",
                                         "Applies the Cloud Build pre-export to this project's Player Settings " +
                                         "(Unity splash screen). Continue?", "Run", "Cancel"))
            return;
        Apply();
    }
}
