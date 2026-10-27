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
        // EffectiveRoutes per placement object, cleared whenever an input changes (InvalidateRoutes). The lists are never
        // mutated once cached, so a caller iterating one while the cache is cleared keeps a consistent snapshot.
        private readonly Dictionary<AdPlacement, List<string>> routeCache = new();
        private readonly HashSet<(string Placement, string Provider)> activeRoutes = new();
        private readonly Dictionary<string, int> primaryDepth = new();
        private readonly Dictionary<string, string> showingProvider = new();
        private readonly Dictionary<string, int> showsThisSession = new();
        private readonly Dictionary<string, float> lastShownAt = new();
        // ShowAsync requests since the last show, per placement (AdPlacement.ShowEveryNthRequest). A placement with
        // PersistRequestCount reads its count from PlayerPrefs the first time and writes every change back.
        private readonly Dictionary<string, int> requestsSinceShow = new();
        private readonly List<AdShowRule> rules = new();
        // Read by CanShow (polled by UI) and the notifications: a rule may add / remove rules while it runs.
        private AdShowRule[] ruleSnapshot = Array.Empty<AdShowRule>();
        private readonly Dictionary<string, float> visibleBannerHeight = new();
        // Banners the game wants visible (ShowAsync until HideBanner / DestroyBanner), and the provider asked to show each.
        private readonly HashSet<string> wantedBanners = new();
        private readonly Dictionary<string, string> bannerProvider = new();
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
        private (float Volume, bool Muted)? adAudio; // null = never set: the SDK defaults stay
        private Task initializationTask;
        private string showingFullscreen;
        private float lastFullscreenClosedAt = float.NegativeInfinity;
        private float lastInterstitialAt = float.NegativeInfinity;
        private float lastAppOpenAt = float.NegativeInfinity;
        // The previous session's last interstitial on this clock (AdsPolicy.InterstitialIntervalAcrossSessions).
        private float previousSessionInterstitialAt = float.NegativeInfinity;
        private int playerLevel;
        private bool noAdsApplied;
        private bool refreshingNoAds;
        private bool disposed;

        /// <summary>Raised after every adapter show: placement id, provider id, result.</summary>
        public event Action<string, string, AdShowResult> ShowResult;

        /// <inheritdoc />
        public event Action<string> PlacementReadyChanged;

        /// <summary>
        /// The on-screen height of a banner / MREC placement changed (<see cref="GetVisibleBannerHeightPixels"/>): it
        /// loaded while shown, was shown, hidden or destroyed, or no-ads removed it. Arguments: placement id, new height
        /// in screen pixels (0 = nothing on screen). Lay the UI out above the banner from here.
        /// </summary>
        public event Action<string, float> BannerHeightChanged;

        /// <inheritdoc />
        public event Action<string, AdBlockReason> ShowRequested;

        /// <inheritdoc />
        public event Action<string, string> PlacementLoaded;

        /// <inheritdoc />
        public event Action<string, string, string> PlacementLoadFailed;

        /// <summary>
        /// Raised when a fullscreen ad starts (true) or ends (false). Pause audio / gameplay on it, or turn on
        /// <c>AdsModule.PauseGameDuringFullscreenAds</c> / <c>MuteAudioDuringFullscreenAds</c>.
        /// </summary>
        public event Action<bool> FullscreenShowingChanged;

        /// <summary>Every paid impression of every adapter (impression-level revenue).</summary>
        public event Action<AdImpression> ImpressionPaid;

        /// <summary>Every ad click of every adapter that reports clicks (<see cref="IAdClickSource"/>): placement id.</summary>
        public event Action<string> AdClicked;

        /// <summary>
        /// Raised with the provider id right before an SDK debug screen opens (<see cref="OpenDebuggerAsync"/>). On Android
        /// it is a separate activity: the installer marks the trip as game-initiated, so the return shows no app open ad.
        /// </summary>
        public event Action<string> DebuggerOpening;

        /// <summary>
        /// Raised with the provider id when an adapter that timed out at init finished later and its routes came back.
        /// A CMP that runs inside that SDK's init (MAX) only answers now: the installer re-publishes consent on it.
        /// </summary>
        public event Action<string> ProviderJoinedLate;

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
            previousSessionInterstitialAt = LoadPreviousInterstitial(startedAt);
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

        /// <summary>PlayerPrefs key of the last interstitial's UTC time (unix seconds), for <c>InterstitialIntervalAcrossSessions</c>.</summary>
        public const string LastInterstitialKey = "gamelib.ads.last_interstitial_utc";

        /// <summary>PlayerPrefs key prefix (+ placement id) of the saved request count, for <c>AdPlacement.PersistRequestCount</c>.</summary>
        public const string RequestCountKeyPrefix = "gamelib.ads.requests.";

        /// <summary>The player's level for <c>AdsPolicy.InterstitialFromLevel</c>. The game sets it (<c>SetPlayerLevel</c>).</summary>
        public int PlayerLevel
        {
            get => playerLevel;
            set
            {
                if (playerLevel == value) return;
                playerLevel = value;
                if (policy.InterstitialFromLevel > 0) RaiseReadyChangedForInterstitials();
            }
        }

        /// <summary>Adds a game rule for fullscreen ads (see <see cref="AdShowRule"/>).</summary>
        public void AddRule(AdShowRule rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            if (rules.Contains(rule)) return;
            rules.Add(rule);
            ruleSnapshot = rules.ToArray();
            RaiseReadyChangedForFullscreen();
        }

        /// <summary>
        /// Forgets every pacing record, as if no fullscreen ad had been shown yet: the interstitial interval (also the one
        /// saved for <c>InterstitialIntervalAcrossSessions</c>), the app open interval and its block after a fullscreen
        /// ad, every placement's cooldown and session count, and the every-Nth request counts (saved ones too).
        /// Not reset: the session start grace (<c>FirstInterstitialDelaySeconds</c>) and the game's own rules.
        /// For a QA / cheat button, or when remote config says so.
        /// </summary>
        public void ResetPacing()
        {
            lastInterstitialAt = float.NegativeInfinity;
            previousSessionInterstitialAt = float.NegativeInfinity;
            lastAppOpenAt = float.NegativeInfinity;
            lastFullscreenClosedAt = float.NegativeInfinity;
            lastShownAt.Clear();
            showsThisSession.Clear();
            try
            {
                PlayerPrefs.DeleteKey(LastInterstitialKey);
                foreach (AdPlacement p in placements.Values)
                    if (p.PersistRequestCount) PlayerPrefs.DeleteKey(RequestCountKeyPrefix + p.Id);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Saved pacing not cleared ({e.Message}).");
            }
            requestsSinceShow.Clear();
            Debug.Log($"{LogPrefix} Pacing reset.");
            RaiseReadyChangedForFullscreen();
        }

        /// <summary>
        /// Opens the debug screen of <paramref name="providerId"/> (<see cref="IAdsDebugger"/>: AdMob Ad Inspector, MAX
        /// Mediation Debugger), or of the first initialized provider that has one when null. False when none could open.
        /// </summary>
        public async Task<bool> OpenDebuggerAsync(string providerId = null)
        {
            string wanted = string.IsNullOrEmpty(providerId) ? null : AdProviderIds.Normalize(providerId);
            foreach (IAdsProviderAdapter adapter in adapters)
            {
                string id = AdProviderIds.Normalize(adapter.ProviderId);
                if (wanted != null && id != wanted) continue;
                if (adapter is not IAdsDebugger debugger || !adapter.IsInitialized) continue;
                try { DebuggerOpening?.Invoke(id); }
                catch (Exception e) { Debug.LogException(e); }
                try
                {
                    return await debugger.OpenDebuggerAsync();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{LogPrefix} {id} debugger failed: {e.Message}");
                    return false;
                }
            }
            Debug.LogWarning($"{LogPrefix} No ad debugger to open{(wanted != null ? $" for '{wanted}'" : string.Empty)}: " +
                             "the provider is not installed, not initialized yet, or has none (the Editor mock has none).");
            return false;
        }

        /// <summary>
        /// Tells every SDK the game's sound setting (<see cref="IAdsAudio"/>), now and for SDKs that initialize later. Call
        /// it at start and on every change of the game's own volume / sound toggle - only when the game has one: Google
        /// warns a low or muted app volume makes fewer video ads eligible, which can lower revenue.
        /// </summary>
        public void SetAdAudio(float volume, bool muted)
        {
            adAudio = (Mathf.Clamp01(volume), muted);
            foreach (IAdsProviderAdapter adapter in adapters)
                if (adapter.IsInitialized) SafeApplyAdAudio(adapter);
        }

        private void SafeApplyAdAudio(IAdsProviderAdapter adapter)
        {
            if (adAudio is not { } audio || adapter is not IAdsAudio target) return;
            try { target.ApplyAdAudio(audio.Volume, audio.Muted); }
            catch (Exception e) { Debug.LogWarning($"{LogPrefix} {adapter.ProviderId} ApplyAdAudio threw: {e.Message}"); }
        }

        /// <summary>Removes a rule added with <see cref="AddRule"/>.</summary>
        public bool RemoveRule(AdShowRule rule)
        {
            if (!rules.Remove(rule)) return false;
            ruleSnapshot = rules.ToArray();
            RaiseReadyChangedForFullscreen();
            return true;
        }

        private void RaiseReadyChangedForInterstitials()
        {
            foreach (AdPlacement p in placements.Values)
                if (p.Format == AdFormat.Interstitial || (p.Format == AdFormat.AppOpen && policy.AppOpenFollowsInterstitialRules))
                    RaiseReadyChanged(p.Id);
        }

        private void RaiseReadyChangedForFullscreen()
        {
            foreach (AdPlacement p in placements.Values)
                if (!p.IsBannerLike) RaiseReadyChanged(p.Id);
        }

        private static float LoadPreviousInterstitial(float clockNow)
        {
            try
            {
                if (!long.TryParse(PlayerPrefs.GetString(LastInterstitialKey, string.Empty), out long at)) return float.NegativeInfinity;
                long ago = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - at;
                return ago < 0 ? float.NegativeInfinity : clockNow - ago; // a clock set back: ignore the saved time
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Last interstitial time unavailable ({e.Message}).");
                return float.NegativeInfinity;
            }
        }

        private void SaveInterstitialTime()
        {
            try
            {
                // Saved now: an app swiped away from the background gets no OnApplicationQuit. Once per interstitial.
                PlayerPrefs.SetString(LastInterstitialKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Last interstitial time not saved ({e.Message}).");
            }
        }

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
                InvalidateRoutes();
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
            SafeApplyAdAudio(adapter);
            ReconcileAll();
        }

        private void DropProvider(string id, string reason)
        {
            // One SDK failing to initialize must not take the other providers down: its routes simply disappear.
            Debug.LogError($"{LogPrefix} {id} failed to initialize: {reason} - its routes are dropped.");
            droppedProviders.Add(id);
            InvalidateRoutes();
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
            foreach (IAdsProviderAdapter adapter in adapters)
                if (AdProviderIds.Normalize(adapter.ProviderId) == id) SafeApplyAdAudio(adapter);
            InvalidateRoutes();
            initializedProviders.Add(id);
            Debug.Log($"{LogPrefix} {id} finished initializing late - its routes are back.");
            // Consent first (the module republishes it), then the provider starts loading.
            try { ProviderJoinedLate?.Invoke(id); }
            catch (Exception e) { Debug.LogException(e); }
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
            InvalidateRoutes();
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
                if (pp.InterstitialIntervalAcrossSessions.HasValue)    policy.InterstitialIntervalAcrossSessions    = pp.InterstitialIntervalAcrossSessions.Value;
                if (pp.InterstitialFromLevel.HasValue)                 policy.InterstitialFromLevel                 = Math.Max(0, pp.InterstitialFromLevel.Value);
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
                if (o.ShowEveryNthRequest.HasValue) merged.ShowEveryNthRequest = o.ShowEveryNthRequest;
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
            InvalidateRoutes();
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
                    if (rule.ShowEveryNthRequest.HasValue)  p.ShowEveryNthRequest = Math.Max(0, rule.ShowEveryNthRequest.Value);
                    if (rule.PersistRequestCount.HasValue)  p.PersistRequestCount = rule.PersistRequestCount.Value;
                    if (rule.WaitForFillSeconds.HasValue)   p.WaitForFillSeconds = Mathf.Max(0f, rule.WaitForFillSeconds.Value);
                    if (rule.SuppressedByNoAds.HasValue)    p.SuppressedByNoAds  = rule.SuppressedByNoAds.Value;
                    if (rule.Routes != null && rule.Routes.Count > 0) p.Routes = OrderRoutes(p, rule.Routes, logPlan);
                }
            }

            foreach (AdPlacementOverride o in legacyOverrides.Values)
            {
                if (!placements.TryGetValue(o.Id, out AdPlacement local)) continue;
                if (o.CooldownSeconds.HasValue)    local.CooldownSeconds    = Mathf.Max(0f, o.CooldownSeconds.Value);
                if (o.MaxShowsPerSession.HasValue) local.MaxShowsPerSession = Math.Max(0, o.MaxShowsPerSession.Value);
                if (o.ShowEveryNthRequest.HasValue) local.ShowEveryNthRequest = Math.Max(0, o.ShowEveryNthRequest.Value);
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
            return EffectiveRoutes(p).ToArray();
        }

        /// <summary>True when the composite told this provider to keep the placement loaded.</summary>
        public bool IsRouteActive(string placementId, string providerId) =>
            placementId != null && activeRoutes.Contains((placementId, AdProviderIds.Normalize(providerId)));

        /// <summary>True when an initialized provider was told to keep the placement loaded (ads have started for it).</summary>
        public bool HasActiveRoute(string placementId)
        {
            if (placementId == null) return false;
            foreach ((string Placement, string Provider) route in activeRoutes)
                if (route.Placement == placementId) return true;
            return false;
        }

        // Called on every readiness check (CanShow / IsPlacementReady, often from UI every frame): served from routeCache.
        private List<string> EffectiveRoutes(AdPlacement p)
        {
            if (routeCache.TryGetValue(p, out List<string> cached)) return cached;
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
            routeCache[p] = list;
            return list;
        }

        private void InvalidateRoutes() => routeCache.Clear();

        private bool Usable(string providerId) =>
            byId.ContainsKey(providerId) && !droppedProviders.Contains(providerId)
            && (planProviders == null || planProviders.Contains(providerId) || providerId == AdProviderIds.None);

        // False when the provider initialized without a unit for this placement (empty unit id on this platform):
        // it would never load nor report a failure, so under Primary the fallback would never open.
        private bool HasUnit(string providerId, string placementId) =>
            !assignedPlacements.TryGetValue(providerId, out HashSet<string> assigned) || assigned.Contains(placementId);

        // A fullscreen placement that used up MaxShowsPerSession cannot show again this session: keeping it loaded would
        // hold an ad (several MB for a video) and its reload request for nothing.
        private bool SessionCapReached(AdPlacement p) =>
            !p.IsBannerLike && p.MaxShowsPerSession > 0
            && showsThisSession.TryGetValue(p.Id, out int shows) && shows >= p.MaxShowsPerSession;

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
                bool capped = SessionCapReached(p);
                List<string> routes = EffectiveRoutes(p);
                int count = routes.Count;
                if (loadStrategy == AdLoadStrategy.Primary && count > 0)
                {
                    int depth = primaryDepth.TryGetValue(placementId, out int d) ? d : 1;
                    count = Mathf.Clamp(depth, 1, routes.Count);
                }
                if (!capped)
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
            SyncBanner(placementId);
        }

        // The provider that should display a wanted banner: the first active route with a loaded banner, else the
        // newest fallback under Primary (earlier routes failed to load), else the first active route. Null = none active.
        private bool NoAdsOwnedNow()
        {
            try { return noAdsOwned != null && noAdsOwned(); }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} no-ads check threw: {e.Message}");
                return false;
            }
        }

        private string ServingBannerProvider(AdPlacement p)
        {
            string fallback = null;
            foreach (string id in EffectiveRoutes(p))
            {
                if (!activeRoutes.Contains((p.Id, id))) continue;
                if (SafeReady(byId[id], p.Id)) return id;
                if (fallback == null || loadStrategy == AdLoadStrategy.Primary) fallback = id;
            }
            return fallback;
        }

        // Re-issues a wanted banner to the provider now serving it: a show requested before init, or a fallback route
        // opened by a load failure, would otherwise never display.
        private void SyncBanner(string placementId)
        {
            if (!wantedBanners.Contains(placementId) || !placements.TryGetValue(placementId, out AdPlacement p) || !p.IsBannerLike)
                return;
            // Read live, not the cached suppression: a banner must never come back once no-ads is owned.
            if (p.SuppressedByNoAds && NoAdsOwnedNow()) return;
            string target = ServingBannerProvider(p);
            bannerProvider.TryGetValue(placementId, out string current);
            if (target == current) return;
            bannerProvider.Remove(placementId);
            // Hidden even when its route was deactivated: MAX keeps a banner unit alive while another placement uses it.
            if (current != null) SafeHideBanner(byId[current], placementId);
            if (target == null) return;
            bannerProvider[placementId] = target;
            _ = ShowBannerOnAsync(byId[target], placementId, CancellationToken.None);
        }

        private async Task<AdShowResult> ShowBannerOnAsync(IAdsProviderAdapter adapter, string placementId, CancellationToken cancellationToken)
        {
            string providerId = AdProviderIds.Normalize(adapter.ProviderId);
            AdShowResult result;
            try
            {
                result = await adapter.ShowAsync(placementId, cancellationToken);
            }
            catch (Exception e)
            {
                if (!(e is OperationCanceledException)) Debug.LogWarning($"{LogPrefix} {providerId} banner '{placementId}' threw: {e.Message}");
                result = AdShowResult.Failed;
            }
            // A failed show is retried on the next route change.
            if (result == AdShowResult.Failed && bannerProvider.TryGetValue(placementId, out string serving) && serving == providerId)
                bannerProvider.Remove(placementId);
            RaiseShowResult(placementId, providerId, result);
            return result;
        }

        private static void SafeHideBanner(IAdsProviderAdapter adapter, string placementId)
        {
            try { adapter.HideBanner(placementId); }
            catch (Exception e) { Debug.LogWarning($"{LogPrefix} {adapter.ProviderId} HideBanner('{placementId}') threw: {e.Message}"); }
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
                // Adapters do not all report a destroyed banner: tell the layout here.
                foreach (AdPlacement p in new List<AdPlacement>(placements.Values))
                    if (p.IsBannerLike) CheckBannerHeight(p.Id);
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
            foreach (string routeId in EffectiveRoutes(p))
                if (byId[routeId].IsPlacementReady(placementId)) return true;
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
                    float lastAt = lastInterstitialAt;
                    if (policy.InterstitialIntervalAcrossSessions && float.IsNegativeInfinity(lastAt)) lastAt = previousSessionInterstitialAt;
                    if (policy.InterstitialMinIntervalSeconds > 0f && t - lastAt < policy.InterstitialMinIntervalSeconds)
                    {
                        reason = AdBlockReason.GlobalInterval;
                        return false;
                    }
                    if (policy.InterstitialFromLevel > 0 && playerLevel < policy.InterstitialFromLevel)
                    {
                        reason = AdBlockReason.BelowLevel;
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
                // This request would be the Nth: the counter only moves in ShowAsync, so CanShow stays side-effect free.
                if (p.ShowEveryNthRequest > 1 && RequestsSinceShow(p) + 1 < p.ShowEveryNthRequest)
                {
                    reason = AdBlockReason.RequestCount;
                    return false;
                }
                AdShowRule[] gameRules = ruleSnapshot;
                for (int i = 0; i < gameRules.Length; i++)
                {
                    if (RuleAllows(gameRules[i], p)) continue;
                    reason = AdBlockReason.GameRule;
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


        private static bool RuleAllows(AdShowRule rule, AdPlacement p)
        {
            try { return rule.Allows(p); }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Rule {rule.GetType().Name} threw for '{p.Id}': {e.Message} - not shown.");
                return false;
            }
        }

        // A fullscreen request: moves the every-Nth counter (unless the placement cannot show at all) and tells the rules.
        private void NoteRequest(string placementId, AdBlockReason reason)
        {
            if (!placements.TryGetValue(placementId ?? string.Empty, out AdPlacement p) || p.IsBannerLike) return;
            if (reason is AdBlockReason.UnknownPlacement or AdBlockReason.PlacementDisabled or AdBlockReason.NoAdsOwned
                       or AdBlockReason.NoProvider)
                return;
            if (p.ShowEveryNthRequest > 1)
            {
                int requests = RequestsSinceShow(p) + 1;
                SetRequestsSinceShow(p, requests);
                if (requests == p.ShowEveryNthRequest - 1) RaiseReadyChanged(placementId); // the next request may show
            }
            foreach (AdShowRule rule in ruleSnapshot)
            {
                try { rule.OnRequested(p, reason); }
                catch (Exception e) { Debug.LogWarning($"{LogPrefix} Rule {rule.GetType().Name}.OnRequested threw: {e.Message}"); }
            }
        }

        private int RequestsSinceShow(AdPlacement p)
        {
            if (requestsSinceShow.TryGetValue(p.Id, out int n)) return n;
            n = 0;
            if (p.PersistRequestCount)
            {
                try { n = Math.Max(0, PlayerPrefs.GetInt(RequestCountKeyPrefix + p.Id, 0)); }
                catch (Exception e) { Debug.LogWarning($"{LogPrefix} Saved request count of '{p.Id}' unavailable ({e.Message})."); }
            }
            requestsSinceShow[p.Id] = n;
            return n;
        }

        private void SetRequestsSinceShow(AdPlacement p, int requests)
        {
            requestsSinceShow[p.Id] = requests;
            if (!p.PersistRequestCount) return;
            try
            {
                // Saved now (an app swiped away gets no OnApplicationQuit); requests are player actions, not per frame.
                if (requests > 0) PlayerPrefs.SetInt(RequestCountKeyPrefix + p.Id, requests);
                else PlayerPrefs.DeleteKey(RequestCountKeyPrefix + p.Id);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Request count of '{p.Id}' not saved ({e.Message}).");
            }
        }

        private void NoteShown(AdPlacement p, AdShowResult result)
        {
            SetRequestsSinceShow(p, 0);
            foreach (AdShowRule rule in ruleSnapshot)
            {
                try { rule.OnShown(p, result); }
                catch (Exception e) { Debug.LogWarning($"{LogPrefix} Rule {rule.GetType().Name}.OnShown threw: {e.Message}"); }
            }
        }

        /// <inheritdoc />
        public ValueTask<AdShowResult> ShowAsync(string placementId, CancellationToken cancellationToken = default)
            => ShowAsync(placementId, -1f, cancellationToken);

        /// <summary>
        /// <see cref="ShowAsync(string, CancellationToken)"/> that waits up to <paramref name="waitForFillSeconds"/> for the
        /// ad to load when nothing else blocks the show, instead of <c>AdPlacement.WaitForFillSeconds</c> (negative = use it).
        /// </summary>
        public async ValueTask<AdShowResult> ShowAsync(string placementId, float waitForFillSeconds, CancellationToken cancellationToken = default)
        {
            RefreshNoAds();
            bool allowed = CanShow(placementId, out AdBlockReason reason);
            if (!allowed && reason == AdBlockReason.NotReady && !cancellationToken.IsCancellationRequested)
            {
                float wait = waitForFillSeconds >= 0f ? waitForFillSeconds : placements[placementId].WaitForFillSeconds;
                if (wait > 0f) (allowed, reason) = await WaitForFillAsync(placementId, wait, cancellationToken);
            }
            if (!allowed)
            {
                NoteRequest(placementId, reason);
                RaiseShowRequested(placementId, reason);
                // Its providers were dropped by a slow init and may still join: accepted, shown when one does.
                if (reason == AdBlockReason.NoProvider && droppedProviders.Count > 0
                    && placements.TryGetValue(placementId, out AdPlacement dropped) && dropped.IsBannerLike)
                {
                    wantedBanners.Add(placementId);
                    Debug.Log($"{LogPrefix} Banner '{placementId}': its providers are still initializing - shown once one is ready.");
                    return AdShowResult.Completed;
                }
                // Not the Nth request: the expected answer of most requests, not a problem.
                if (reason != AdBlockReason.RequestCount) Debug.LogWarning($"{LogPrefix} Placement '{placementId}' not shown: {reason}.");
                else if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Placement '{placementId}' not shown: {reason}.");
                return AdShowResult.Failed;
            }
            if (cancellationToken.IsCancellationRequested) return AdShowResult.Failed;

            AdPlacement p = placements[placementId];
            if (p.IsBannerLike)
            {
                // Desired visibility: remembered until HideBanner / DestroyBanner. The active route serving it gets the
                // request even before its banner loaded (the adapter shows it once loaded); with no active route yet
                // (SDK still initializing) it is replayed by Reconcile, so this is not a failure.
                wantedBanners.Add(placementId);
                RaiseShowRequested(placementId, AdBlockReason.None);
                string target = ServingBannerProvider(p);
                if (target == null)
                {
                    Debug.Log($"{LogPrefix} Banner '{placementId}': no route active yet - shown once its provider is ready.");
                    CheckBannerHeight(placementId);
                    return AdShowResult.Completed;
                }
                if (bannerProvider.TryGetValue(placementId, out string current) && current != target)
                    SafeHideBanner(byId[current], placementId);
                bannerProvider[placementId] = target;
                AdShowResult shown = await ShowBannerOnAsync(byId[target], placementId, cancellationToken);
                CheckBannerHeight(placementId); // already loaded: no ready change will follow
                return shown;
            }

            // Claimed synchronously (before the first await) so a second ShowAsync in the same frame sees it - also one
            // started by a rule's OnRequested below.
            SetShowingFullscreen(placementId);
            NoteRequest(placementId, AdBlockReason.None);
            RaiseShowRequested(placementId, AdBlockReason.None);
            bool displayed = false;
            try
            {
                foreach (string routeId in EffectiveRoutes(p))
                {
                    IAdsProviderAdapter adapter = byId[routeId];
                    if (!adapter.IsPlacementReady(placementId)) continue;
                    string providerId = routeId;
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
                        // An app open only restarts the interstitial interval when it follows the interstitial rules.
                        if (p.Format == AdFormat.Interstitial || (p.Format == AdFormat.AppOpen && policy.AppOpenFollowsInterstitialRules)
                            || (p.Format == AdFormat.Rewarded && result == AdShowResult.Completed && policy.RewardedResetsInterstitialTimer))
                        {
                            lastInterstitialAt = t;
                            SaveInterstitialTime();
                        }
                        if (p.Format == AdFormat.AppOpen) lastAppOpenAt = t;
                        NoteShown(p, result);
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

        // Waits while loading is the only thing missing. Ends early when the ad is ready, when another rule starts
        // blocking (another ad opened, no-ads bought) or when the caller cancels; the answer is CanShow's at that moment.
        private async Task<(bool, AdBlockReason)> WaitForFillAsync(string placementId, float seconds, CancellationToken cancellationToken)
        {
            if (AdsLog.Verbose) Debug.Log($"{LogPrefix} Placement '{placementId}' not loaded yet - waiting up to {seconds:0.#}s.");
            TaskCompletionSource<bool> signal = null;
            void OnReadyChanged(string id)
            {
                if (id == placementId) signal?.TrySetResult(true);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task delay = Task.Delay(TimeSpan.FromSeconds(seconds), timeout.Token);
            PlacementReadyChanged += OnReadyChanged;
            try
            {
                AdBlockReason reason;
                while (true)
                {
                    signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    if (CanShow(placementId, out reason)) return (true, reason);
                    if (reason != AdBlockReason.NotReady) return (false, reason);
                    if (await Task.WhenAny(signal.Task, delay) == delay) break;
                }
                RefreshNoAds();
                bool allowed = CanShow(placementId, out reason);
                if (!allowed && AdsLog.Verbose) Debug.Log($"{LogPrefix} Placement '{placementId}': no fill within {seconds:0.#}s.");
                return (allowed, reason);
            }
            finally
            {
                PlacementReadyChanged -= OnReadyChanged;
                timeout.Cancel();
            }
        }

        /// <inheritdoc />
        public float GetBannerHeightPixels(string placementId)
        {
            if (string.IsNullOrEmpty(placementId) || !placements.TryGetValue(placementId, out AdPlacement p) || !p.IsBannerLike)
                return 0f;
            foreach (string routeId in EffectiveRoutes(p))
            {
                IAdsProviderAdapter adapter = byId[routeId];
                if (adapter is not IBannerMetrics metrics || !adapter.IsPlacementReady(placementId)) continue;
                float height = metrics.GetBannerHeightPixels(placementId);
                if (height > 0f) return height;
            }
            return 0f;
        }

        /// <summary>
        /// Height in screen pixels the banner / MREC at <paramref name="placementId"/> takes on screen now: its loaded
        /// height while the game shows it (ShowAsync until HideBanner / DestroyBanner), else 0.
        /// </summary>
        public float GetVisibleBannerHeightPixels(string placementId)
        {
            if (placementId == null || !wantedBanners.Contains(placementId) || disabled.Contains(placementId)) return 0f;
            if (placements.TryGetValue(placementId, out AdPlacement p) && p.SuppressedByNoAds && IsNoAdsOwned()) return 0f;
            return GetBannerHeightPixels(placementId);
        }

        /// <inheritdoc />
        public float GetExpectedBannerHeightPixels(string placementId)
        {
            if (string.IsNullOrEmpty(placementId) || !placements.TryGetValue(placementId, out AdPlacement p) || !p.IsBannerLike)
                return 0f;
            if (disabled.Contains(placementId) || (p.SuppressedByNoAds && IsNoAdsOwned())) return 0f;
            float loaded = GetBannerHeightPixels(placementId);
            if (loaded > 0f) return loaded;
            foreach (string routeId in EffectiveRoutes(p))
            {
                IAdsProviderAdapter adapter = byId[routeId];
                if (adapter is not IBannerMetrics metrics || !activeRoutes.Contains((placementId, routeId))) continue;
                float height = metrics.GetExpectedBannerHeightPixels(placementId);
                if (height > 0f) return height;
            }
            return 0f;
        }

        /// <inheritdoc />
        public void HideBanner(string placementId)
        {
            if (string.IsNullOrEmpty(placementId)) return;
            wantedBanners.Remove(placementId); // also cancels a show still waiting for its provider
            bannerProvider.Remove(placementId);
            foreach (IAdsProviderAdapter adapter in adapters) adapter.HideBanner(placementId);
            CheckBannerHeight(placementId);
        }

        /// <inheritdoc />
        public void DestroyBanner(string placementId)
        {
            if (string.IsNullOrEmpty(placementId)) return;
            wantedBanners.Remove(placementId);
            bannerProvider.Remove(placementId);
            foreach (IAdsProviderAdapter adapter in adapters) adapter.DestroyBanner(placementId);
            CheckBannerHeight(placementId);
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

        private void RaiseShowRequested(string placementId, AdBlockReason reason)
        {
            try { ShowRequested?.Invoke(placementId, reason); }
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
            CheckBannerHeight(placementId);
        }

        private void CheckBannerHeight(string placementId)
        {
            if (string.IsNullOrEmpty(placementId) || !placements.TryGetValue(placementId, out AdPlacement p) || !p.IsBannerLike) return;
            float height = GetVisibleBannerHeightPixels(placementId);
            float before = visibleBannerHeight.TryGetValue(placementId, out float h) ? h : 0f;
            if (Mathf.Approximately(before, height)) return;
            visibleBannerHeight[placementId] = height;
            try { BannerHeightChanged?.Invoke(placementId, height); }
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
            if (SafeReady(adapter, placementId))
            {
                try { PlacementLoaded?.Invoke(placementId, AdProviderIds.Normalize(adapter.ProviderId)); }
                catch (Exception e) { Debug.LogException(e); }
            }
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
            if (disposed) return;
            try { PlacementLoadFailed?.Invoke(placementId, AdProviderIds.Normalize(adapter.ProviderId), error); }
            catch (Exception e) { Debug.LogException(e); }
            if (loadStrategy != AdLoadStrategy.Primary) return;
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
