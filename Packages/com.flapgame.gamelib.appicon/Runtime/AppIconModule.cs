using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.AppIcon
{
    /// <summary>One alternate icon: its name in code and the 1024 x 1024 image built into the iOS app.</summary>
    [Serializable]
    public sealed class AppIconEntry
    {
        [Tooltip("Name used in code (SetIconAsync) and in the Xcode asset catalog. Letters, digits, '-' and '_'.")]
        public string Name;

        // A GUID rather than a Texture2D: the module asset is in the build, and the images are only needed by Xcode.
        [Tooltip("Image of the icon (square PNG / JPG, best 1024 x 1024). Only the iOS build step reads it.")]
        public string ImageGuid;
    }

    /// <summary>Registers <see cref="IAppIconService"/>. Created by GameLib > Setup.</summary>
    [CreateAssetMenu(menuName = "GameLib/Modules/App Icon", fileName = "AppIconModule")]
    public sealed class AppIconModule : GameLibModule
    {
        private static readonly Regex ValidName = new("^[A-Za-z0-9_-]+$");

        /// <summary>Unity's own app icon set in the Xcode asset catalog: an alternate icon with this name would replace it.</summary>
        public const string ReservedName = "AppIcon";

        [Tooltip("Alternate icons. The default icon is the Player Settings icon.")]
        public List<AppIconEntry> Icons = new();

        public override ValueTask InstallAsync(IServiceRegistry services, CancellationToken cancellationToken = default)
        {
            var names = new List<string>();
            foreach (AppIconEntry icon in Icons)
                if (icon != null && IsValidName(icon.Name?.Trim())) names.Add(icon.Name.Trim());
            services.TryResolve(out IAppLifecycle lifecycle);
            services.Register<IAppIconService>(new AppIconService(names, AppIconBackends.ForPlatform(), lifecycle));
            return default;
        }

        public static bool IsValidName(string name) =>
            !string.IsNullOrEmpty(name) && ValidName.IsMatch(name) && !string.Equals(name, ReservedName, StringComparison.OrdinalIgnoreCase);

        public override void Validate(ICollection<GameLibIssue> issues)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Icons.Count; i++)
            {
                AppIconEntry icon = Icons[i];
                string iconName = icon?.Name?.Trim();
                if (string.Equals(iconName, ReservedName, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(GameLibIssue.Error($"App Icon: '{iconName}' is the name of the main icon set - pick another name."));
                    continue;
                }
                if (!IsValidName(iconName))
                {
                    issues.Add(GameLibIssue.Error($"App Icon: icon #{i + 1} name '{iconName}' must be letters, digits, '-' or '_'."));
                    continue;
                }
                if (!seen.Add(iconName))
                    issues.Add(GameLibIssue.Error($"App Icon: two icons are named '{iconName}'."));
#if UNITY_EDITOR
                string path = string.IsNullOrEmpty(icon.ImageGuid) ? null : UnityEditor.AssetDatabase.GUIDToAssetPath(icon.ImageGuid);
                if (string.IsNullOrEmpty(path))
                    issues.Add(GameLibIssue.Error($"App Icon: icon '{iconName}' has no image."));
#endif
            }
        }
    }
}
