using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace FlapGame.GameLib.Ads.Editor
{
    /// <summary>
    /// Inspector for <see cref="AdsModule"/>: providers (id dropdown, switch, per-platform app keys), placements with
    /// routes as a provider dropdown + per-platform unit ids, a validation pass (<see cref="AdsSettingsValidation"/>)
    /// and a scan of <c>Assets/**/*.cs</c> for <c>Show*Async("literal")</c> ids that no placement defines.
    /// Pre-0.6 routes (enum provider + one unit id) are migrated once when the asset is inspected.
    /// </summary>
    [CustomEditor(typeof(AdsModule))]
    public sealed class AdsModuleEditor : UnityEditor.Editor
    {
        private static readonly Regex ShowCallPattern = new(
            @"\bShow(?:Rewarded|Interstitial|AppOpen|Banner)?Async\(\s*""([^""]+)""",
            RegexOptions.Compiled);

        private GUIStyle sectionHeaderStyle;
        private GUIStyle SectionHeaderStyle => sectionHeaderStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };

        private Dictionary<string, List<string>> literalIdsInCode; // id -> "file:line" list

        private void OnEnable()
        {
            var settings = target as AdsModule;
            if (settings != null && AdsSettingsValidation.Migrate(settings))
            {
                EditorUtility.SetDirty(settings);
                Debug.Log($"[Ads] Migrated pre-0.6 routes/providers of '{settings.name}' to provider ids and per-platform unit ids.");
            }
            literalIdsInCode = ScanAssetsForLiteralIds();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var settings = (AdsModule)target;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Installed providers", SectionHeaderStyle);
                var parts = new List<string>();
                foreach (string id in AdsSettingsValidation.KnownProviderIds())
                    if (id != AdProviderIds.None)
                        parts.Add($"{id}: {(AdsSettingsValidation.IsProviderInstalled(id) ? "installed" : "not installed")}");
                EditorGUILayout.LabelField(string.Join("    ", parts), EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("Routes for a provider that is not installed are skipped at runtime; the Null adapter mocks everything when no SDK is present (Editor / development builds only - a release build without an SDK shows no ads).", EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("required"));
            EditorGUILayout.Space(6);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Providers"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("LoadStrategy"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Policy"), true);
            EditorGUILayout.Space(6);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AppOpenPlacementId"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AppOpenColdStartWaitSeconds"));
            EditorGUILayout.Space(6);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("InitializeInBackground"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ConsentTimeoutSeconds"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("PlanRemoteConfigKey"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("NullAdapterFakeDelaySeconds"));
            EditorGUILayout.Space(6);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ConsentDebug"), true);

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Test Mode", SectionHeaderStyle);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("TestMode"), new GUIContent("Enable Test Mode"));
                if (serializedObject.FindProperty("TestMode").boolValue)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("AdMobTestDeviceIds"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("MaxTestDeviceIds"), true);
                    EditorGUILayout.HelpBox(
                        "AdMob test ad unit IDs:\n" +
                        "Android Banner:       ca-app-pub-3940256099942544/6300978111\n" +
                        "Android Interstitial: ca-app-pub-3940256099942544/1033173712\n" +
                        "Android Rewarded:     ca-app-pub-3940256099942544/5224354917\n" +
                        "Android App Open:     ca-app-pub-3940256099942544/9257395921\n" +
                        "iOS Banner:           ca-app-pub-3940256099942544/2934735716\n" +
                        "iOS Interstitial:     ca-app-pub-3940256099942544/4411468910\n" +
                        "iOS Rewarded:         ca-app-pub-3940256099942544/1712485313\n" +
                        "iOS App Open:         ca-app-pub-3940256099942544/5662855259",
                        MessageType.Info);
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Placements"), true);

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(8);
            DrawValidation(settings);
        }

        private void DrawValidation(AdsModule settings)
        {
            List<string> problems = AdsSettingsValidation.Validate(settings, EditorUserBuildSettings.activeBuildTarget);
            var ids = new HashSet<string>();
            foreach (AdPlacement p in settings.Placements)
                if (p != null && !string.IsNullOrEmpty(p.Id)) ids.Add(p.Id);

            if (problems.Count > 0)
                EditorGUILayout.HelpBox("Placement problems:\n- " + string.Join("\n- ", problems), MessageType.Warning);
            else
                EditorGUILayout.HelpBox("Placements look consistent.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Placement ids used in Assets/**/*.cs", SectionHeaderStyle);
                if (GUILayout.Button("Rescan", GUILayout.Width(70)))
                    literalIdsInCode = ScanAssetsForLiteralIds();
            }

            if (literalIdsInCode == null || literalIdsInCode.Count == 0)
            {
                EditorGUILayout.HelpBox("No Show*Async(\"literal\") calls found under Assets/.", MessageType.None);
                return;
            }

            var sb = new StringBuilder();
            foreach (KeyValuePair<string, List<string>> kv in literalIdsInCode)
            {
                if (ids.Contains(kv.Key)) continue;
                sb.Append('\'').Append(kv.Key).Append("' is not in Placements - called from ").Append(string.Join(", ", kv.Value)).Append('\n');
            }
            if (sb.Length > 0)
                EditorGUILayout.HelpBox(sb.ToString().TrimEnd(), MessageType.Error);
            else
                EditorGUILayout.HelpBox($"All {literalIdsInCode.Count} literal id(s) used in code exist in Placements.", MessageType.Info);
        }

        private static Dictionary<string, List<string>> ScanAssetsForLiteralIds()
        {
            var result = new Dictionary<string, List<string>>();
            string root = Application.dataPath;
            if (!Directory.Exists(root)) return result;
            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); }
                catch (Exception) { continue; }
                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match m in ShowCallPattern.Matches(lines[i]))
                    {
                        string id = m.Groups[1].Value;
                        if (!result.TryGetValue(id, out List<string> where)) result[id] = where = new List<string>();
                        where.Add($"{Path.GetFileName(file)}:{i + 1}");
                    }
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Checks and one-shot migration of an <see cref="AdsModule"/> asset. Used by the inspector and by
    /// tests; pure (no GUI) so tests can call it.
    /// </summary>
    public static class AdsSettingsValidation
    {
        private static Dictionary<string, string> factoryIds; // id -> display name

        /// <summary>Provider ids offered in dropdowns: the <see cref="AdProviderIds"/> constants plus every factory compiled in.</summary>
        public static List<string> KnownProviderIds()
        {
            var ids = new List<string> { AdProviderIds.None, AdProviderIds.AdMob, AdProviderIds.Max, AdProviderIds.LevelPlay };
            foreach (string id in FactoryIds().Keys)
                if (!ids.Contains(id)) ids.Add(id);
            foreach (IAdsProviderFactory f in AdsProviderCatalog.All)
            {
                string id = AdProviderIds.Normalize(f.ProviderId);
                if (!ids.Contains(id)) ids.Add(id);
            }
            return ids;
        }

        /// <summary>True when an <see cref="IAdsProviderFactory"/> with this id is compiled in (or registered).</summary>
        public static bool IsProviderInstalled(string providerId)
        {
            string id = AdProviderIds.Normalize(providerId);
            return id == AdProviderIds.None || FactoryIds().ContainsKey(id) || AdsProviderCatalog.Contains(id);
        }

        // Provider factories found by type (SubsystemRegistration does not run in edit mode, so the catalog may be empty).
        private static Dictionary<string, string> FactoryIds()
        {
            if (factoryIds != null) return factoryIds;
            factoryIds = new Dictionary<string, string>();
            foreach (Type t in TypeCache.GetTypesDerivedFrom<IAdsProviderFactory>())
            {
                if (t.IsAbstract || t.IsInterface || t.GetConstructor(Type.EmptyTypes) == null) continue;
                try
                {
                    var f = (IAdsProviderFactory)Activator.CreateInstance(t);
                    factoryIds[AdProviderIds.Normalize(f.ProviderId)] = f.DisplayName;
                }
                catch (Exception) { /* a factory that cannot be built in the Editor is simply not listed */ }
            }
            return factoryIds;
        }

        /// <summary>The unit id a route uses on <paramref name="target"/> (platform field, else the shared fallback).</summary>
        public static string UnitIdFor(AdRoute route, BuildTarget target)
        {
            if (route == null) return null;
            string platform = target == BuildTarget.iOS ? route.IosAdUnitId : route.AndroidAdUnitId;
            return !string.IsNullOrEmpty(platform) ? platform : route.AdUnitId;
        }

        /// <summary>Problems of <paramref name="settings"/> for <paramref name="target"/>. Empty = consistent.</summary>
        public static List<string> Validate(AdsModule settings, BuildTarget target, Func<string, bool> isInstalled = null)
        {
            isInstalled ??= IsProviderInstalled;
            var problems = new List<string>();
            if (settings == null) return problems;
            string platform = target == BuildTarget.iOS ? "iOS" : "Android";

            var configured = new HashSet<string>();
            if (settings.Providers != null)
            {
                for (int i = 0; i < settings.Providers.Count; i++)
                {
                    AdsProviderConfig c = settings.Providers[i];
                    if (c == null) { problems.Add($"Providers #{i}: null entry."); continue; }
                    string id = AdProviderIds.Normalize(c.ProviderId);
                    if (!configured.Add(id)) problems.Add($"Providers: '{id}' is listed twice.");
                    if (id == AdProviderIds.LevelPlay && c.Enabled)
                    {
                        string key = target == BuildTarget.iOS ? c.IosAppKey : c.AndroidAppKey;
                        if (string.IsNullOrEmpty(key)) problems.Add($"Providers: LevelPlay has no {platform} app key - it cannot initialize.");
                    }
                    if (c.Enabled && id != AdProviderIds.None && !isInstalled(id))
                        problems.Add($"Providers: '{id}' is enabled but its SDK / provider assembly is not installed (GameLib > Setup).");
                }
            }

            var seen = new HashSet<string>();
            if (settings.Placements == null) return problems;
            for (int i = 0; i < settings.Placements.Count; i++)
            {
                AdPlacement p = settings.Placements[i];
                string label = p == null || string.IsNullOrEmpty(p.Id) ? $"#{i}" : $"'{p.Id}'";
                if (p == null) { problems.Add($"{label}: null entry."); continue; }
                if (string.IsNullOrEmpty(p.Id)) problems.Add($"{label}: empty Id.");
                else if (!seen.Add(p.Id)) problems.Add($"{label}: duplicate placement id - merge into one placement with several Routes.");

                if (p.Routes == null || p.Routes.Count == 0) { problems.Add($"{label}: no Routes - it can never show."); continue; }

                var providersInRoutes = new HashSet<string>();
                foreach (AdRoute r in p.Routes)
                {
                    if (r == null) { problems.Add($"{label}: null route."); continue; }
                    string id = r.EffectiveProviderId;
                    if (!providersInRoutes.Add(id)) problems.Add($"{label}: '{id}' appears twice - only the first route per provider is used.");
                    if (id == AdProviderIds.None) continue;
                    if (!configured.Contains(id)) problems.Add($"{label}: route provider '{id}' is not in Providers - add it there (switch + app keys).");
                    if (string.IsNullOrEmpty(UnitIdFor(r, target))) problems.Add($"{label}: '{id}' route has no {platform} ad unit id.");
                    else if (!isInstalled(id)) problems.Add($"{label}: '{id}' unit id set but that provider is not installed (GameLib > Setup).");
                }
                if (p.Format == AdFormat.Rewarded && p.SuppressedByNoAds)
                    problems.Add($"{label}: rewarded placement is suppressed by no-ads - players who bought no-ads lose the reward path (usually unintended).");
            }
            return problems;
        }

        /// <summary>
        /// One-shot migration of pre-0.6 data: a route with an empty <see cref="AdRoute.ProviderId"/> gets
        /// <see cref="AdProviderIds.FromLegacy"/> of its enum; a single <see cref="AdRoute.AdUnitId"/> fills both
        /// platform ids; providers named by routes get an enabled row; the obsolete app open field moves into Policy.
        /// Returns true when something changed (the caller marks the asset dirty).
        /// </summary>
        public static bool Migrate(AdsModule settings)
        {
            if (settings == null) return false;
            bool changed = false;
            settings.Providers ??= new List<AdsProviderConfig>();
            if (settings.Placements != null)
            {
                foreach (AdPlacement p in settings.Placements)
                {
                    if (p?.Routes == null) continue;
                    foreach (AdRoute r in p.Routes)
                    {
                        if (r == null) continue;
                        if (string.IsNullOrWhiteSpace(r.ProviderId))
                        {
                            r.ProviderId = AdProviderIds.FromLegacy(r.Provider);
                            changed = true;
                        }
                        if (!string.IsNullOrEmpty(r.AdUnitId) && string.IsNullOrEmpty(r.AndroidAdUnitId) && string.IsNullOrEmpty(r.IosAdUnitId))
                        {
                            r.AndroidAdUnitId = r.AdUnitId;
                            r.IosAdUnitId     = r.AdUnitId;
                            r.AdUnitId        = null;
                            changed = true;
                        }
                        string id = r.EffectiveProviderId;
                        if (id != AdProviderIds.None && settings.FindProvider(id) == null)
                        {
                            settings.Providers.Add(new AdsProviderConfig { ProviderId = id, Enabled = true });
                            changed = true;
                        }
                    }
                }
            }
            settings.Policy ??= new AdsPolicy();
            return changed;
        }
    }

    /// <summary>Provider id as a dropdown (known ids + "Other..." free text).</summary>
    internal static class ProviderIdField
    {
        public static void Draw(Rect rect, SerializedProperty prop, GUIContent label)
        {
            List<string> ids = AdsSettingsValidation.KnownProviderIds();
            string current = string.IsNullOrWhiteSpace(prop.stringValue) ? string.Empty : AdProviderIds.Normalize(prop.stringValue);
            int index = ids.IndexOf(current);
            var options = new List<GUIContent>();
            foreach (string id in ids)
                options.Add(new GUIContent(AdsSettingsValidation.IsProviderInstalled(id) ? id : id + " (not installed)"));
            if (index < 0)
            {
                options.Add(new GUIContent(string.IsNullOrEmpty(current) ? "(empty)" : current + " (custom)"));
                index = options.Count - 1;
            }
            int picked = EditorGUI.Popup(rect, label, index, options.ToArray());
            if (picked != index && picked < ids.Count) prop.stringValue = ids[picked];
        }
    }

    /// <summary>Route row: provider dropdown + Android / iOS unit id (+ the shared fallback when it is set).</summary>
    [CustomPropertyDrawer(typeof(AdRoute))]
    public sealed class AdRouteDrawer : PropertyDrawer
    {
        private static float Line => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            bool fallback = !string.IsNullOrEmpty(property.FindPropertyRelative(nameof(AdRoute.AdUnitId)).stringValue);
            return Line * (fallback ? 4 : 3);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            ProviderIdField.Draw(row, property.FindPropertyRelative(nameof(AdRoute.ProviderId)), label);
            using (new EditorGUI.IndentLevelScope())
            {
                row.y += Line;
                EditorGUI.PropertyField(row, property.FindPropertyRelative(nameof(AdRoute.AndroidAdUnitId)), new GUIContent("Android Unit Id"));
                row.y += Line;
                EditorGUI.PropertyField(row, property.FindPropertyRelative(nameof(AdRoute.IosAdUnitId)), new GUIContent("iOS Unit Id"));
                SerializedProperty shared = property.FindPropertyRelative(nameof(AdRoute.AdUnitId));
                if (!string.IsNullOrEmpty(shared.stringValue))
                {
                    row.y += Line;
                    EditorGUI.PropertyField(row, shared, new GUIContent("Unit Id (both platforms)"));
                }
            }
            EditorGUI.EndProperty();
        }
    }

    /// <summary>Provider row: id dropdown, Enabled, Android / iOS app key.</summary>
    [CustomPropertyDrawer(typeof(AdsProviderConfig))]
    public sealed class AdsProviderConfigDrawer : PropertyDrawer
    {
        private static float Line => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => Line * 4;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            ProviderIdField.Draw(row, property.FindPropertyRelative(nameof(AdsProviderConfig.ProviderId)), label);
            using (new EditorGUI.IndentLevelScope())
            {
                row.y += Line;
                EditorGUI.PropertyField(row, property.FindPropertyRelative(nameof(AdsProviderConfig.Enabled)));
                row.y += Line;
                EditorGUI.PropertyField(row, property.FindPropertyRelative(nameof(AdsProviderConfig.AndroidAppKey)), new GUIContent("Android App Key"));
                row.y += Line;
                EditorGUI.PropertyField(row, property.FindPropertyRelative(nameof(AdsProviderConfig.IosAppKey)), new GUIContent("iOS App Key"));
            }
            EditorGUI.EndProperty();
        }
    }
}
