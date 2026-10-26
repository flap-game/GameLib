using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace FlapGame.GameLib.Editor
{
    /// <summary>
    /// GameLib Setup without the window, for scripts and agents. Batch mode:
    /// <code>
    /// Unity -batchmode -quit -projectPath P -executeMethod FlapGame.GameLib.Editor.GameLibCli.Install -gamelibModules ads.max,iap
    /// Unity -batchmode -quit -projectPath P -executeMethod FlapGame.GameLib.Editor.GameLibCli.Doctor
    /// </code>
    /// Each command writes a JSON report (<c>-gamelibReport &lt;path&gt;</c>, default <c>Logs/gamelib-&lt;command&gt;.json</c>),
    /// logs it after <see cref="ReportMarker"/> and exits with <see cref="ExitOk"/>, <see cref="ExitError"/> or
    /// <see cref="ExitRunAgain"/>. In an open Editor call <see cref="InstallModules"/> / <see cref="BuildDoctorReport"/>.
    /// </summary>
    public static class GameLibCli
    {
        public const int ExitOk = 0;
        public const int ExitError = 1;
        /// <summary>Install changed the manifest: Unity must resolve and compile the new packages, then Install runs again.</summary>
        public const int ExitRunAgain = 2;

        public const string ReportMarker = "[GameLib] report: ";
        public const string ModulesArgument = "-gamelibModules";
        public const string ReportArgument = "-gamelibReport";

        private static readonly TimeSpan PackageSearchTimeout = TimeSpan.FromMinutes(2);

        /// <summary>Batch mode entry point: <c>-gamelibModules id,id,...</c> (ids of <see cref="GameLibModuleCatalog"/>).</summary>
        public static void Install()
        {
            string[] ids = (Argument(ModulesArgument) ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            GameLibInstallReport report;
            try
            {
                report = InstallModules(ids);
            }
            catch (Exception e)
            {
                report = new GameLibInstallReport { Status = GameLibInstallStatus.Error };
                report.Errors.Add(e.ToString());
            }
            Finish("install", report, report.Status switch
            {
                GameLibInstallStatus.Done => ExitOk,
                GameLibInstallStatus.RunAgain => ExitRunAgain,
                _ => ExitError,
            });
        }

        /// <summary>Batch mode entry point: the state of every module plus <see cref="GameLibProject.Diagnose"/>.</summary>
        public static void Doctor()
        {
            GameLibDoctorReport report;
            try
            {
                report = BuildDoctorReport();
            }
            catch (Exception e)
            {
                report = new GameLibDoctorReport();
                report.Issues.Add(new GameLibCliIssue { Severity = "Error", Message = e.ToString() });
            }
            Finish("doctor", report, report.ErrorCount > 0 ? ExitError : ExitOk);
        }

        /// <summary>
        /// Installs the modules, the GameLib modules they need and their SDKs, then creates their module assets and lists
        /// them in GameLibSettings. Idempotent: run it until it returns <see cref="GameLibInstallStatus.Done"/>. A run that
        /// edits the manifest returns <see cref="GameLibInstallStatus.RunAgain"/>, because the module types only exist after
        /// Unity resolves and compiles the new packages.
        /// </summary>
        public static GameLibInstallReport InstallModules(IEnumerable<string> moduleIds)
        {
            var report = new GameLibInstallReport();
            var requested = new List<GameLibModuleInfo>();
            foreach (string id in moduleIds.Select(i => i.Trim()).Where(i => i.Length > 0).Distinct())
            {
                GameLibModuleInfo module = GameLibModuleCatalog.Find(id);
                if (module == null)
                    report.Errors.Add($"Unknown module '{id}'. Modules: {string.Join(", ", GameLibModuleCatalog.All.Select(m => m.Id))}.");
                else if (!module.Available)
                    report.Errors.Add($"Module '{id}' is not released yet.");
                else
                    requested.Add(module);
            }
            if (requested.Count == 0 && report.Errors.Count == 0)
                report.Errors.Add($"No module given. Pass {ModulesArgument} id,id,... Modules: {string.Join(", ", GameLibModuleCatalog.All.Select(m => m.Id))}.");
            if (report.Errors.Count > 0)
                return Done(report, GameLibInstallStatus.Error);

            List<GameLibModuleInfo> chain = requested.SelectMany(GameLibModuleCatalog.WithDependencies).Distinct().ToList();
            report.Modules.AddRange(chain.Select(m => m.Id));

            if (EditManifest(chain, report))
            {
                if (!Application.isBatchMode)
                    Client.Resolve();
                return Done(report, GameLibInstallStatus.RunAgain);
            }

            Dictionary<string, PackageInfo> installed = GameLibProject.InstalledPackages();
            foreach (GameLibModuleInfo module in chain)
            {
                if (!GameLibProject.IsInstalled(module, installed))
                {
                    report.Pending.Add($"{module.PackageName} is in the manifest but not resolved yet.");
                    continue;
                }
                if (module.ModuleTypeName == null)
                    continue;
                if (GameLibProject.FindModuleType(module) == null)
                {
                    report.Errors.Add($"{module.ModuleTypeName} is not compiled although {module.PackageName} is installed. Fix the " +
                                      "compile errors in the Editor log first.");
                    continue;
                }

                GameLibSettings settings = GameLibProject.LoadSettings();
                GameLibModule existing = GameLibProject.FindModuleAsset(module, settings);
                if (existing != null && settings != null && settings.Modules.Contains(existing))
                    continue;
                GameLibModule asset = GameLibProject.AddModuleAsset(module);
                report.Changes.Add($"Listed {AssetDatabase.GetAssetPath(asset)} in {GameLibProject.SettingsAssetPath}.");
            }

            report.Issues.AddRange(GameLibProject.Diagnose().Select(GameLibCliIssue.From));
            if (report.Errors.Count > 0)
                return Done(report, GameLibInstallStatus.Error);
            return Done(report, report.Pending.Count > 0 ? GameLibInstallStatus.RunAgain : GameLibInstallStatus.Done);
        }

        /// <summary>Every module of the catalog with its install state, and every issue of the project's GameLib setup.</summary>
        public static GameLibDoctorReport BuildDoctorReport()
        {
            var report = new GameLibDoctorReport { GameLibVersion = PackageInfo.FindForAssembly(typeof(GameLibSettings).Assembly)?.version ?? GameLibVersion.Current };
            Dictionary<string, PackageInfo> installed = GameLibProject.InstalledPackages();
            GameLibSettings settings = GameLibProject.LoadSettings();
            foreach (GameLibModuleInfo module in GameLibModuleCatalog.All)
            {
                bool isInstalled = GameLibProject.IsInstalled(module, installed);
                var state = new GameLibModuleState
                {
                    Id = module.Id,
                    Package = module.PackageName,
                    Installed = isInstalled,
                    Version = isInstalled ? installed[module.PackageName].version : null,
                };
                if (isInstalled)
                {
                    state.MissingSdks.AddRange(GameLibModuleCatalog.WithDependencies(module).SelectMany(m => m.Sdks).Distinct()
                                                                   .Where(s => !installed.ContainsKey(s.PackageName))
                                                                   .Select(s => s.PackageName));
                    if (module.ModuleTypeName != null)
                    {
                        GameLibModule asset = GameLibProject.FindModuleAsset(module, settings);
                        state.Asset = asset != null ? AssetDatabase.GetAssetPath(asset) : null;
                        state.AssetListed = asset != null && settings != null && settings.Modules.Contains(asset);
                        state.Settings = asset != null ? SerializedFields(asset) : null;
                    }
                }
                report.Modules.Add(state);
            }
            report.Issues.AddRange(GameLibProject.Diagnose().Select(GameLibCliIssue.From));
            report.ErrorCount = report.Issues.Count(i => i.Severity == nameof(GameLibIssueSeverity.Error));
            return report;
        }

        /// <summary>The asset's serialized fields as Unity saves them (enums as numbers), without Unity's own <c>m_</c> fields.</summary>
        private static JObject SerializedFields(UnityEngine.Object asset)
        {
            JObject root = JObject.Parse(EditorJsonUtility.ToJson(asset));
            JObject fields = root.Properties().FirstOrDefault()?.Value as JObject ?? root;
            foreach (JProperty unity in fields.Properties().Where(p => p.Name.StartsWith("m_", StringComparison.Ordinal)).ToList())
                unity.Remove();
            return fields;
        }

        /// <returns>True when the manifest changed.</returns>
        private static bool EditManifest(List<GameLibModuleInfo> chain, GameLibInstallReport report)
        {
            var manifest = new ManifestManager();
            bool changed = false;
            // Registered packages count too: embedded and local packages are not in the manifest's dependencies.
            Dictionary<string, PackageInfo> installed = GameLibProject.InstalledPackages();

            foreach (GameLibModuleInfo module in chain.Where(m => !manifest.HasDependency(m.PackageName) && !installed.ContainsKey(m.PackageName)))
            {
                string identifier = GameLibProject.InstallIdentifier(module.PackageName);
                manifest.SetDependency(module.PackageName, identifier);
                report.Changes.Add($"Added {module.PackageName}: {identifier}");
                changed = true;
            }

            List<SdkRequirement> sdks = chain.SelectMany(m => m.Sdks).Distinct().ToList();
            List<SdkRequirement> missing = sdks.Where(s => !manifest.HasDependency(s.PackageName) && !installed.ContainsKey(s.PackageName)).ToList();
            // Registries Unity loaded at start: only their packages can be looked up in this run.
            var knownRegistries = new HashSet<string>(sdks.Where(s => s.Source == SdkSource.ScopedRegistry && manifest.HasScopedRegistry(s.RegistryUrl))
                                                          .Select(s => s.RegistryUrl));
            if (missing.Count > 0)
            {
                // Every registry of the chain, so dependencies between the SDKs resolve (MAX / Firebase -> EDM on OpenUPM).
                foreach (SdkRequirement registry in sdks.Where(s => s.Source == SdkSource.ScopedRegistry))
                {
                    if (!manifest.HasScopedRegistry(registry.RegistryUrl))
                    {
                        report.Changes.Add($"Added scoped registry {registry.RegistryUrl}");
                        changed = true;
                    }
                    manifest.EnsureScopedRegistry(registry.RegistryName, registry.RegistryUrl, registry.RegistryScopes);
                }
            }
            foreach (SdkRequirement sdk in missing)
            {
                if (sdk.Source == SdkSource.ScopedRegistry && string.IsNullOrEmpty(sdk.Location) && !knownRegistries.Contains(sdk.RegistryUrl))
                {
                    // No pinned version: the newest one is looked up on the registry, which Unity only knows from the next start.
                    report.Pending.Add($"{sdk.PackageName}: its registry was just added; the next run looks up its version.");
                    continue;
                }
                string value = sdk.Source switch
                {
                    SdkSource.Git => sdk.Location,
                    SdkSource.UnityRegistry => sdk.Location,
                    SdkSource.ScopedRegistry => string.IsNullOrEmpty(sdk.Location) ? LatestVersion(sdk) : sdk.Location,
                    SdkSource.Tarball => DownloadTarball(sdk),
                    _ => throw new InvalidOperationException($"Unknown SDK source {sdk.Source}."),
                };
                manifest.SetDependency(sdk.PackageName, value);
                report.Changes.Add($"Added {sdk.PackageName}: {value}");
                changed = true;
            }

            if (changed)
                manifest.Save();
            return changed;
        }

        private static string DownloadTarball(SdkRequirement sdk)
        {
            TarballInstaller.DownloadAsync(sdk).GetAwaiter().GetResult();
            return TarballInstaller.ManifestValue(sdk);
        }

        /// <summary>The recommended (else newest) version of a package on its scoped registry. The registry must be in the
        /// manifest Unity loaded at start.</summary>
        private static string LatestVersion(SdkRequirement sdk)
        {
            var request = Client.Search(sdk.PackageName);
            DateTime deadline = DateTime.UtcNow + PackageSearchTimeout;
            while (!request.IsCompleted)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Looking up {sdk.PackageName} on {sdk.RegistryUrl} timed out.");
                Thread.Sleep(100);
            }
            if (request.Status != StatusCode.Success || request.Result == null || request.Result.Length == 0)
                throw new InvalidOperationException($"{sdk.PackageName} not found on {sdk.RegistryUrl}: {request.Error?.message}");
            PackageInfo package = request.Result[0];
            foreach (string version in new[] { package.versions.recommended, package.versions.latestCompatible, package.versions.latest, package.version })
            {
                if (!string.IsNullOrEmpty(version))
                    return version;
            }
            throw new InvalidOperationException($"{sdk.PackageName} on {sdk.RegistryUrl} lists no version.");
        }

        private static GameLibInstallReport Done(GameLibInstallReport report, GameLibInstallStatus status)
        {
            report.Status = status;
            report.Next = status switch
            {
                GameLibInstallStatus.Done => "Fill in the module settings (see Issues), then run Doctor.",
                GameLibInstallStatus.RunAgain => Application.isBatchMode
                    ? "Run Install again with the same modules: the next Unity start resolves and compiles the new packages."
                    : "Wait for Unity to resolve packages and compile, then call InstallModules again.",
                _ => "Fix the errors, then run Install again.",
            };
            return report;
        }

        private static void Finish(string command, object report, int exitCode)
        {
            string json = JsonConvert.SerializeObject(report, Formatting.Indented, new StringEnumConverter());
            string path = Argument(ReportArgument) ?? Path.Combine("Logs", $"gamelib-{command}.json");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameLib] Could not write {path}: {e.Message}");
            }
            Debug.Log(ReportMarker + json);
            if (Application.isBatchMode)
                EditorApplication.Exit(exitCode);
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }

    public enum GameLibInstallStatus
    {
        Done = 0,
        RunAgain = 1,
        Error = 2,
    }

    public sealed class GameLibInstallReport
    {
        public GameLibInstallStatus Status { get; set; }
        /// <summary>What to do next, in one sentence.</summary>
        public string Next { get; set; }
        /// <summary>The requested modules and every module they need, dependencies first.</summary>
        public List<string> Modules { get; } = new();
        public List<string> Changes { get; } = new();
        public List<string> Pending { get; } = new();
        public List<string> Errors { get; } = new();
        /// <summary><see cref="GameLibProject.Diagnose"/> after the run (only when no manifest change is pending).</summary>
        public List<GameLibCliIssue> Issues { get; } = new();
    }

    public sealed class GameLibDoctorReport
    {
        public string GameLibVersion { get; set; }
        public int ErrorCount { get; set; }
        public List<GameLibModuleState> Modules { get; } = new();
        public List<GameLibCliIssue> Issues { get; } = new();
    }

    public sealed class GameLibModuleState
    {
        public string Id { get; set; }
        public string Package { get; set; }
        public bool Installed { get; set; }
        public string Version { get; set; }
        public List<string> MissingSdks { get; } = new();
        /// <summary>Path of the module asset, null when there is none or the module has no asset.</summary>
        public string Asset { get; set; }
        public bool AssetListed { get; set; }
        /// <summary>The module asset's serialized fields, to read the current configuration.</summary>
        public JObject Settings { get; set; }
    }

    public sealed class GameLibCliIssue
    {
        public string Severity { get; set; }
        public string Message { get; set; }

        public static GameLibCliIssue From(GameLibIssue issue) => new() { Severity = issue.Severity.ToString(), Message = issue.Message };
    }
}
