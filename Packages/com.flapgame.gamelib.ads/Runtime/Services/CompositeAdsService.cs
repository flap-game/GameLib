using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// The one <see cref="IAdsService"/> gameplay talks to. Owns every rule - per-placement routes, load strategy,
    /// cooldown, session cap, no-ads, the global <see cref="AdsPolicy"/>, the remote <see cref="AdsPlan"/>, one
    /// fullscreen ad at a time - and drives the <see cref="IAdsProviderAdapter"/>s: which placements each one keeps
    /// loaded (<see cref="IAdsProviderAdapter.SetPlacementActive"/>) and which one shows. Adapters know nothing about rules.
    /// <para>
    /// Effective values of a placement = the <see cref="AdsModule"/> row, then the plan's placement rule, then the
    /// legacy <c>ads_placements</c> overrides (<see cref="ApplyOverrides(IEnumerable{AdPlacementOverride})"/>).
    /// </para>
    /// </summary>
    public sealed class CompositeAdsService : IAdsService, IBannerMetrics, IAdClickSource, IDisposable
    {
        private const string LogPrefix = "[Ads]";

        /// <summary>
        /// How long one adapter may take to initialize before its routes are set aside. An adapter that finishes later
        /// (e.g. a consent flow inside its SDK init that the user is still reading) joins the moment it is done.
        /// </summary>
        public static readonly TimeSpan DefaultInitializeTimeout = TimeSpan.FromSeconds(15);

        private readonly List<IAdsProviderAdapter> adapters = new();
        private readonly Dictionary<string, IAdsProviderAdapter> byId = new();
        private readonly List<Action> unsubscribe = new();
        private readonly AdsModule settings;
        private readonly Dictionary<string, AdPlacement> basePlacements = new();
        private readonly Dictionary<string, AdPlacement> placements = new();
        private readonly Dictionary<string, AdPlacementOverride> legacyOverrides = new();
        private readonly HashSet<string> disabled = new();
        private readonly HashSet<string> initializedProviders = new();
        private readonly HashSet<string> droppedProviders = new();
        // Placements each provider received a unit for at init (a route with no unit id for this platform never loads).
        private readonly Dictionary<string, HashSet<string>> assignedPlacements = new();
        private readonly HashSet<(string Placement, string Provider)> activeRoutes = new();
        private readonly Dictionary<string, int> primaryDepth = new();
        private readonly Dictionary<string, string> showingProvider = new();
        private readonly Dictionary<string, int> showsThisSession = new();
        private readonly Dictionary<string, float> lastShownAt = new();
        private readonly Func<bool> noAdsOwned;
        private readonly Func<float> now;
        private readonly Func<IAdsProviderAdapter, AdsProviderContext> contextFactory;
        private readonly float startedAt;
        private readonly AdsPolicy basePolicy;
        private readonly AdLoadStrategy baseLoadStrategy;

        private AdsPlan plan;
        private HashSet<string> planProviders;
        private AdsPolicy policy;
        private AdLoadStrategy loadStrategy;
        private ConsentState consent = new();
        private Task initializationTask;
        private string showingFullscreen;
        private float lastFullscreenClosedAt = float.NegativeInfinity;
        private float lastInterstitialAt = float.NegativeInfinity;
        private float lastAppOpenAt = float.NegativeInfinity;
        private bool noAdsApplied;
        private bool refreshingNoAds;
        private bool disposed;

        /// <summary>Raised after every adapter show: placement id, provider id, result.</summary>
        public event Action<string, string, AdShowResult> ShowResult;

        /// <inheritdoc />
        public event Action<string> PlacementReadyChanged;

        /// <summary>Raised when a fullscreen ad starts (true) or ends (false). Pause audio / gameplay on it.</summary>
        public event Action<bool> FullscreenShowingChanged;

        /// <summary>Every paid impression of every adapter (impression-level revenue).</summary>
        public event Action<AdImpression> ImpressionPaid;

        /// <summary>Every ad click of every adapter that reports clicks (<see cref="IAdClickSource"/>): placement id.</summary>
        public event Action<string> AdClicked;

        /// <param name="adapters">One adapter per provider id. A second adapter with the same id is ignored with a warning.</param>
        /// <param name="settings">Placement table, providers and policy. Null = no placements (every show fails with UnknownPlacement).</param>
        /// <param name="plan">Remote plan read before SDK init (route order, rules, policy). Null = settings only.</param>
        /// <param name="noAdsOwned">Returns true while the player owns no-ads (<c>Entitlements.NoAds</c>). Null = never owned. Evaluated lazily.</param>
        /// <param name="clock">Seconds source for cooldowns and policy. Null = <c>Time.realtimeSinceStartup</c>. Tests inject a fake.</param>
        /// <param name="contextFactory">Builds each adapter's init context. Null = <see cref="BuildProviderContext"/> from <paramref name="settings"/>.</param>
        public CompositeAdsService(IReadOnlyList<IAdsProviderAdapter> adapters, AdsModule settings, AdsPlan plan = null,
                                   Func<bool> noAdsOwned = null, Func<float> clock = null,
                                   Func<IAdsProviderAdapter, AdsProviderContext> contextFactory = null)
        {
            if (adapters == null) throw new ArgumentNullException(nameof(adapters));
            this.settings   = settings;
            this.noAdsOwned = noAdsOwned;
            now             = clock ?? (() => Time.realtimeSinceStartup);
            startedAt       = now();
            this.contextFactory = contextFactory ?? (a => BuildProviderContext(this.settings, a.ProviderId, consent,
                                                                             a is NullAdsAdapter { ServesAllPlacements: true }));

            foreach (IAdsProviderAdapter a in adapters)
            {
                if (a == null) continue;
                string id = AdProviderIds.Normalize(a.ProviderId);
                if (byId.ContainsKey(id))
                {
                    Debug.LogWarning($"{LogPrefix} Two adapters registered for '{id}'; keeping the first.");
                    continue;
                }
                byId[id] = a;
                this.adapters.Add(a);
                IAdsProviderAdapter adapter = a;
                Action<string> ready = placementId => OnAdapterReadyChanged(adapter, placementId);
                Action<string, string> failed = (placementId, error) => OnAdapterLoadFailed(adapter, placementId, error);
                Action<AdImpression> paid = OnImpressionPaid;
                a.PlacementReadyChanged += ready;
                a.PlacementLoadFailed   += failed;
                a.ImpressionPaid        += paid;
                unsubscribe.Add(() =>
                {
                    adapter.PlacementReadyChanged -= ready;
                    adapter.PlacementLoadFailed   -= failed;
                    adapter.ImpressionPaid        -= paid;
                });
                if (a is IAdClickSource clicks)
                {
                    Action<string> clicked = OnAdapterClicked;
                    clicks.AdClicked += clicked;
                    unsubscribe.Add(() => clicks.AdClicked -= clicked);
                }
            }

            basePolicy = settings?.Policy?.Clone() ?? new AdsPolicy();
            baseLoadStrategy = settings != null ? settings.LoadStrategy : AdLoadStrategy.Primary;

            if (settings != null && settings.Placements != null)
                foreach (AdPlacement p in settings.Placements)
                    if (p != null && !string.IsNullOrEmpty(p.Id)) basePlacements[p.Id] = p.Clone();

            SetPlan(plan, log: plan != null);
        }

        /// <summary>Always <see cref="AdProviderType.None"/>: the composite is not one SDK. Kept for pre-0.6 callers.</summary>
        public AdProviderType ProviderType => AdProviderType.None;

        /// <summary>Installed adapters, in constructor order (duplicates removed).</summary>
        public IReadOnlyList<IAdsProviderAdapter> Adapters => adapters;

        /// <summary>Effective placements (settings, then plan rules, then legacy overrides).</summary>
        public IReadOnlyDictionary<string, AdPlacement> Placements => placements;

        /// <summary>The plan in force, or null.</summary>
        public AdsPlan Plan => plan;

        /// <summary>Effective policy (settings policy with the plan's fields on top). Treat as read-only.</summary>
        public AdsPolicy Policy => policy;

        /// <summary>Effective load strategy (settings, or the plan's when it names a valid one).</summary>
        public AdLoadStrategy LoadStrategy => loadStrategy;

        /// <summary>True while a fullscreen ad is on screen.</summary>
        public bool IsShowingFullscreen => showingFullscreen != null;

        /// <summary>Seconds app open placements stay blocked after a fullscreen ad closed. Shortcut to the policy field.</summary>
        public float AppOpenSuppressSecondsAfterFullscreen
        {
            get => policy.AppOpenSuppressSecondsAfterFullscreen;
            set
            {
                basePolicy.AppOpenSuppressSecondsAfterFullscreen = value;
                policy.AppOpenSuppressSecondsAfterFullscreen     = value;
            }
        }

        /// <summary>How long one adapter may take to initialize. Default <see cref="DefaultInitializeTimeout"/>.</summary>
        public TimeSpan InitializeTimeout { get; set; } = DefaultInitializeTimeout;

        /// <summary>True once <see cref="InitializeAsync"/> finished (adapters that failed or timed out are dropped, not awaited).</summary>
        public bool IsInitialized => initializationTask != null && initializationTask.Status == TaskStatus.RanToCompletion;

        /// <summary>True when the adapter with this id finished initializing and was not dropped.</summary>
        public bool IsProviderInitialized(string providerId) =>
            initializedProviders.Contains(AdProviderIds.Normalize(providerId));

        /// <summary>The consent state last applied (a copy).</summary>
        public ConsentState Consent => consent.Clone();

        /// <inheritdoc />
        public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            // A cancelled run (bootstrap torn down, consent abandoned) must not poison later attempts.
            Task task = initializationTask;
            if (task == null || task.IsCanceled || task.IsFaulted)
                initializationTask = task = InitializeInternalAsync(cancellationToken);
            return new ValueTask(task);
        }

        private async Task InitializeInternalAsync(CancellationToken cancellationToken)
        {
            // Tell adapters about an already-owned no-ads product BEFORE anything loads.
            RefreshNoAds();
            var tasks = new List<Task>(adapters.Count);
            foreach (IAdsProviderAdapter adapter in adapters)
                tasks.Add(InitializeAdapterAsync(adapter, cancellationToken));
            // In parallel: one slow or failing SDK never delays the others.
            await Task.WhenAll(tasks);
        }

        private async Task InitializeAdapterAsync(IAdsProviderAdapter adapter, CancellationToken cancellationToken)
        {
            string id = AdProviderIds.Normalize(adapter.ProviderId);
            if (initializedProviders.Contains(id) || droppedProviders.Contains(id)) return;
            Task init;
            try
            {
                SafeApplyConsent(adapter);
                AdsProviderContext context = contextFactory(adapter);
                var assigned = new HashSet<string>();
                if (context?.Units != null)
                    foreach (AdUnitAssignment u in context.Units)
                        if (!string.IsNullOrEmpty(u.PlacementId)) assigned.Add(u.PlacementId);
                assignedPlacements[id] = assigned;
                init = adapter.InitializeAsync(context, cancellationToken).AsTask();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                DropProvider(id, e.Message);
                return;
            }

            if (!init.IsCompleted && InitializeTimeout > TimeSpan.Zero)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task finished = await Task.WhenAny(init, Task.Delay(InitializeTimeout, timeoutCts.Token));
                timeoutCts.Cancel();
                if (finished != init)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DropProvider(id, $"timed out after {InitializeTimeout.TotalSeconds:0}s (it joins if it finishes later)");
                    _ = AdmitLateAsync(id, init);
                    return;
                }
            }

            try
            {
                await init;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                DropProvider(id, e.Message);
                return;
            }

            if (disposed) return;
            initializedProviders.Add(id);
            ReconcileAll();
        }

        private void DropProvider(string id, string reason)
        {
            // One SDK failing to initialize must not take the other providers down: its routes simply disappear.
            Debug.LogError($"{LogPrefix} {id} failed to initialize: {reason} - its routes are dropped.");
            droppedProviders.Add(id);
            ReconcileAll();
        }

        // An adapter that timed out keeps initializing; when it succeeds its routes come back for this session.
        private async Task AdmitLateAsync(string id, Task init)
        {
            try
            {
                await init;
            }
            catch (Exception e)
            {
                if (!(e is OperationCanceledException)) Debug.LogWarning($"{LogPrefix} {id} finished initializing with an error: {e.Message}");
                return;
            }
            if (disposed || !droppedProviders.Remove(id)) return;
            initializedProviders.Add(id);
            Debug.Log($"{LogPrefix} {id} finished initializing late - its routes are back.");
            ReconcileAll();
            foreach (string placementId in placements.Keys) RaiseReadyChanged(placementId);
        }

        /// <summary>
        /// Applies a remote plan live: placement rules, route order, load strategy and policy. Providers that were not
        /// initialized at startup are ignored until the next launch. Null = back to the settings values.
        /// </summary>
        public void ApplyPlan(AdsPlan plan)
        {
            SetPlan(plan, log: true);
            noAdsApplied = false; // SuppressedByNoAds may have changed: re-apply suppression.
            RefreshNoAds();
            ReconcileAll();
            foreach (string id in placements.Keys) RaiseReadyChanged(id);
        }

        private void SetPlan(AdsPlan newPlan, bool log)
        {
            plan = newPlan;
            planProviders = null;
            if (plan?.Providers != null && plan.Providers.Count > 0)
            {
                planProviders = new HashSet<string>();
                foreach (string p in plan.Providers)
                    if (!string.IsNullOrWhiteSpace(p)) planProviders.Add(AdProviderIds.Normalize(p));
            }

            loadStrategy = baseLoadStrategy;
            if (!string.IsNullOrWhiteSpace(plan?.LoadStrategy))
            {
                if (Enum.TryParse(plan.LoadStrategy.Trim(), true, out AdLoadStrategy parsed) && Enum.IsDefined(typeof(AdLoadStrategy), parsed))
                    loadStrategy = parsed;
                else if (log)
                    Debug.LogWarning($"{LogPrefix} Plan '{plan.Name}': unknown load strategy '{plan.LoadStrategy}' - keeping {baseLoadStrategy}.");
            }

            policy = basePolicy.Clone();
            AdsPlanPolicy pp = plan?.Policy;
            if (pp != null)
            {
                if (pp.InterstitialMinIntervalSeconds.HasValue)        policy.InterstitialMinIntervalSeconds        = Mathf.Max(0f, pp.InterstitialMinIntervalSeconds.Value);
                if (pp.FirstInterstitialDelaySeconds.HasValue)         policy.FirstInterstitialDelaySeconds         = Mathf.Max(0f, pp.FirstInterstitialDelaySeconds.Value);
                if (pp.RewardedResetsInterstitialTimer.HasValue)       policy.RewardedResetsInterstitialTimer       = pp.RewardedResetsInterstitialTimer.Value;
                if (pp.AppOpenSuppressSecondsAfterFullscreen.HasValue) policy.AppOpenSuppressSecondsAfterFullscreen = Mathf.Max(0f, pp.AppOpenSuppressSecondsAfterFullscreen.Value);
                if (pp.AppOpenMinIntervalSeconds.HasValue)             policy.AppOpenMinIntervalSeconds             = Mathf.Max(0f, pp.AppOpenMinIntervalSeconds.Value);
                if (pp.AppOpenFollowsInterstitialRules.HasValue)       policy.AppOpenFollowsInterstitialRules       = pp.AppOpenFollowsInterstitialRules.Value;
            }

            Rebuild(logPlan: log, logLegacy: false);
            if (log && plan != null) Debug.Log($"{LogPrefix} Plan '{plan.Name}' applied ({loadStrategy}).");
        }

        /// <summary>
        /// Legacy <c>ads_placements</c> rows: merges into placements whose Id matches. Only the fields present in a row
        /// change (see <see cref="AdPlacementOverride"/>); routes may reorder/drop the locally routed providers but never
        /// add a provider or change an ad unit id. Ids not in the local table are ignored. Applied after the plan.
        /// </summary>
        public void ApplyOverrides(IEnumerable<AdPlacementOverride> overrides)
        {
            if (overrides == null) return;
            var applied = new List<string>();
            foreach (AdPlacementOverride o in overrides)
            {
                if (o == null || string.IsNullOrEmpty(o.Id)) continue;
                if (!basePlacements.ContainsKey(o.Id))
                {
                    Debug.LogWarning($"{LogPrefix} Override for unknown placement '{o.Id}' ignored.");
                    continue;
                }
                if (!legacyOverrides.TryGetValue(o.Id, out AdPlacementOverride merged))
                    legacyOverrides[o.Id] = merged = new AdPlacementOverride { Id = o.Id };
                if (o.CooldownSeconds.HasValue)    merged.CooldownSeconds    = o.CooldownSeconds;
                if (o.MaxShowsPerSession.HasValue) merged.MaxShowsPerSession = o.MaxShowsPerSession;
                if (o.SuppressedByNoAds.HasValue)  merged.SuppressedByNoAds  = o.SuppressedByNoAds;
                if (o.Routes != null && o.Routes.Count > 0) merged.Routes = new List<AdRoute>(o.Routes);
                applied.Add(o.Id);
            }
            Rebuild(logPlan: false, logLegacy: true);
            if (applied.Count > 0) Debug.Log($"{LogPrefix} Applied {applied.Count} placement override(s).");
            foreach (string id in applied) RaiseReadyChanged(id);
            noAdsApplied = false; // SuppressedByNoAds may have changed: re-apply suppression on the next refresh.
            RefreshNoAds();
            ReconcileAll();
        }

        /// <summary>Pre-0.5 overload: whole placements as rows.</summary>
        [Obsolete("Use ApplyOverrides(IEnumerable<AdPlacementOverride>) - it merges only the fields present in each row.")]
        public void ApplyOverrides(IEnumerable<AdPlacement> overrides)
        {
            if (overrides == null) return;
            var rows = new List<AdPlacementOverride>();
            foreach (AdPlacement p in overrides) rows.Add(AdPlacementOverride.FromPlacement(p));
            ApplyOverrides(rows);
        }

        /// <summary>Forwards a consent change to every adapter (and to adapters initialized later).</summary>
        public void ApplyConsent(ConsentState state)
        {
            consent = state?.Clone() ?? new ConsentState();
            foreach (IAdsProviderAdapter adapter in adapters) SafeApplyConsent(adapter);
        }

        private void SafeApplyConsent(IAdsProviderAdapter adapter)
        {
            try { adapter.ApplyConsent(consent.Clone()); }
            catch (Exception e) { Debug.LogWarning($"{LogPrefix} {adapter.ProviderId} ApplyConsent threw: {e.Message}"); }
        }

        // settings -> plan rules -> legacy rows.
        private void Rebuild(bool logPlan, bool logLegacy)
        {
            placements.Clear();
            disabled.Clear();
            foreach (KeyValuePair<string, AdPlacement> kv in basePlacements) placements[kv.Key] = kv.Value.Clone();

            if (plan?.Placements != null)
            {
                foreach (AdsPlanPlacement rule in plan.Placements)
                {
                    if (rule == null || string.IsNullOrEmpty(rule.Id)) continue;
                    if (!placements.TryGetValue(rule.Id, out AdPlacement p))
                    {
                        if (logPlan) Debug.LogWarning($"{LogPrefix} Plan rule for unknown placement '{rule.Id}' ignored.");
                        continue;
                    }
                    if (rule.Enabled == false)              disabled.Add(rule.Id);
                    if (rule.CooldownSeconds.HasValue)      p.CooldownSeconds    = Mathf.Max(0f, rule.CooldownSeconds.Value);
                    if (rule.MaxShowsPerSession.HasValue)   p.MaxShowsPerSession = Math.Max(0, rule.MaxShowsPerSession.Value);
                    if (rule.SuppressedByNoAds.HasValue)    p.SuppressedByNoAds  = rule.SuppressedByNoAds.Value;
                    if (rule.Routes != null && rule.Routes.Count > 0) p.Routes = OrderRoutes(p, rule.Routes, logPlan);
                }
            }

            foreach (AdPlacementOverride o in legacyOverrides.Values)
            {
                if (!placements.TryGetValue(o.Id, out AdPlacement local)) continue;
                if (o.CooldownSeconds.HasValue)    local.CooldownSeconds    = Mathf.Max(0f, o.CooldownSeconds.Value);
                if (o.MaxShowsPerSession.HasValue) local.MaxShowsPerSession = Math.Max(0, o.MaxShowsPerSession.Value);
                if (o.SuppressedByNoAds.HasValue)  local.SuppressedByNoAds  = o.SuppressedByNoAds.Value;
                if (o.Routes != null && o.Routes.Count > 0)
                {
                    List<AdRoute> routes = MergeRoutes(local, o.Routes, logLegacy);
                    if (routes.Count > 0) local.Routes = routes;
                    else if (logLegacy) Debug.LogWarning($"{LogPrefix} Override for '{o.Id}' names no provider the placement is routed to locally - routes kept.");
                }
            }
        }

        // Plan route order: provider ids, matched against the routes defined in settings (unit ids stay local).
        private static List<AdRoute> OrderRoutes(AdPlacement local, List<string> order, bool log)
        {
            var result = new List<AdRoute>(order.Count);
            foreach (string raw in order)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string id = AdProviderIds.Normalize(raw);
                AdRoute localRoute = FindRoute(local, id);
                if (localRoute == null)
                {
                    if (log) Debug.LogWarning($"{LogPrefix} Plan for '{local.Id}': provider '{id}' has no route in AdsModule - ignored.");
                    continue;
                }
                if (result.Exists(x => x.EffectiveProviderId == id)) continue;
                result.Add(localRoute.Clone());
            }
            return result;
        }

        // Remote routes may only reorder / drop providers the local placement already uses, with the local unit id.
        private static List<AdRoute> MergeRoutes(AdPlacement local, List<AdRoute> remote, bool log)
        {
            var result = new List<AdRoute>(remote.Count);
            foreach (AdRoute r in remote)
            {
                if (r == null) continue;
                string id = r.EffectiveProviderId;
                AdRoute localRoute = FindRoute(local, id);
                if (localRoute == null)
                {
                    if (log) Debug.LogWarning($"{LogPrefix} Override for '{local.Id}': provider '{id}' is not routed locally - ignored (remote config cannot add providers).");
                    continue;
                }
                if (result.Exists(x => x.EffectiveProviderId == id)) continue;
                result.Add(localRoute.Clone());
            }
            return result;
        }

        private static AdRoute FindRoute(AdPlacement p, string providerId)
        {
            if (p.Routes == null) return null;
            foreach (AdRoute l in p.Routes)
                if (l != null && l.EffectiveProviderId == providerId) return l;
            return null;
        }

        /// <summary>
        /// Provider ids a placement may use right now, in priority order: its effective routes, limited to installed
        /// adapters that did not fail to initialize and to the plan's provider list. Empty for unknown placements.
        /// </summary>
        public IReadOnlyList<string> GetEffectiveRoutes(string placementId)
        {
            if (string.IsNullOrEmpty(placementId) || !placements.TryGetValue(placementId, out AdPlacement p)) return Array.Empty<string>();
            return EffectiveRoutes(p);
        }

        /// <summary>True when the composite told this provider to keep the placement loaded.</summary>
        public bool IsRouteActive(string placementId, string providerId) =>
            placementId != null && activeRoutes.Contains((placementId, AdProviderIds.Normalize(providerId)));

        private List<string> EffectiveRoutes(AdPlacement p)
        {
            var list = new List<string>();
            if (p.Routes != null)
            {
                foreach (AdRoute r in p.Routes)
                {
                    if (r == null) continue;
                    string id = r.EffectiveProviderId;
                    if (Usable(id) && HasUnit(id, p.Id) && !list.Contains(id)) list.Add(id);
                }
            }
            // No SDK installed: the mock adapter serves every placement (Editor / development builds only).
            if (!list.Contains(AdProviderIds.None) && Usable(AdProviderIds.None)
                && byId[AdProviderIds.None] is NullAdsAdapter { ServesAllPlacements: true })
                list.Add(AdProviderIds.None);
            return list;
        }

        private bool Usable(string providerId) =>
            byId.ContainsKey(providerId) && !droppedProviders.Contains(providerId)
            && (planProviders == null || planProviders.Contains(providerId) || providerId == AdProviderIds.None);

        // False when the provider initialized without a unit for this placement (empty unit id on this platform):
        // it would never load nor report a failure, so under Primary the fallback would never open.
        private bool HasUnit(string providerId, string placementId) =>
            !assignedPlacements.TryGetValue(providerId, out HashSet<string> assigned) || assigned.Contains(placementId);

        private IEnumerable<IAdsProviderAdapter> RoutedAdapters(AdPlacement p)
        {
            foreach (string id in EffectiveRoutes(p)) yield return byId[id];
        }

        private void ReconcileAll()
        {
            foreach (string id in placements.Keys) Reconcile(id);
        }

        // Tells adapters which placements to keep loaded: Primary = the first route plus fallbacks opened by load
        // failures; Parallel = every route. A route that is showing stays active until the ad closes.
        private void Reconcile(string placementId)
        {
            if (disposed) return;
            var desired = new HashSet<string>();
            if (placements.TryGetValue(placementId, out AdPlacement p) && !disabled.Contains(placementId)
                && !(p.SuppressedByNoAds && noAdsApplied))
            {
                List<string> routes = EffectiveRoutes(p);
                int count = routes.Count;
                if (loadStrategy == AdLoadStrategy.Primary && count > 0)
                {
                    int depth = primaryDepth.TryGetValue(placementId, out int d) ? d : 1;
                    count = Mathf.Clamp(depth, 1, routes.Count);
                }
                for (int i = 0; i < count; i++) desired.Add(routes[i]);
                if (showingProvider.TryGetValue(placementId, out string showing)) desired.Add(showing);
                desired.RemoveWhere(id => !initializedProviders.Contains(id));
            }

            foreach (KeyValuePair<string, IAdsProviderAdapter> kv in byId)
            {
                bool want = desired.Contains(kv.Key);
                bool has  = activeRoutes.Contains((placementId, kv.Key));
                if (want == has) continue;
                if (want) activeRoutes.Add((placementId, kv.Key));
                else activeRoutes.Remove((placementId, kv.Key));
                try { kv.Value.SetPlacementActive(placementId, want); }
                catch (Exception e) { Debug.LogWarning($"{LogPrefix} {kv.Key} SetPlacementActive('{placementId}', {want}) threw: {e.Message}"); }
            }
        }

        /// <summary>
        /// Re-reads the no-ads ownership. When it turned on: suppressed banners are destroyed and adapters stop loading
        /// suppressed placements. Called on every show request and after bootstrap; call it yourself right after a
        /// successful no-ads purchase so a visible banner disappears immediately.
        /// </summary>
        public void RefreshNoAds()
        {
            if (refreshingNoAds) return;
            bool owned;
            try { owned = noAdsOwned != null && noAdsOwned(); }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} No-ads ownership check threw: {e.Message}");
                return;
            }
            if (owned == noAdsApplied) return;

            refreshingNoAds = true;
            try
            {
                noAdsApplied = owned;
                foreach (AdPlacement p in placements.Values)
                {
                    if (!p.SuppressedByNoAds) continue;
                    foreach (IAdsProviderAdapter adapter in adapters)
                    {
                        if (owned && p.IsBannerLike) adapter.DestroyBanner(p.Id);
                        (adapter as ISuppressibleAdsAdapter)?.SetSuppressed(p.Id, owned);
                    }
                }
                ReconcileAll();
                if (owned) Debug.Log($"{LogPrefix} No-ads owned: suppressed placements stopped and banners destroyed.");
            }
            finally
            {
                refreshingNoAds = false;
            }
        }

        /// <inheritdoc />
        public bool IsPlacementReady(string placementId)
        {
            if (string.IsNullOrEmpty(placementId) || !placements.TryGetValue(placementId, out AdPlacement p)) return false;
            if (disabled.Contains(placementId)) return false;
            foreach (IAdsProviderAdapter adapter in RoutedAdapters(p))
                if (adapter.IsPlacementReady(placementId)) return true;
            return false;
        }

        /// <inheritdoc />
        public bool CanShow(string placementId, out AdBlockReason reason)
        {
            if (string.IsNullOrEmpty(placementId) || !placements.TryGetValue(placementId, out AdPlacement p))
            {
                reason = AdBlockReason.UnknownPlacement;
                return false;
            }
            if (disabled.Contains(placementId))
            {
                reason = AdBlockReason.PlacementDisabled;
                return false;
            }
            if (p.SuppressedByNoAds && IsNoAdsOwned())
            {
                reason = AdBlockReason.NoAdsOwned;
                return false;
            }
            if (EffectiveRoutes(p).Count == 0)
            {
                reason = AdBlockReason.NoProvider;
                return false;
            }
            if (!p.IsBannerLike)
            {
                float t = now();
                if (IsShowingFullscreen)
                {
                    reason = AdBlockReason.AnotherAdShowing;
                    return false;
                }
                if (p.Format == AdFormat.AppOpen && policy.AppOpenSuppressSecondsAfterFullscreen > 0f
                    && t - lastFullscreenClosedAt < policy.AppOpenSuppressSecondsAfterFullscreen)
                {
                    reason = AdBlockReason.AfterFullscreenAd;
                    return false;
                }
                if (p.Format == AdFormat.AppOpen && policy.AppOpenMinIntervalSeconds > 0f
                    && t - lastAppOpenAt < policy.AppOpenMinIntervalSeconds)
                {
                    reason = AdBlockReason.AppOpenInterval;
                    return false;
                }
                if (p.Format == AdFormat.Interstitial || (p.Format == AdFormat.AppOpen && policy.AppOpenFollowsInterstitialRules))
                {
                    if (policy.FirstInterstitialDelaySeconds > 0f && t - startedAt < policy.FirstInterstitialDelaySeconds)
                    {
                        reason = AdBlockReason.SessionStartGrace;
                        return false;
                    }
                    if (policy.InterstitialMinIntervalSeconds > 0f && t - lastInterstitialAt < policy.InterstitialMinIntervalSeconds)
                    {
                        reason = AdBlockReason.GlobalInterval;
                        return false;
                    }
                }
                if (p.MaxShowsPerSession > 0 && showsThisSession.TryGetValue(placementId, out int shows) && shows >= p.MaxShowsPerSession)
                {
                    reason = AdBlockReason.SessionCap;
                    return false;
                }
                if (p.CooldownSeconds > 0f && lastShownAt.TryGetValue(placementId, out float at) && t - at < p.CooldownSeconds)
                {
                    reason = AdBlockReason.Cooldown;
                    return false;
                }
                if (!IsPlacementReady(placementId))
                {
                    reason = AdBlockReason.NotReady;
                    return false;
                }
            }
            reason = AdBlockReason.None;
            return true;
        }

        private static bool IsInterstitialLike(AdFormat format) => format == AdFormat.Interstitial || format == AdFormat.AppOpen;

        /// <inheritdoc />
        public async ValueTask<AdShowResult> ShowAsync(string placementId, CancellationToken cancellationToken = default)
        {
            RefreshNoAds();
            if (!CanShow(placementId, out AdBlockReason reason))
            {
                Debug.LogWarning($"{LogPrefix} Placement '{placementId}' not shown: {reason}.");
                return AdShowResult.Failed;
            }
            if (cancellationToken.IsCancellationRequested) return AdShowResult.Failed;

            AdPlacement p = placements[placementId];
            if (p.IsBannerLike)
            {
                // Desired visibility: the first route with a live adapter gets the request even before its banner
                // loaded - the adapter shows it as soon as the load completes.
                foreach (IAdsProviderAdapter adapter in RoutedAdapters(p))
                {
                    AdShowResult r = await adapter.ShowAsync(placementId, cancellationToken);
                    RaiseShowResult(placementId, adapter.ProviderId, r);
                    return r;
                }
                Debug.LogWarning($"{LogPrefix} Banner '{placementId}': no adapter installed for any of its routes.");
                return AdShowResult.Failed;
            }

            // Claimed synchronously (before the first await) so a second ShowAsync in the same frame sees it.
            SetShowingFullscreen(placementId);
            bool displayed = false;
            try
            {
                foreach (IAdsProviderAdapter adapter in RoutedAdapters(p))
                {
                    if (!adapter.IsPlacementReady(placementId)) continue;
                    string providerId = AdProviderIds.Normalize(adapter.ProviderId);
                    showingProvider[placementId] = providerId;
                    AdShowResult result;
                    try
                    {
                        result = await adapter.ShowAsync(placementId, cancellationToken);
                    }
                    finally
                    {
                        showingProvider.Remove(placementId);
                    }
                    RaiseShowResult(placementId, providerId, result);
                    // Skipped = the user closed it deliberately; that still counts as a show.
                    if (result != AdShowResult.Failed)
                    {
                        displayed = true;
                        float t = now();
                        showsThisSession[placementId] = showsThisSession.TryGetValue(placementId, out int n) ? n + 1 : 1;
                        lastShownAt[placementId] = t;
                        if (IsInterstitialLike(p.Format)) lastInterstitialAt = t;
                        if (p.Format == AdFormat.AppOpen) lastAppOpenAt = t;
                        else if (p.Format == AdFormat.Rewarded && result == AdShowResult.Completed
                                 && policy.RewardedResetsInterstitialTimer) lastInterstitialAt = t;
                        return result;
                    }
                    // The caller gave up: never open a second ad from the next route.
                    if (cancellationToken.IsCancellationRequested) return AdShowResult.Failed;
                }
                Debug.LogWarning($"{LogPrefix} Placement '{placementId}': every ready route failed at display.");
                return AdShowResult.Failed;
            }
            finally
            {
                if (displayed) lastFullscreenClosedAt = now();
                SetShowingFullscreen(null);
                Reconcile(placementId);
            }
        }

        /// <inheritdoc />
        public float GetBannerHeightPixels(string placementId)
        {
            if (string.IsNullOrEmpty(placementId) || !placements.TryGetValue(placementId, out AdPlacement p) || !p.IsBannerLike)
                return 0f;
            foreach (IAdsProviderAdapter adapter in RoutedAdapters(p))
            {
                if (adapter is not IBannerMetrics metrics || !adapter.IsPlacementReady(placementId)) continue;
                float height = metrics.GetBannerHeightPixels(placementId);
                if (height > 0f) return height;
            }
            return 0f;
        }

        /// <inheritdoc />
        public float GetExpectedBannerHeightPixels(string placementId)
        {
            if (string.IsNullOrEmpty(placementId) || !placements.TryGetValue(placementId, out AdPlacement p) || !p.IsBannerLike)
                return 0f;
            if (disabled.Contains(placementId) || (p.SuppressedByNoAds && IsNoAdsOwned())) return 0f;
            float loaded = GetBannerHeightPixels(placementId);
            if (loaded > 0f) return loaded;
            foreach (IAdsProviderAdapter adapter in RoutedAdapters(p))
            {
                if (adapter is not IBannerMetrics metrics || !activeRoutes.Contains((placementId, AdProviderIds.Normalize(adapter.ProviderId)))) continue;
                float height = metrics.GetExpectedBannerHeightPixels(placementId);
                if (height > 0f) return height;
            }
            return 0f;
        }

        /// <inheritdoc />
        public void HideBanner(string placementId)
        {
            if (string.IsNullOrEmpty(placementId)) return;
            foreach (IAdsProviderAdapter adapter in adapters) adapter.HideBanner(placementId);
        }

        /// <inheritdoc />
        public void DestroyBanner(string placementId)
        {
            if (string.IsNullOrEmpty(placementId)) return;
            foreach (IAdsProviderAdapter adapter in adapters) adapter.DestroyBanner(placementId);
        }

        /// <summary>Unsubscribes from and disposes every adapter.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (Action a in unsubscribe) a();
            unsubscribe.Clear();
            foreach (IAdsProviderAdapter adapter in adapters)
            {
                try { adapter.Dispose(); }
                catch (Exception e) { Debug.LogWarning($"{LogPrefix} {adapter.ProviderId} Dispose threw: {e.Message}"); }
            }
        }

        /// <summary>
        /// Default init context of one provider: every placement routed to it with the unit id of the current platform
        /// (routes with an empty unit id are skipped with a warning, except for the mock provider).
        /// </summary>
        /// <param name="allPlacements">True = hand every placement over (the mock adapter when no SDK is installed).</param>
        public static AdsProviderContext BuildProviderContext(AdsModule settings, string providerId, ConsentState consent,
                                                              bool allPlacements = false)
        {
            string id = AdProviderIds.Normalize(providerId);
            var units = new List<AdUnitAssignment>();
            if (settings != null && settings.Placements != null)
            {
                foreach (AdPlacement p in settings.Placements)
                {
                    if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                    AdRoute route = FindRoute(p, id);
                    if (route == null)
                    {
                        if (allPlacements) units.Add(new AdUnitAssignment(p, null));
                        continue;
                    }
                    string unit = route.CurrentAdUnitId;
                    if (id != AdProviderIds.None && string.IsNullOrEmpty(unit))
                    {
                        Debug.LogWarning($"{LogPrefix} Placement '{p.Id}': {id} route has no ad unit id for this platform - skipped.");
                        continue;
                    }
                    units.Add(new AdUnitAssignment(p, unit));
                }
            }
            return new AdsProviderContext(settings, settings != null ? settings.FindProvider(id) : null, units, consent?.Clone());
        }

        private bool IsNoAdsOwned()
        {
            try { return noAdsOwned != null && noAdsOwned(); }
            catch (Exception) { return false; }
        }

        private void SetShowingFullscreen(string placementId)
        {
            bool was = showingFullscreen != null;
            showingFullscreen = placementId;
            bool isNow = placementId != null;
            if (was == isNow) return;
            try { FullscreenShowingChanged?.Invoke(isNow); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void RaiseShowResult(string placementId, string providerId, AdShowResult result)
        {
            try { ShowResult?.Invoke(placementId, AdProviderIds.Normalize(providerId), result); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void OnAdapterClicked(string placementId)
        {
            if (disposed) return;
            try { AdClicked?.Invoke(placementId); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void RaiseReadyChanged(string placementId)
        {
            try { PlacementReadyChanged?.Invoke(placementId); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private int RouteIndex(string placementId, IAdsProviderAdapter adapter, out int routeCount)
        {
            routeCount = 0;
            if (placementId == null || !placements.TryGetValue(placementId, out AdPlacement p)) return -1;
            List<string> routes = EffectiveRoutes(p);
            routeCount = routes.Count;
            return routes.IndexOf(AdProviderIds.Normalize(adapter.ProviderId));
        }

        private void OnAdapterReadyChanged(IAdsProviderAdapter adapter, string placementId)
        {
            if (disposed) return;
            // A load landing after the IAP service initialized is a cheap moment to notice an owned no-ads product.
            RefreshNoAds();
            if (loadStrategy == AdLoadStrategy.Primary)
            {
                int index = RouteIndex(placementId, adapter, out _);
                // A higher-priority route is ready again: the lower fallbacks stop loading (unless one is showing).
                if (index >= 0 && primaryDepth.TryGetValue(placementId, out int depth) && index + 1 < depth
                    && SafeReady(adapter, placementId))
                {
                    primaryDepth[placementId] = index + 1;
                    Reconcile(placementId);
                }
            }
            RaiseReadyChanged(placementId);
        }

        private void OnAdapterLoadFailed(IAdsProviderAdapter adapter, string placementId, string error)
        {
            if (disposed || loadStrategy != AdLoadStrategy.Primary) return;
            int index = RouteIndex(placementId, adapter, out int count);
            if (index < 0 || index + 1 >= count) return;
            int depth = primaryDepth.TryGetValue(placementId, out int d) ? d : 1;
            if (index + 2 <= depth) return;
            // The last active route failed to load: open the next one as a fallback.
            primaryDepth[placementId] = index + 2;
            Debug.Log($"{LogPrefix} '{placementId}': {adapter.ProviderId} failed to load ({error}) - also loading the next route.");
            Reconcile(placementId);
        }

        private static bool SafeReady(IAdsProviderAdapter adapter, string placementId)
        {
            try { return adapter.IsPlacementReady(placementId); }
            catch (Exception) { return false; }
        }

        private void OnImpressionPaid(AdImpression impression)
        {
            try { ImpressionPaid?.Invoke(impression); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
