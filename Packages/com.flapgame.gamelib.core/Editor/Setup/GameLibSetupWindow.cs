using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace FlapGame.GameLib.Editor
{
    /// <summary>
    /// GameLib > Setup: adds modules one at a time. Each module walks the same steps - install its package (and the
    /// GameLib packages it needs), install its third-party SDK, create its settings asset, then fix what the check reports.
    /// </summary>
    public sealed class GameLibSetupWindow : EditorWindow
    {
        private enum ModuleState
        {
            NotInstalled,
            NeedsSetup,
            Ready,
            ComingSoon,
        }

        private Dictionary<string, PackageInfo> installed = new();
        private GameLibSettings settings;
        private string selectedId;
        private Vector2 listScroll;
        private Vector2 detailScroll;
        private List<GameLibIssue> doctorIssues;
        private Request pendingRequest;
        private string pendingLabel;
        private string downloading;
        private UnityEditor.Editor moduleEditor;

        [MenuItem("GameLib/Setup", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<GameLibSetupWindow>("GameLib Setup");
            window.minSize = new Vector2(640, 420);
        }

        private void OnEnable()
        {
            Refresh();
            selectedId ??= GameLibModuleCatalog.All.First().Id;
        }

        private void OnDisable()
        {
            EditorApplication.update -= PollRequest;
            if (moduleEditor != null)
                DestroyImmediate(moduleEditor);
        }

        private void OnFocus() => Refresh();

        private void Refresh()
        {
            installed = GameLibProject.InstalledPackages();
            settings = GameLibProject.LoadSettings();
            doctorIssues = null;
        }

        private void OnGUI()
        {
            DrawToolbar();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawModuleList();
                DrawDetails();
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label($"GameLib {GameLibVersion.Current}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (pendingRequest != null)
                    GUILayout.Label(pendingLabel, EditorStyles.miniLabel);
                if (GUILayout.Button("Check project", EditorStyles.toolbarButton))
                    doctorIssues = GameLibProject.Diagnose();
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
                    Refresh();
            }
        }

        private void DrawModuleList()
        {
            using (var scroll = new EditorGUILayout.ScrollViewScope(listScroll, GUILayout.Width(230)))
            {
                listScroll = scroll.scrollPosition;
                DrawCoreRow();
                foreach (GameLibModuleInfo module in GameLibModuleCatalog.All)
                {
                    ModuleState state = StateOf(module);
                    string label = $"{Badge(state)}  {module.DisplayName}";
                    bool selected = module.Id == selectedId;
                    if (GUILayout.Toggle(selected, label, EditorStyles.toolbarButton) && !selected)
                    {
                        selectedId = module.Id;
                        GUI.FocusControl(null);
                    }
                }
            }
        }

        private void DrawCoreRow()
        {
            bool hasSettings = settings != null;
            string label = $"{(hasSettings ? "✔" : "●")}  Core";
            bool selected = selectedId == "core";
            if (GUILayout.Toggle(selected, label, EditorStyles.toolbarButton) && !selected)
                selectedId = "core";
        }

        private void DrawDetails()
        {
            using (var scroll = new EditorGUILayout.ScrollViewScope(detailScroll))
            {
                detailScroll = scroll.scrollPosition;
                if (selectedId == "core")
                    DrawCore();
                else if (GameLibModuleCatalog.Find(selectedId) is { } module)
                    DrawModule(module);

                if (doctorIssues != null)
                    DrawIssues("Project check", doctorIssues);
            }
        }

        private void DrawCore()
        {
            EditorGUILayout.LabelField("Core", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox(
                "Core starts GameLib before the first scene (no bootstrap object needed) and always provides lifecycle, consent state, " +
                "entitlements (no_ads) and analytics. Every module below plugs into it.", MessageType.None);

            Step(1, "Settings asset", settings != null, () =>
            {
                if (GUILayout.Button($"Create {GameLibProject.SettingsAssetPath}"))
                {
                    settings = GameLibProject.LoadOrCreateSettings();
                    Selection.activeObject = settings;
                }
            });

            if (settings != null)
            {
                DrawInlineEditor(settings);
            }
        }

        private void DrawModule(GameLibModuleInfo module)
        {
            EditorGUILayout.LabelField(module.DisplayName, EditorStyles.largeLabel);
            EditorGUILayout.LabelField(module.PackageName, EditorStyles.miniLabel);
            EditorGUILayout.HelpBox(module.Description, MessageType.None);

            if (!module.Available)
            {
                EditorGUILayout.HelpBox("Not released yet. It will show up here in a later GameLib version.", MessageType.Info);
                return;
            }

            IReadOnlyList<GameLibModuleInfo> chain = GameLibModuleCatalog.WithDependencies(module);
            var missing = chain.Where(m => !GameLibProject.IsInstalled(m, installed)).ToList();

            Step(1, "Package", missing.Count == 0, () =>
            {
                string names = string.Join(", ", missing.Select(m => m.PackageName));
                EditorGUILayout.LabelField($"Adds: {names}", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(pendingRequest != null))
                {
                    if (GUILayout.Button("Install"))
                        StartRequest(Client.AddAndRemove(missing.Select(m => GameLibProject.InstallIdentifier(m.PackageName)).ToArray()), $"Installing {module.DisplayName}...");
                }
            });

            if (missing.Count > 0)
                return;

            // The SDKs of the whole chain: Analytics - Firebase also needs Firebase App from the firebase module.
            List<SdkRequirement> sdks = AllSdks(module);
            bool sdksReady = sdks.All(s => installed.ContainsKey(s.PackageName));
            if (sdks.Count > 0)
            {
                Step(2, "Third-party SDK", sdksReady, () =>
                {
                    foreach (SdkRequirement sdk in sdks.Where(s => !installed.ContainsKey(s.PackageName)))
                        DrawSdkInstall(module, sdk);
                });
                if (!sdksReady)
                    return;
            }

            int assetStep = sdks.Count > 0 ? 3 : 2;
            if (module.ModuleTypeName == null)
            {
                EditorGUILayout.HelpBox("Ready. This module has no asset of its own; it is configured where its README says.", MessageType.Info);
                return;
            }

            GameLibModule asset = GameLibProject.FindModuleAsset(module, settings);
            bool listed = asset != null && settings != null && settings.Modules.Contains(asset);
            Step(assetStep, "Module asset", listed, () =>
            {
                if (GameLibProject.FindModuleType(module) == null)
                {
                    EditorGUILayout.HelpBox("Waiting for scripts to compile...", MessageType.Info);
                    return;
                }
                if (GUILayout.Button(asset == null ? "Create and add to GameLibSettings" : "Add to GameLibSettings"))
                {
                    GameLibProject.AddModuleAsset(module);
                    Refresh();
                }
            });

            if (!listed)
                return;

            DrawInlineEditor(asset);

            var issues = new List<GameLibIssue>();
            asset.Validate(issues);
            Step(assetStep + 1, "Configuration", issues.All(i => i.Severity != GameLibIssueSeverity.Error), () => { });
            if (issues.Count > 0)
                DrawIssues(null, issues);
        }

        private void DrawSdkInstall(GameLibModuleInfo module, SdkRequirement sdk)
        {
            EditorGUILayout.LabelField($"{sdk.DisplayName} ({sdk.PackageName})", EditorStyles.wordWrappedLabel);
            switch (sdk.Source)
            {
                case SdkSource.UnityRegistry:
                case SdkSource.Git:
                    using (new EditorGUI.DisabledScope(pendingRequest != null))
                    {
                        string identifier = sdk.Source == SdkSource.Git ? sdk.Location : $"{sdk.PackageName}@{sdk.Location}";
                        if (GUILayout.Button($"Install {sdk.DisplayName}"))
                            StartRequest(Client.Add(identifier), $"Installing {sdk.DisplayName}...");
                    }
                    break;
                case SdkSource.ScopedRegistry:
                    using (new EditorGUI.DisabledScope(pendingRequest != null))
                    {
                        EditorGUILayout.LabelField($"Adds the scoped registry {sdk.RegistryUrl}", EditorStyles.wordWrappedMiniLabel);
                        if (GUILayout.Button($"Install {sdk.DisplayName}"))
                        {
                            // Every registry the module's SDKs need, so dependencies between them resolve (MAX -> EDM on OpenUPM).
                            var manifest = new ManifestManager();
                            foreach (SdkRequirement other in AllSdks(module).Where(s => s.Source == SdkSource.ScopedRegistry))
                                manifest.EnsureScopedRegistry(other.RegistryName, other.RegistryUrl, other.RegistryScopes);
                            manifest.Save();
                            string identifier = string.IsNullOrEmpty(sdk.Location) ? sdk.PackageName : $"{sdk.PackageName}@{sdk.Location}";
                            StartRequest(Client.Add(identifier), $"Installing {sdk.DisplayName}...");
                        }
                    }
                    break;
                case SdkSource.Tarball:
                    using (new EditorGUI.DisabledScope(pendingRequest != null || downloading != null))
                    {
                        EditorGUILayout.LabelField($"Downloads {TarballInstaller.FileName(sdk)} into Packages/{TarballInstaller.Folder}/ - commit that folder.",
                                                   EditorStyles.wordWrappedMiniLabel);
                        if (downloading == sdk.PackageName)
                            EditorGUILayout.HelpBox($"Downloading {sdk.DisplayName}...", MessageType.Info);
                        else if (GUILayout.Button($"Install {sdk.DisplayName}"))
                            InstallTarball(module, sdk);
                    }
                    break;
                default:
                    EditorGUILayout.HelpBox($"Install {sdk.DisplayName} following the module README.", MessageType.Warning);
                    break;
            }
        }

        private async void InstallTarball(GameLibModuleInfo module, SdkRequirement sdk)
        {
            downloading = sdk.PackageName;
            try
            {
                // Every registry the module chain needs, so the tarball's dependencies resolve (Firebase -> EDM on OpenUPM).
                await TarballInstaller.InstallAsync(sdk, AllSdks(module).Where(s => s.Source == SdkSource.ScopedRegistry));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GameLib] Installing {sdk.DisplayName} from {sdk.Location} failed: {e.Message}");
            }
            finally
            {
                downloading = null;
                Refresh();
                Repaint();
            }
        }

        private static List<SdkRequirement> AllSdks(GameLibModuleInfo module) =>
            GameLibModuleCatalog.WithDependencies(module).SelectMany(m => m.Sdks).Distinct().ToList();

        private void DrawInlineEditor(Object target)
        {
            UnityEditor.Editor.CreateCachedEditor(target, null, ref moduleEditor);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.ObjectField(target, typeof(Object), false);
                moduleEditor.OnInspectorGUI();
            }
        }

        private static void Step(int number, string title, bool done, System.Action drawTodo)
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"{(done ? "✔" : "○")}  {number}. {title}", EditorStyles.boldLabel);
                if (!done)
                    drawTodo();
            }
        }

        private static void DrawIssues(string title, List<GameLibIssue> issues)
        {
            EditorGUILayout.Space(8);
            if (title != null)
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (issues.Count == 0)
                EditorGUILayout.HelpBox("No problems found.", MessageType.Info);
            foreach (GameLibIssue issue in issues)
                EditorGUILayout.HelpBox(issue.Message, issue.Severity == GameLibIssueSeverity.Error ? MessageType.Error : MessageType.Warning);
        }

        private ModuleState StateOf(GameLibModuleInfo module)
        {
            if (!module.Available)
                return ModuleState.ComingSoon;
            if (!GameLibProject.IsInstalled(module, installed))
                return ModuleState.NotInstalled;
            if (AllSdks(module).Any(s => !installed.ContainsKey(s.PackageName)))
                return ModuleState.NeedsSetup;
            if (module.ModuleTypeName == null)
                return ModuleState.Ready;

            GameLibModule asset = GameLibProject.FindModuleAsset(module, settings);
            if (asset == null || settings == null || !settings.Modules.Contains(asset))
                return ModuleState.NeedsSetup;

            var issues = new List<GameLibIssue>();
            asset.Validate(issues);
            return issues.Any(i => i.Severity == GameLibIssueSeverity.Error) ? ModuleState.NeedsSetup : ModuleState.Ready;
        }

        private static string Badge(ModuleState state) => state switch
        {
            ModuleState.Ready => "✔",
            ModuleState.NeedsSetup => "●",
            ModuleState.NotInstalled => "○",
            _ => "…",
        };

        private void StartRequest(Request request, string label)
        {
            pendingRequest = request;
            pendingLabel = label;
            EditorApplication.update -= PollRequest;
            EditorApplication.update += PollRequest;
        }

        private void PollRequest()
        {
            if (pendingRequest == null || !pendingRequest.IsCompleted)
                return;

            if (pendingRequest.Status == StatusCode.Failure)
                Debug.LogError($"[GameLib] {pendingLabel} failed: {pendingRequest.Error?.message}");

            pendingRequest = null;
            pendingLabel = null;
            EditorApplication.update -= PollRequest;
            Refresh();
            Repaint();
        }
    }
}
