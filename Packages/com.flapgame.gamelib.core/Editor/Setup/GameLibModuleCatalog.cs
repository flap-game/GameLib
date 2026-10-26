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
                Available = false,
                Sdks = new[]
                {
                    new SdkRequirement { DisplayName = "Unity IAP", PackageName = "com.unity.purchasing", Source = SdkSource.UnityRegistry, Location = "5.3.1" },
                },
            },
            new GameLibModuleInfo
            {
                Id = "firebase",
                DisplayName = "Firebase",
                Description = "Shared Firebase initialization for Analytics and Crashlytics.",
                ModuleTypeName = "FlapGame.GameLib.Firebase.FirebaseModule",
                Available = false,
            },
            new GameLibModuleInfo
            {
                Id = "analytics.firebase",
                DisplayName = "Analytics - Firebase",
                Description = "Firebase Analytics sink: game events, ad_impression and purchase events.",
                DependsOn = new[] { "firebase" },
                ModuleTypeName = "FlapGame.GameLib.Analytics.Firebase.FirebaseAnalyticsModule",
                Available = false,
            },
            new GameLibModuleInfo
            {
                Id = "crashlytics",
                DisplayName = "Crashlytics",
                Description = "Firebase Crashlytics with analytics breadcrumbs and custom keys.",
                DependsOn = new[] { "firebase" },
                ModuleTypeName = "FlapGame.GameLib.Crashlytics.CrashlyticsModule",
                Available = false,
            },
            new GameLibModuleInfo
            {
                Id = "appsflyer",
                DisplayName = "AppsFlyer",
                Description = "AppsFlyer attribution, ad revenue, purchases (manual or Purchase Connector), ATT and consent.",
                ModuleTypeName = "FlapGame.GameLib.AppsFlyer.AppsFlyerModule",
                Available = false,
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
