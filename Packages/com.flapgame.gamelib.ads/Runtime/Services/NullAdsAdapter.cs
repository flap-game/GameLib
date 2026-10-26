using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Mock adapter (provider id <c>none</c>) for the Editor, development builds and builds without an ad SDK. Serves
    /// every placement routed to <c>none</c> (or every placement when <c>serveAllPlacements</c> is set): an activated
    /// placement is ready at once, Show waits <c>fakeDelaySeconds</c>, logs, and returns Completed.
    /// <para>
    /// Release builds (neither <c>UNITY_EDITOR</c> nor <c>DEVELOPMENT_BUILD</c>) never fake an ad: a fake
    /// Completed would pay every rewarded placement without an impression. There the adapter serves nothing,
    /// logs an error at initialization and every placement reports not ready / Failed.
    /// </para>
    /// </summary>
    public sealed class NullAdsAdapter : IAdsProviderAdapter
    {
        private const string LogPrefix = "[Ads/Null]";

        private readonly HashSet<string> served = new();
        private readonly HashSet<string> refused = new();
        private readonly HashSet<string> active = new();
        private readonly HashSet<string> loaded = new();
        private readonly HashSet<string> visibleBanners = new();
        private readonly Dictionary<string, AdFormat> formats = new();
        private readonly float fakeDelaySeconds;
        private readonly bool allowed;
        private bool initialized;

        /// <summary>True in the Editor and development builds: the only builds where fake ads may pay out.</summary>
        public static bool FakeAdsAllowedInThisBuild
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Factory constructor: placements come from the init context's units.</summary>
        [Preserve]
        public NullAdsAdapter() : this(null) { }

        /// <param name="settings">Placements routed to <c>none</c> are served (all of them with <paramref name="serveAllPlacements"/>). Null = only the context's units.</param>
        /// <param name="fakeDelaySeconds">How long a fake ad pretends to be on screen.</param>
        /// <param name="serveAllPlacements">True = serve every placement whatever its routes (no SDK installed).</param>
        /// <param name="allowFakeAds">Null = <see cref="FakeAdsAllowedInThisBuild"/>. Tests pass false to simulate a release build.</param>
        public NullAdsAdapter(AdsModule settings, float fakeDelaySeconds = 0.5f, bool serveAllPlacements = false, bool? allowFakeAds = null)
        {
            this.fakeDelaySeconds = Mathf.Max(0f, fakeDelaySeconds);
            ServesAllPlacements   = serveAllPlacements;
            allowed               = allowFakeAds ?? FakeAdsAllowedInThisBuild;
            if (settings == null || settings.Placements == null) return;
            foreach (AdPlacement p in settings.Placements)
            {
                if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                if (!serveAllPlacements && !HasNullRoute(p)) continue;
                Serve(p.Id, p.Format);
            }
        }

        /// <summary>True when this adapter stands in for every placement (no SDK installed).</summary>
        public bool ServesAllPlacements { get; }

        /// <summary>Placements this adapter fakes.</summary>
        public IReadOnlyCollection<string> ServedPlacements => served;

        /// <summary>Test knob: the next N loads fail with <see cref="PlacementLoadFailed"/> instead of becoming ready.</summary>
        public int FailNextLoads { get; set; }

        /// <summary>Test knob: served placements are ready once initialized, even when not activated.</summary>
        public bool AlwaysReady { get; set; }

        /// <summary>Test knob: the result the next fake shows return (Completed by default).</summary>
        public AdShowResult FakeResult { get; set; } = AdShowResult.Completed;

        /// <summary>Test knob: when set, every completed fake show raises <see cref="ImpressionPaid"/> with this value (USD).</summary>
        public double? FakeImpressionValue { get; set; }

        /// <summary>Last consent state received.</summary>
        public ConsentState LastConsent { get; private set; }

        /// <inheritdoc />
        public string ProviderId => AdProviderIds.None;

        /// <summary>Pre-0.6 enum id.</summary>
        public AdProviderType ProviderType => AdProviderType.None;

        /// <inheritdoc />
        public bool IsInitialized => initialized;

        /// <inheritdoc />
        public event Action<string> PlacementReadyChanged;

        /// <inheritdoc />
        public event Action<string, string> PlacementLoadFailed;

        /// <inheritdoc />
        public event Action<AdImpression> ImpressionPaid;

        /// <inheritdoc />
        public ValueTask InitializeAsync(AdsProviderContext context, CancellationToken cancellationToken = default)
        {
            if (initialized) return default;
            if (context != null)
            {
                foreach (AdUnitAssignment unit in context.Units)
                    if (!string.IsNullOrEmpty(unit.PlacementId)) Serve(unit.PlacementId, unit.Format);
                if (LastConsent == null) LastConsent = context.Consent.Clone();
            }
            initialized = true;
            if (refused.Count > 0)
                Debug.LogError($"{LogPrefix} Release build without an ad SDK for {refused.Count} placement(s) " +
                               $"({string.Join(", ", refused)}): no ads will show and rewarded placements never pay. " +
                               "Install AdMob/MAX/LevelPlay (GameLib > SDK Setup) or route these placements to a real provider.");
            Debug.Log($"{LogPrefix} Initialized; serving {served.Count} placement(s) with fake ads.");
            foreach (string id in new List<string>(active)) Load(id);
            if (AlwaysReady)
                foreach (string id in served) PlacementReadyChanged?.Invoke(id);
            return default;
        }

        /// <inheritdoc />
        public void SetPlacementActive(string placementId, bool isActive)
        {
            if (string.IsNullOrEmpty(placementId)) return;
            if (isActive)
            {
                if (!active.Add(placementId)) return;
                if (initialized) Load(placementId);
                return;
            }
            if (!active.Remove(placementId)) return;
            visibleBanners.Remove(placementId);
            if (loaded.Remove(placementId)) PlacementReadyChanged?.Invoke(placementId);
        }

        /// <inheritdoc />
        public bool IsPlacementReady(string placementId)
        {
            if (!initialized || placementId == null || !served.Contains(placementId)) return false;
            return AlwaysReady || loaded.Contains(placementId);
        }

        /// <summary>Adapter-level check, kept for diagnostics and tests.</summary>
        public bool CanShow(string placementId, out AdBlockReason reason)
        {
            if (placementId != null && refused.Contains(placementId)) { reason = AdBlockReason.NotReady; return false; }
            if (placementId == null || !served.Contains(placementId)) { reason = AdBlockReason.UnknownPlacement; return false; }
            reason = initialized ? AdBlockReason.None : AdBlockReason.NotReady;
            return initialized;
        }

        /// <inheritdoc />
        public async ValueTask<AdShowResult> ShowAsync(string placementId, CancellationToken cancellationToken = default)
        {
            bool banner = placementId != null && formats.TryGetValue(placementId, out AdFormat f) && (f == AdFormat.Banner || f == AdFormat.Mrec);
            if (!IsPlacementReady(placementId) && !(banner && initialized && served.Contains(placementId)))
            {
                Debug.LogWarning($"{LogPrefix} '{placementId}' not shown: not served by the Null adapter.");
                return AdShowResult.Failed;
            }
            Debug.Log($"{LogPrefix} Showing fake ad '{placementId}' ({fakeDelaySeconds:0.##}s).");
            if (fakeDelaySeconds > 0f && !banner)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(fakeDelaySeconds), cancellationToken); }
                catch (OperationCanceledException) { return AdShowResult.Failed; }
            }
            if (banner)
            {
                visibleBanners.Add(placementId);
                RaiseImpression(placementId);
                return AdShowResult.Completed;
            }
            AdShowResult result = FakeResult;
            Debug.Log($"{LogPrefix} Fake ad '{placementId}' {result}.");
            if (result == AdShowResult.Completed) RaiseImpression(placementId);
            return result;
        }

        /// <inheritdoc />
        public void HideBanner(string placementId)
        {
            if (placementId != null && visibleBanners.Remove(placementId)) Debug.Log($"{LogPrefix} Hide banner '{placementId}'.");
        }

        /// <inheritdoc />
        public void DestroyBanner(string placementId)
        {
            if (placementId == null || !visibleBanners.Remove(placementId)) return;
            Debug.Log($"{LogPrefix} Destroy banner '{placementId}'.");
        }

        /// <inheritdoc />
        public void ApplyConsent(ConsentState consent) => LastConsent = consent?.Clone();

        /// <inheritdoc />
        public void Dispose()
        {
            active.Clear();
            loaded.Clear();
            visibleBanners.Clear();
        }

        private void Serve(string placementId, AdFormat format)
        {
            formats[placementId] = format;
            if (allowed) served.Add(placementId); else refused.Add(placementId);
        }

        private void Load(string placementId)
        {
            if (!served.Contains(placementId) || loaded.Contains(placementId)) return;
            if (FailNextLoads > 0)
            {
                FailNextLoads--;
                PlacementLoadFailed?.Invoke(placementId, "Simulated no fill");
                return;
            }
            loaded.Add(placementId);
            PlacementReadyChanged?.Invoke(placementId);
        }

        private void RaiseImpression(string placementId)
        {
            if (!FakeImpressionValue.HasValue) return;
            AdFormat format = formats.TryGetValue(placementId, out AdFormat f) ? f : AdFormat.Interstitial;
            ImpressionPaid?.Invoke(new AdImpression(placementId, AdProviderIds.None, format, null, "null",
                                                    FakeImpressionValue.Value, "USD", "estimated"));
        }

        private static bool HasNullRoute(AdPlacement p)
        {
            if (p.Routes == null) return false;
            foreach (AdRoute r in p.Routes)
                if (r != null && r.EffectiveProviderId == AdProviderIds.None) return true;
            return false;
        }
    }

    /// <summary>
    /// Factory of the mock provider <c>none</c>. No consent platform. Registered before any scene loads; the installer
    /// uses it only when no other provider is selected (or a route explicitly says <c>none</c>).
    /// </summary>
    [Preserve]
    public sealed class NullAdsProviderFactory : IAdsProviderFactory
    {
        /// <inheritdoc />
        public string ProviderId => AdProviderIds.None;

        /// <inheritdoc />
        public string DisplayName => "None (mock ads)";

        /// <inheritdoc />
        public int ConsentPriority => 0;

        /// <inheritdoc />
        public IAdsProviderAdapter CreateAdapter() => new NullAdsAdapter();

        /// <inheritdoc />
        public IConsentService CreateConsentService(AdsModule settings) => null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register() => AdsProviderCatalog.Register(new NullAdsProviderFactory());
    }
}
