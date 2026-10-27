using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FlapGame.GameLib.AppIcon.Editor
{
    /// <summary>Writes alternate icons as single-size (1024 pt) app icon sets of an Xcode asset catalog (Xcode 14+).</summary>
    public static class AppIconXcode
    {
        public const int IconSize = 1024;

        /// <summary>File inside every icon set GameLib wrote, so a later build can remove the ones no longer listed.</summary>
        public const string Marker = "gamelib-appicon.txt";

        public const string ContentsJson =
            "{\n  \"images\" : [\n    {\n      \"filename\" : \"icon.png\",\n      \"idiom\" : \"universal\",\n" +
            "      \"platform\" : \"ios\",\n      \"size\" : \"1024x1024\"\n    }\n  ],\n" +
            "  \"info\" : {\n    \"author\" : \"xcode\",\n    \"version\" : 1\n  }\n}\n";

        /// <summary>Writes <c>&lt;catalog&gt;/&lt;name&gt;.appiconset</c> with the image at <paramref name="imagePath"/>.</summary>
        public static void WriteIconSet(string catalogDirectory, string name, string imagePath)
        {
            string set = Path.Combine(catalogDirectory, name + ".appiconset");
            Directory.CreateDirectory(set);
            File.WriteAllBytes(Path.Combine(set, "icon.png"), ToIconPng(File.ReadAllBytes(imagePath), name));
            File.WriteAllText(Path.Combine(set, "Contents.json"), ContentsJson);
            File.WriteAllText(Path.Combine(set, Marker), "Written by GameLib App Icon.\n");
        }

        /// <summary>Deletes icon sets GameLib wrote earlier (an Xcode "Append" build) that are not in <paramref name="keep"/>.</summary>
        public static List<string> RemoveStaleIconSets(string catalogDirectory, ICollection<string> keep)
        {
            var removed = new List<string>();
            if (!Directory.Exists(catalogDirectory)) return removed;
            foreach (string set in Directory.GetDirectories(catalogDirectory, "*.appiconset"))
            {
                string name = Path.GetFileNameWithoutExtension(set);
                if (keep.Contains(name) || !File.Exists(Path.Combine(set, Marker))) continue;
                Directory.Delete(set, true);
                removed.Add(name);
            }
            return removed;
        }

        /// <summary>
        /// The ASSETCATALOG_COMPILER_ALTERNATE_APPICON_NAMES value: <paramref name="existing"/> (set by another plugin)
        /// without the names of <paramref name="removed"/> sets, plus <paramref name="names"/>.
        /// </summary>
        public static string MergeIconNames(string existing, IEnumerable<string> names, ICollection<string> removed = null)
        {
            var result = new List<string>();
            foreach (string n in (existing ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (!result.Contains(n) && (removed == null || !removed.Contains(n))) result.Add(n);
            foreach (string n in names)
                if (!result.Contains(n)) result.Add(n);
            return string.Join(" ", result);
        }

        /// <summary>
        /// The image as a 1024 x 1024 PNG without alpha (the App Store rejects transparent icons; transparent pixels
        /// become black). Resized on the CPU, so it also works in batch mode without a GPU.
        /// </summary>
        public static byte[] ToIconPng(byte[] image, string name = null)
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var icon = new Texture2D(IconSize, IconSize, TextureFormat.RGB24, false);
            try
            {
                if (!source.LoadImage(image))
                    throw new InvalidOperationException($"App Icon '{name}': the image is not a PNG or JPG.");
                source.wrapMode = TextureWrapMode.Clamp; // edge pixels must not blend with the opposite edge
                if (source.width != source.height)
                    Debug.LogWarning($"[GameLib.AppIcon] Icon '{name}' is {source.width} x {source.height}, not square - it is stretched.");
                var pixels = new Color32[IconSize * IconSize];
                for (int y = 0; y < IconSize; y++)
                {
                    float v = (y + 0.5f) / IconSize;
                    for (int x = 0; x < IconSize; x++)
                    {
                        Color c = source.GetPixelBilinear((x + 0.5f) / IconSize, v);
                        pixels[y * IconSize + x] = new Color(c.r * c.a, c.g * c.a, c.b * c.a, 1f);
                    }
                }
                icon.SetPixels32(pixels);
                icon.Apply();
                return icon.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(icon);
            }
        }

        /// <summary>The icons of <paramref name="module"/> that the build can write: (name, image path).</summary>
        public static List<(string Name, string Path)> BuildableIcons(AppIconModule module)
        {
            var result = new List<(string, string)>();
            if (module == null) return result;
            foreach (AppIconEntry icon in module.Icons)
            {
                string iconName = icon?.Name?.Trim();
                if (!AppIconModule.IsValidName(iconName)) continue;
                string path = string.IsNullOrEmpty(icon.ImageGuid) ? null : UnityEditor.AssetDatabase.GUIDToAssetPath(icon.ImageGuid);
                // Full path: an image inside a package lives in PackageCache, not under "Packages/".
                if (string.IsNullOrEmpty(path) || !File.Exists(Path.GetFullPath(path)))
                {
                    Debug.LogWarning($"[GameLib.AppIcon] Icon '{iconName}' has no image; left out of the build.");
                    continue;
                }
                result.Add((iconName, Path.GetFullPath(path)));
            }
            return result;
        }
    }
}
