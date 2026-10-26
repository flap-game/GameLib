#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace FlapGame.GameLib.Haptics.Editor
{
    /// <summary>
    /// Auto-injects <c>android.permission.VIBRATE</c> into the generated Android manifest at build
    /// time so games using <c>IHapticsService</c> don't have to declare it by hand.
    /// </summary>
    /// <remarks>
    /// Runs only on Android builds (guarded by <c>UNITY_ANDROID</c>). Idempotent: if the permission
    /// is already present (added by us on a previous build, or declared by the game), it does nothing.
    /// VIBRATE is a normal-level permission — no runtime prompt and no Play Store warning — so always
    /// declaring it when the package is present is safe.
    /// </remarks>
    public sealed class HapticsAndroidManifestProcessor : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
        private const string VibratePermission = "android.permission.VIBRATE";

        // Run late so a permission we add isn't clobbered by an earlier processor regenerating the file.
        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"[GameLib.Haptics] AndroidManifest not found at '{manifestPath}'; VIBRATE permission not added.");
                return;
            }

            var doc = new XmlDocument();
            doc.Load(manifestPath);

            XmlElement manifest = doc.DocumentElement;
            if (manifest == null || manifest.Name != "manifest")
            {
                Debug.LogWarning("[GameLib.Haptics] Unexpected AndroidManifest root; VIBRATE permission not added.");
                return;
            }

            var nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("android", AndroidNamespace);

            // Already declared (by us on a prior build, or by the game itself)? Leave it untouched.
            XmlNode existing = manifest.SelectSingleNode(
                $"uses-permission[@android:name='{VibratePermission}']", nsmgr);
            if (existing != null)
                return;

            XmlElement permission = doc.CreateElement("uses-permission");
            XmlAttribute nameAttr = doc.CreateAttribute("android", "name", AndroidNamespace);
            nameAttr.Value = VibratePermission;
            permission.Attributes.Append(nameAttr);
            manifest.AppendChild(permission);

            doc.Save(manifestPath);
            Debug.Log("[GameLib.Haptics] Added android.permission.VIBRATE to AndroidManifest.xml.");
        }
    }
}
#endif
