using System;
using System.Collections.Generic;
using System.Linq;

namespace FlapGame.GameLib.Editor
{
    /// <summary>How a third-party SDK a module needs gets into the project.</summary>
    public enum SdkSource
    {
        /// <summary>A package on the Unity registry (resolved by Unity, e.g. com.unity.purchasing).</summary>
        UnityRegistry = 0,
        /// <summary>A git URL in the manifest.</summary>
        Git = 1,
        /// <summary>A package on a vendor scoped registry (needs the registry entry in the manifest).</summary>
        ScopedRegistry = 2,
        /// <summary>A .tgz downloaded from the vendor and referenced as <c>file:</c>.</summary>
        Tarball = 3,
    }

    public sealed class SdkRequirement
    {
        public string DisplayName { get; set; }
        public string PackageName { get; set; }
        public SdkSource Source { get; set; }
        /// <summary>Version (UnityRegistry / ScopedRegistry), git URL (Git) or download URL (Tarball).</summary>
        public string Location { get; set; }
        public string RegistryName { get; set; }
        public string RegistryUrl { get; set; }
        public string[] RegistryScopes { get; set; } = Array.Empty<string>();
    }

    public sealed class GameLibModuleInfo
    {
        /// <summary>Short id, also the package suffix: <c>ads.max</c> -> <c>com.flapgame.gamelib.ads.max</c>.</summary>
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        /// <summary>Ids of other GameLib modules this one needs (core is implicit).</summary>
        public string[] DependsOn { get; set; } = Array.Empty<string>();
        /// <summary>Full name of the module's <see cref="GameLibModule"/> subclass, or null when it has no asset.</summary>
        public string ModuleTypeName { get; set; }
        public SdkRequirement[] Sdks { get; set; } = Array.Empty<SdkRequirement>();
        /// <summary>False while the module is planned but not released yet.</summary>
        public bool Available { get; set; } = true;

        public string PackageName => GameLibModuleCatalog.PackagePrefix + Id;
    }

    /// <summary>Every module GameLib Setup knows about, in the order it lists them.</summary>
    public static class GameLibModuleCatalog
    {
        public const string PackagePrefix = "com.flapgame.gamelib.";
        public const string CorePackageName = PackagePrefix + "core";

        /// <summary>Google's External Dependency Manager, needed by MAX, Firebase and AppsFlyer; served by OpenUPM.</summary>
        public static readonly SdkRequirement ExternalDependencyManager = new()
        {
            DisplayName = "External Dependency Manager",
            PackageName = "com.google.external-dependency-manager",
            Source = SdkSource.ScopedRegistry,
            RegistryName = "package.openupm.com",
            RegistryUrl = "https://package.openupm.com",
            RegistryScopes = new[] { "com.google.external-dependency-manager" },
        };

        /// <summary>Firebase Unity SDK version every Firebase module installs (all Firebase packages must match).</summary>
        public const string FirebaseVersion = "14.1.0";

        /// <summary>A Firebase package from Google's tarball archive, e.g. <c>analytics</c> -> com.google.firebase.analytics.</summary>
        public static SdkRequirement FirebaseSdk(string displayName, string product)
        {
            string package = "com.google.firebase." + product;
            return new SdkRequirement
            {
                DisplayName = displayName,
                PackageName = package,
                Source = SdkSource.Tarball,
                Location = $"https://dl.google.com/games/registry/unity/{package}/{package}-{FirebaseVersion}.tgz",
            };
        }

        public static IReadOnlyList<GameLibModuleInfo> All { get; } = new[]
        {
            new GameLibModuleInfo
            {
                Id = "haptics",
                DisplayName = "Haptics",
                Description = "Vibration and haptic presets on Android and iOS, with a persisted on/off toggle.",
                ModuleTypeName = "FlapGame.GameLib.Haptics.HapticsModule",
            },
            new GameLibModuleInfo
            {
                Id = "ads",
                DisplayName = "Ads",
                Description = "Placement-based interstitial, rewarded, banner and app open ads, with per-game show rules.",
                ModuleTypeName = "FlapGame.GameLib.Ads.AdsModule",
            },
            new GameLibModuleInfo
            {
                Id = "ads.max",
                DisplayName = "Ads - AppLovin MAX",
                Description = "AppLovin MAX adapter and MAX consent flow for the Ads module.",
                DependsOn = new[] { "ads" },
                ModuleTypeName = null,
                Sdks = new[]
                {
                    // The MAX plugin compiles against uGUI and UnityWebRequest; minimal projects lack them.
                    new SdkRequirement { DisplayName = "Unity UI", PackageName = "com.unity.ugui", Source = SdkSource.UnityRegistry, Location = "2.0.0" },
                    new SdkRequirement { DisplayName = "Unity Web Request module", PackageName = "com.unity.modules.unitywebrequest", Source = SdkSource.UnityRegistry, Location = "1.0.0" },
                    ExternalDependencyManager,
                    new SdkRequirement
                    {
                        DisplayName = "AppLovin MAX SDK",
                        PackageName = "com.applovin.mediation.ads",
                        Source = SdkSource.ScopedRegistry,
                        RegistryName = "AppLovin MAX Unity",
                        RegistryUrl = "https://unity.packages.applovin.com/",
                        RegistryScopes = new[] { "com.applovin.mediation.ads", "com.applovin.mediation.adapters", "com.applovin.mediation.dsp" },
                    },
                },
            },
            new GameLibModuleInfo
            {
                Id = "iap",
                DisplayName = "In-App Purchases",
                Description = "Unity IAP 5: consumables, non-consumables and subscriptions, localized prices, restore.",
                ModuleTypeName = "FlapGame.GameLib.Iap.IapModule",
                Sdks = new[]
                {
                    new SdkRequirement { DisplayName = "Unity IAP", PackageName = "com.unity.purchasing", Source = SdkSource.UnityRegistry, Location = "5.4.4" },
                },
            },
            new GameLibModuleInfo
            {
                Id = "firebase",
                DisplayName = "Firebase",
                Description = "Shared Firebase start for Analytics, Crashlytics and Remote Config. Add one of those; this comes with it.",
                ModuleTypeName = null,
                Sdks = new[] { ExternalDependencyManager, FirebaseSdk("Firebase App", "app") },
            },
            new GameLibModuleInfo
            {
                Id = "analytics.firebase",
                DisplayName = "Analytics - Firebase",
                Description = "Firebase Analytics sink: game events within Firebase limits, ad_impression revenue, consent mode.",
                DependsOn = new[] { "firebase" },
                ModuleTypeName = "FlapGame.GameLib.Analytics.Firebase.FirebaseAnalyticsModule",
                Sdks = new[] { FirebaseSdk("Firebase Analytics", "analytics") },
            },
            new GameLibModuleInfo
            {
                Id = "crashlytics",
                DisplayName = "Crashlytics",
                Description = "Firebase Crashlytics as ICrashReporter, with analytics breadcrumbs and the shared user id.",
                DependsOn = new[] { "firebase" },
                ModuleTypeName = "FlapGame.GameLib.Crashlytics.CrashlyticsModule",
                Sdks = new[] { FirebaseSdk("Firebase Crashlytics", "crashlytics") },
            },
            new GameLibModuleInfo
            {
                Id = "remoteconfig.firebase",
                DisplayName = "Remote Config - Firebase",
                Description = "IRemoteConfig on Firebase Remote Config: defaults in the asset, last fetch cached on disk. Ads reads its plan from it.",
                DependsOn = new[] { "firebase" },
                ModuleTypeName = "FlapGame.GameLib.RemoteConfig.RemoteConfigModule",
                Sdks = new[] { FirebaseSdk("Firebase Remote Config", "remote-config") },
            },
            new GameLibModuleInfo
            {
                Id = "appsflyer",
                DisplayName = "AppsFlyer",
                Description = "AppsFlyer attribution, ad revenue, purchases (manual or Purchase Connector), ATT and consent.",
                ModuleTypeName = "FlapGame.GameLib.AppsFlyer.AppsFlyerModule",
                Sdks = new[]
                {
                    ExternalDependencyManager,
                    // 6.17.900: the last 6.x. 7.0.x also works with GameLib but ships its Android Purchase Connector disabled.
                    new SdkRequirement
                    {
                        DisplayName = "AppsFlyer Unity plugin",
                        PackageName = "appsflyer-unity-plugin",
                        Source = SdkSource.Git,
                        Location = "https://github.com/AppsFlyerSDK/appsflyer-unity-plugin.git#v6.17.900",
                    },
                },
            },
        };

        public static GameLibModuleInfo Find(string id) => All.FirstOrDefault(m => m.Id == id);

        /// <summary><paramref name="module"/> and every module it needs, dependencies first.</summary>
        public static IReadOnlyList<GameLibModuleInfo> WithDependencies(GameLibModuleInfo module)
        {
            var result = new List<GameLibModuleInfo>();
            Visit(module, result, new HashSet<string>());
            return result;
        }

        private static void Visit(GameLibModuleInfo module, List<GameLibModuleInfo> result, HashSet<string> visiting)
        {
            if (module == null || result.Contains(module))
                return;
            if (!visiting.Add(module.Id))
                throw new InvalidOperationException($"Module dependency cycle at '{module.Id}'.");

            foreach (string dependency in module.DependsOn)
            {
                GameLibModuleInfo info = Find(dependency) ?? throw new InvalidOperationException($"'{module.Id}' depends on unknown module '{dependency}'.");
                Visit(info, result, visiting);
            }

            result.Add(module);
        }
    }
}
