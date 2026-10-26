using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace FlapGame.GameLib.Editor
{
    /// <summary>What GameLib Setup reads from and writes to the open project: installed packages, the settings asset, module assets.</summary>
    public static class GameLibProject
    {
        public const string RootFolder = "Assets/GameLib";
        public const string ResourcesFolder = RootFolder + "/Resources";
        public const string ModulesFolder = RootFolder + "/Modules";
        public const string SettingsAssetPath = ResourcesFolder + "/" + GameLibSettings.ResourcesPath + ".asset";
        public const string RepositoryUrl = "https://github.com/flap-game/GameLib.git";

        public static Dictionary<string, PackageInfo> InstalledPackages() =>
            PackageInfo.GetAllRegisteredPackages().ToDictionary(p => p.name);

        public static bool IsInstalled(GameLibModuleInfo module, IReadOnlyDictionary<string, PackageInfo> installed) =>
            installed.ContainsKey(module.PackageName);

        /// <summary>
        /// The manifest identifier for a GameLib package, matching how core itself was installed: the same git
        /// repository at tag <c>v&lt;version&gt;</c>, or <c>name@version</c> from a registry.
        /// </summary>
        public static string InstallIdentifier(string packageName)
        {
            PackageInfo core = PackageInfo.FindForAssembly(typeof(GameLibSettings).Assembly);
            string version = core?.version ?? GameLibVersion.Current;
            if (core != null && core.source == PackageSource.Registry)
                return $"{packageName}@{version}";

            string repo = RepositoryUrl;
            if (core != null && core.source == PackageSource.Git && !string.IsNullOrEmpty(core.packageId))
            {
                // packageId = "com.flapgame.gamelib.core@https://host/repo.git?path=...#tag" - keep the host/repo the game used.
                string url = core.packageId.Substring(core.packageId.IndexOf('@') + 1);
                int cut = url.IndexOfAny(new[] { '?', '#' });
                if (cut > 0) repo = url.Substring(0, cut);
            }
            return $"{repo}?path=Packages/{packageName}#v{version}";
        }

        public static GameLibSettings LoadSettings() => AssetDatabase.LoadAssetAtPath<GameLibSettings>(SettingsAssetPath);

        public static GameLibSettings LoadOrCreateSettings()
        {
            GameLibSettings settings = LoadSettings();
            if (settings != null)
                return settings;

            EnsureFolder(ResourcesFolder);
            settings = ScriptableObject.CreateInstance<GameLibSettings>();
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        public static Type FindModuleType(GameLibModuleInfo module) =>
            module.ModuleTypeName == null
                ? null
                : TypeCache.GetTypesDerivedFrom<GameLibModule>().FirstOrDefault(t => t.FullName == module.ModuleTypeName);

        /// <summary>The module's asset listed in the settings, else any asset of its type in the project.</summary>
        public static GameLibModule FindModuleAsset(GameLibModuleInfo module, GameLibSettings settings)
        {
            Type type = FindModuleType(module);
            if (type == null)
                return null;

            GameLibModule listed = settings != null ? settings.Modules.FirstOrDefault(m => m != null && m.GetType() == type) : null;
            if (listed != null)
                return listed;

            string guid = AssetDatabase.FindAssets("t:" + type.Name).FirstOrDefault();
            return guid != null ? AssetDatabase.LoadAssetAtPath<GameLibModule>(AssetDatabase.GUIDToAssetPath(guid)) : null;
        }

        /// <summary>Creates the module asset if needed and lists it in the settings.</summary>
        public static GameLibModule AddModuleAsset(GameLibModuleInfo module)
        {
            Type type = FindModuleType(module) ?? throw new InvalidOperationException($"Module type {module.ModuleTypeName} not found. Is {module.PackageName} installed?");
            GameLibSettings settings = LoadOrCreateSettings();

            GameLibModule asset = FindModuleAsset(module, settings);
            if (asset == null)
            {
                EnsureFolder(ModulesFolder);
                asset = (GameLibModule)ScriptableObject.CreateInstance(type);
                AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath($"{ModulesFolder}/{type.Name}.asset"));
            }

            if (settings.AddModule(asset))
                EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return asset;
        }

        /// <summary>Everything that is wrong with the GameLib setup of this project.</summary>
        public static List<GameLibIssue> Diagnose()
        {
            var issues = new List<GameLibIssue>();
            GameLibSettings settings = LoadSettings();
            if (settings == null)
            {
                issues.Add(GameLibIssue.Warning($"No {SettingsAssetPath}. GameLib starts with core services only."));
                return issues;
            }

            Dictionary<string, PackageInfo> installed = InstalledPackages();
            foreach (GameLibModuleInfo module in GameLibModuleCatalog.All)
            {
                if (!IsInstalled(module, installed) || module.ModuleTypeName == null)
                    continue;

                Type type = FindModuleType(module);
                if (type != null && !settings.Modules.Any(m => m != null && m.GetType() == type))
                    issues.Add(GameLibIssue.Warning($"{module.DisplayName} is installed but its module asset is not in GameLibSettings, so it never starts."));

                foreach (GameLibModuleInfo dependency in GameLibModuleCatalog.WithDependencies(module))
                {
                    if (!IsInstalled(dependency, installed))
                        issues.Add(GameLibIssue.Error($"{module.DisplayName} needs {dependency.PackageName}, which is not installed."));
                }

                foreach (SdkRequirement sdk in module.Sdks)
                {
                    if (!installed.ContainsKey(sdk.PackageName))
                        issues.Add(GameLibIssue.Error($"{module.DisplayName} needs {sdk.DisplayName} ({sdk.PackageName}), which is not installed."));
                }
            }

            for (int i = 0; i < settings.Modules.Count; i++)
            {
                GameLibModule asset = settings.Modules[i];
                if (asset == null)
                {
                    issues.Add(GameLibIssue.Warning($"GameLibSettings module slot {i} is empty."));
                    continue;
                }

                var moduleIssues = new List<GameLibIssue>();
                asset.Validate(moduleIssues);
                issues.AddRange(moduleIssues.Select(issue => new GameLibIssue(issue.Severity, $"{asset.name}: {issue.Message}")));
            }

            return issues;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
