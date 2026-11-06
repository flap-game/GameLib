using System.IO;
#if UNITY_ANDROID
using UnityEditor.Android;
#endif
using UnityEngine;

namespace FlapGame.GameLib.Ads.Editor
{
    /// <summary>
    /// (0.9.0) Android build step for the native-ads Java plugin of this package (<c>Plugins/Android/*.java</c>):
    /// keeps its classes from R8 (C# reaches them only through JNI), and drops them from the Gradle project when the
    /// Google Mobile Ads package is not installed (they need the GMA Android SDK to compile). Idempotent.
    /// </summary>
#if UNITY_ANDROID
    public sealed class AdMobNativeGradleProcessor : IPostGenerateGradleAndroidProject
    {
        private const string LogPrefix = "[GameLib.Ads.AdMob]";
        private const string JavaPackage = "com.flapgame.gamelib.ads.nativeads";

        public const string KeepRules =
            "\n# GameLib Ads AdMob: native ads plugin is called through JNI.\n" +
            "-keep class " + JavaPackage + ".** { *; }\n";

        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
#if GAMELIB_ADMOB
            string proguardPath = Path.Combine(path, "proguard-unity.txt");
            if (File.Exists(proguardPath))
            {
                if (!File.ReadAllText(proguardPath).Contains(JavaPackage)) File.AppendAllText(proguardPath, KeepRules);
            }
            else
            {
                Debug.LogWarning($"{LogPrefix} {proguardPath} not found; with minify on, keep {JavaPackage}.** yourself.");
            }
#else
            string javaDir = Path.Combine(path, "src", "main", "java", "com", "flapgame", "gamelib", "ads", "nativeads");
            if (Directory.Exists(javaDir))
            {
                Directory.Delete(javaDir, true);
                Debug.Log($"{LogPrefix} Google Mobile Ads is not installed: native ads plugin left out of the build.");
            }
#endif
        }
    }
#endif
}
