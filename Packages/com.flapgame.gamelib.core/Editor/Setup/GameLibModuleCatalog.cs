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
        /// <summary>
        /// Optional SDKs only: the CLI key (<c>-gamelibOptional</c>). Null means the last part of <see cref="PackageName"/>.
        /// </summary>
        public string Key { get; set; }
        /// <summary>
        /// Optional SDKs only: packages installed and removed together with this one (MAX ships each network's adapter as
        /// an <c>.android</c> and an <c>.ios</c> package).
        /// </summary>
        public SdkRequirement[] Companions { get; set; } = Array.Empty<SdkRequirement>();
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
        /// <summary>
        /// SDKs the game picks from (AdMob's mediation adapters): Setup installs and removes each one, Doctor never asks for
        /// them. CLI: <c>-gamelibOptional</c> with <see cref="GameLibModuleCatalog.OptionalKey"/>s.
        /// </summary>
        public SdkRequirement[] OptionalSdks { get; set; } = Array.Empty<SdkRequirement>();
        /// <summary>Heading of <see cref="OptionalSdks"/> in Setup.</summary>
        public string OptionalSdksTitle { get; set; }
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

        /// <summary>
        /// Firebase Unity SDK version a new install gets (all Firebase packages must match). The GameLib Firebase code
        /// compiles against 13.16.0 and 14.1.0; Doctor checks the installed version (<see cref="SupportedFirebaseMajors"/>).
        /// </summary>
        public const string FirebaseVersion = "13.16.0";

        /// <summary>Firebase major versions GameLib is checked against.</summary>
        public static readonly int[] SupportedFirebaseMajors = { 13, 14 };

        /// <summary>
        /// A Firebase package from the GameWorkstore git mirror of Google's packages, e.g. <c>analytics</c> ->
        /// <c>https://github.com/GameWorkstore/com.google.firebase.analytics.git#13.16.0</c> - the source CoreGame games use.
        /// Nothing to commit (Google's tarballs are about 60 MB per game), but the mirror trails Google: 13.16.0 is its
        /// newest tag. For 14.x take the tarballs from https://developers.google.com/unity/archive yourself
        /// (<see cref="FirebaseTarballSdk"/>); Setup leaves Firebase entries already in the manifest alone.
        /// </summary>
        public static SdkRequirement FirebaseSdk(string displayName, string product)
        {
            string package = "com.google.firebase." + product;
            return new SdkRequirement
            {
                DisplayName = displayName,
                PackageName = package,
                Source = SdkSource.Git,
                Location = $"https://github.com/GameWorkstore/{package}.git#{FirebaseVersion}",
            };
        }

        /// <summary>The same package from Google's tarball archive (downloaded into Packages/GoogleTgz and committed).</summary>
        public static SdkRequirement FirebaseTarballSdk(string displayName, string product, string version)
        {
            string package = "com.google.firebase." + product;
            return new SdkRequirement
            {
                DisplayName = displayName,
                PackageName = package,
                Source = SdkSource.Tarball,
                Location = $"https://dl.google.com/games/registry/unity/{package}/{package}-{version}.tgz",
            };
        }

        /// <summary>
        /// Google's mediation adapter for one network, from OpenUPM (<c>com.google.ads.mobile.mediation.&lt;network&gt;</c>,
        /// under the <c>com.google.ads.mobile</c> scope). Versions pinned 2026-11-06 from the AdMob + MAX version set
        /// (<c>com.flapgame.gamelib.ads/Documentation~/mediation-versions.json</c>, checked by
        /// <c>Tools/mediation_versions.py check</c>): each one's native SDK equals what the matching MAX adapter pins, so a
        /// game with both stacks still passes pod install. Change them there first. Each needs Google Mobile Ads 11.0.0 or
        /// later (Verve 11.2.0) - GameLib installs 11.6.0. Verve is 1.1.3, not OpenUPM's "latest" 3.38.0: that is a broken
        /// publish (myTarget's adapter inside, an iOS pod that does not exist).
        /// </summary>
        public static SdkRequirement AdMobMediation(string network, string displayName, string version) => new()
        {
            DisplayName = displayName,
            PackageName = "com.google.ads.mobile.mediation." + network,
            Source = SdkSource.ScopedRegistry,
            Location = version,
            RegistryName = "package.openupm.com",
            RegistryUrl = "https://package.openupm.com",
            RegistryScopes = new[] { "com.google.ads.mobile" },
        };

        /// <summary>The AppLovin MAX scoped registry: the MAX SDK and its adapters.</summary>
        public const string MaxRegistryUrl = "https://unity.packages.applovin.com/";
        private static readonly string[] MaxRegistryScopes = { "com.applovin.mediation.ads", "com.applovin.mediation.adapters", "com.applovin.mediation.dsp" };

        /// <summary>
        /// AppLovin MAX SDK version a new install gets. Pinned with the AdMob + MAX version set: Google's AppLovin adapter
        /// pins the AppLovin iOS SDK exactly (8.6.6 ships 13.6.4), so MAX core and that adapter move together.
        /// </summary>
        public const string MaxVersion = "8.6.6";

        /// <summary>
        /// AppLovin's adapter for one MAX network: <c>com.applovin.mediation.adapters.&lt;network&gt;.android</c> plus its
        /// <c>.ios</c> twin, installed and removed together. CLI key <c>max-&lt;network&gt;</c> (AdMob's adapters use the bare
        /// network name). Versions pinned 2026-11-06 from the AdMob + MAX version set, like <see cref="AdMobMediation"/>.
        /// Networks MAX 8.6.6 treats as obsolete (HyprMX, maio, myTarget, CSJ, Tencent) are left out: it uninstalls them.
        /// </summary>
        public static SdkRequirement MaxMediation(string network, string displayName, string androidVersion, string iosVersion) => new()
        {
            DisplayName = displayName,
            PackageName = $"com.applovin.mediation.adapters.{network}.android",
            Source = SdkSource.ScopedRegistry,
            Location = androidVersion,
            RegistryName = "AppLovin MAX Unity",
            RegistryUrl = MaxRegistryUrl,
            RegistryScopes = MaxRegistryScopes,
            Key = "max-" + network,
            Companions = new[]
            {
                new SdkRequirement
                {
                    DisplayName = displayName + " (iOS)",
                    PackageName = $"com.applovin.mediation.adapters.{network}.ios",
                    Source = SdkSource.ScopedRegistry,
                    Location = iosVersion,
                    RegistryName = "AppLovin MAX Unity",
                    RegistryUrl = MaxRegistryUrl,
                    RegistryScopes = MaxRegistryScopes,
                },
            },
        };

        /// <summary>
        /// The short name of an optional SDK for the CLI: <see cref="SdkRequirement.Key"/>, else the last part of its package
        /// name (<c>pangle</c>).
        /// </summary>
        public static string OptionalKey(SdkRequirement sdk) => sdk.Key ?? sdk.PackageName.Substring(sdk.PackageName.LastIndexOf('.') + 1);

        /// <summary>An optional SDK with its <see cref="SdkRequirement.Companions"/>.</summary>
        public static IEnumerable<SdkRequirement> WithCompanions(SdkRequirement sdk) => new[] { sdk }.Concat(sdk.Companions);

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
                Id = "save",
                DisplayName = "Save",
                Description = "Save game data: crash-safe file with backup and checksum, versioned sections with migrations, autosave, optional encryption.",
                ModuleTypeName = "FlapGame.GameLib.Save.SaveModule",
            },
            new GameLibModuleInfo
            {
                Id = "extensions",
                DisplayName = "Extensions",
                Description = "Everyday helpers: random picks and shuffles, FindDeep, DestroyChildren, vector / color / UI extensions, number and time formatting, Delay.",
                ModuleTypeName = null,
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
                        Location = MaxVersion,
                        RegistryName = "AppLovin MAX Unity",
                        RegistryUrl = MaxRegistryUrl,
                        RegistryScopes = MaxRegistryScopes,
                    },
                },
                OptionalSdksTitle = "Mediation adapters (one per network in the MAX ad units; Android + iOS together)",
                OptionalSdks = new[]
                {
                    MaxMediation("bidmachine", "BidMachine", "3080001.0.0", "308020000.0.0"),
                    MaxMediation("bigoads", "BIGO Ads", "6010000.0.0", "6010001.0.0"),
                    MaxMediation("chartboost", "Chartboost", "9140101.0.0", "9140200.0.0"),
                    MaxMediation("fyber", "DT Exchange", "8050000.0.0", "8050100.0.0"),
                    MaxMediation("googleadmanager", "Google Ad Manager", "25050000.0.0", "13110000.0.0"),
                    MaxMediation("google", "Google AdMob", "25050000.0.0", "13110000.0.0"),
                    MaxMediation("inmobi", "InMobi", "11050001.0.0", "11050000.0.0"),
                    MaxMediation("ironsource", "ironSource", "907000000.0.0", "907000000.0.0"),
                    MaxMediation("vungle", "Liftoff Monetize", "7070400.0.0", "7070300.0.0"),
                    MaxMediation("line", "LINE", "300001010.1.0", "3000100.0.0"),
                    MaxMediation("facebook", "Meta Audience Network", "6220001.0.0", "6220004.0.0"),
                    MaxMediation("mintegral", "Mintegral", "17019100.0.0", "801080000.0.0"),
                    MaxMediation("mobilefuse", "MobileFuse", "1130100.0.0", "1130000.0.0"),
                    MaxMediation("moloco", "Moloco", "4130001.0.0", "4110101.0.0"),
                    MaxMediation("ogurypresage", "Ogury", "6030101.0.0", "5030100.0.0"),
                    MaxMediation("bytedance", "Pangle", "803000401.0.0", "803000800.0.0"),
                    MaxMediation("pubmatic", "PubMatic", "5040100.0.0", "5040100.0.0"),
                    MaxMediation("smaato", "Smaato", "23020300.0.0", "23020300.0.0"),
                    MaxMediation("unityads", "Unity Ads", "4210001.0.0", "4210001.0.0"),
                    MaxMediation("verve", "Verve", "3090300.0.0", "3090300.0.0"),
                    MaxMediation("yandex", "Yandex", "8060000.0.0", "8060000.0.0"),
                    MaxMediation("ysonetwork", "YSO Network", "1030900.0.0", "1020100.0.0"),
                },
            },
            new GameLibModuleInfo
            {
                Id = "ads.admob",
                DisplayName = "Ads - AdMob",
                Description = "Google AdMob adapter and Google UMP consent for the Ads module.",
                DependsOn = new[] { "ads" },
                ModuleTypeName = null,
                Sdks = new[]
                {
                    new SdkRequirement
                    {
                        DisplayName = "Google Mobile Ads",
                        PackageName = "com.google.ads.mobile",
                        Source = SdkSource.ScopedRegistry,
                        Location = "11.6.0",
                        RegistryName = "package.openupm.com",
                        RegistryUrl = "https://package.openupm.com",
                        RegistryScopes = new[] { "com.google.ads.mobile" },
                    },
                },
                OptionalSdksTitle = "Mediation adapters (one per network in the AdMob mediation groups)",
                OptionalSdks = new[]
                {
                    AdMobMediation("applovin", "AppLovin", "8.7.7"),
                    AdMobMediation("bidmachine", "BidMachine", "1.5.3"),
                    AdMobMediation("bigo", "Bigo", "3.1.0"),
                    AdMobMediation("chartboost", "Chartboost", "4.15.2"),
                    AdMobMediation("dtexchange", "DT Exchange", "3.7.0"),
                    AdMobMediation("imobile", "i-mobile", "1.3.16"),
                    AdMobMediation("inmobi", "InMobi", "5.4.1"),
                    AdMobMediation("ironsource", "ironSource", "4.9.0"),
                    AdMobMediation("liftoffmonetize", "Liftoff Monetize", "5.7.10"),
                    AdMobMediation("line", "LINE", "2.2.1"),
                    AdMobMediation("maio", "maio", "3.1.11"),
                    AdMobMediation("metaaudiencenetwork", "Meta Audience Network", "3.20.1"),
                    AdMobMediation("mintegral", "Mintegral", "2.2.7"),
                    AdMobMediation("moloco", "Moloco", "3.11.0"),
                    AdMobMediation("mytarget", "myTarget", "3.43.0"),
                    AdMobMediation("pangle", "Pangle", "7.2.2"),
                    AdMobMediation("pubmatic", "PubMatic", "2.4.1"),
                    AdMobMediation("unity", "Unity Ads", "3.22.0"),
                    AdMobMediation("verve", "Verve", "1.1.3"),
                },
            },
            new GameLibModuleInfo
            {
                Id = "att",
                DisplayName = "App Tracking Transparency",
                Description = "iOS ATT prompt after the consent form (optional delay) or when the game asks. Nothing on Android.",
                ModuleTypeName = "FlapGame.GameLib.Att.AttModule",
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
                Id = "analytics.local",
                DisplayName = "Analytics - Local",
                Description = "Local tracking for the test devices: on the office LAN every event goes to the internal tracking server and the online sinks stay off.",
                ModuleTypeName = "FlapGame.GameLib.Analytics.Local.LocalAnalyticsModule",
                Sdks = new[]
                {
                    new SdkRequirement { DisplayName = "Unity Web Request module", PackageName = "com.unity.modules.unitywebrequest", Source = SdkSource.UnityRegistry, Location = "1.0.0" },
                    // The Android advertising id is read through Play Services over JNI.
                    new SdkRequirement { DisplayName = "Android JNI module", PackageName = "com.unity.modules.androidjni", Source = SdkSource.UnityRegistry, Location = "1.0.0" },
                },
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
                Description = "AppsFlyer attribution, ad revenue, purchases (manual or Purchase Connector), consent; starts after the ATT answer.",
                DependsOn = new[] { "att" },
                ModuleTypeName = "FlapGame.GameLib.AppsFlyer.AppsFlyerModule",
                Sdks = new[]
                {
                    ExternalDependencyManager,
                    // 7.0.3 leaves the Android Purchase Connector out; GameLib then logs af_purchase on Android (AppsFlyerModule).
                    new SdkRequirement
                    {
                        DisplayName = "AppsFlyer Unity plugin",
                        PackageName = "appsflyer-unity-plugin",
                        Source = SdkSource.Git,
                        Location = "https://github.com/AppsFlyerSDK/appsflyer-unity-plugin.git#v7.0.3",
                    },
                },
            },
            new GameLibModuleInfo
            {
                Id = "rate",
                DisplayName = "Rate",
                Description = "Rate-us prompts: when to ask (every Nth trigger, caps), the store review sheet on iOS and Google Play.",
                ModuleTypeName = "FlapGame.GameLib.Rate.RateModule",
            },
            new GameLibModuleInfo
            {
                Id = "appicon",
                DisplayName = "App Icon",
                Description = "Alternate app icons the player can pick (iOS), built into the Xcode project.",
                ModuleTypeName = "FlapGame.GameLib.AppIcon.AppIconModule",
            },
            new GameLibModuleInfo
            {
                Id = "build",
                DisplayName = "Cloud Build",
                Description = "Unity Cloud Build: Unity splash off, App Store upload script for iOS builds.",
                ModuleTypeName = null,
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
