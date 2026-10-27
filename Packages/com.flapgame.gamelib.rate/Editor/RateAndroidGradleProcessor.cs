using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
#if UNITY_ANDROID
using UnityEditor.Android;
#endif
using UnityEngine;

namespace FlapGame.GameLib.Rate.Editor
{
    /// <summary>
    /// Adds Google Play In-App Review (<see cref="RateSettings.AndroidReviewLibrary"/>) to the generated unityLibrary
    /// module and keeps its classes from R8, which cannot see the JNI calls. Only when a Rate module is listed in
    /// GameLibSettings. Idempotent.
    /// </summary>
#if UNITY_ANDROID
    public sealed class RateAndroidGradleProcessor : IPostGenerateGradleAndroidProject
    {
        private const string LogPrefix = "[GameLib.Rate]";

        public const string KeepRules =
            "\n# GameLib Rate: Play In-App Review is called through JNI.\n" +
            "-keep class com.google.android.play.core.review.** { *; }\n" +
            "-keep class com.google.android.gms.tasks.** { *; }\n";

        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            GameLibSettings settings = GameLibSettings.Load();
            RateModule module = settings != null ? settings.Modules.OfType<RateModule>().FirstOrDefault() : null;
            string library = module != null ? module.Settings.AndroidReviewLibrary?.Trim() : null;
            if (string.IsNullOrEmpty(library)) return;

            string gradlePath = Path.Combine(path, "build.gradle");
            if (!File.Exists(gradlePath))
            {
                Debug.LogWarning($"{LogPrefix} {gradlePath} not found; Play In-App Review not added (the store page opens instead).");
                return;
            }
            string gradle = File.ReadAllText(gradlePath);
            if (!RateGradle.TryAddDependency(gradle, library, out string patched))
                Debug.LogWarning($"{LogPrefix} No dependencies block in {gradlePath}; Play In-App Review not added.");
            else if (patched != gradle)
                File.WriteAllText(gradlePath, patched);

            string proguardPath = Path.Combine(path, "proguard-unity.txt");
            if (File.Exists(proguardPath))
            {
                string rules = File.ReadAllText(proguardPath);
                if (!rules.Contains("com.google.android.play.core.review")) File.AppendAllText(proguardPath, KeepRules);
            }
            else
            {
                Debug.LogWarning($"{LogPrefix} {proguardPath} not found; with minify on, keep com.google.android.play.core.review.** yourself.");
            }
        }
    }
#endif

    /// <summary>Gradle text edits of the Rate build step.</summary>
    public static class RateGradle
    {
        /// <summary>
        /// Adds <c>implementation '&lt;library&gt;'</c> to the top-level dependencies block. True when the block exists
        /// (<paramref name="patched"/> is unchanged when the artifact is already listed, in any version).
        /// </summary>
        public static bool TryAddDependency(string gradle, string library, out string patched)
        {
            patched = gradle;
            // group:artifact, followed by ':' (a version) or a quote: another artifact of the same group does not count.
            string[] parts = library.Split(':');
            string artifact = parts.Length >= 2 ? parts[0] + ":" + parts[1] : library;
            if (Regex.IsMatch(gradle, Regex.Escape(artifact) + "[:'\"]")) return true;
            Match block = Regex.Match(gradle, @"(?m)^dependencies\s*\{[^\S\n]*\r?\n?");
            if (!block.Success) return false;
            patched = gradle.Insert(block.Index + block.Length, $"    implementation '{library}'\n");
            return true;
        }
    }
}
