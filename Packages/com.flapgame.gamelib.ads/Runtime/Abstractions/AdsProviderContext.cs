using System.Collections.Generic;

namespace FlapGame.GameLib.Ads
{
    /// <summary>A placement routed to one provider, with the unit id for the platform this build targets.</summary>
    public readonly struct AdUnitAssignment
    {
        public readonly AdPlacement Placement;
        public readonly string AdUnitId;

        public AdUnitAssignment(AdPlacement placement, string adUnitId)
        {
            Placement = placement;
            AdUnitId  = adUnitId;
        }

        public string PlacementId => Placement?.Id;
        public AdFormat Format => Placement?.Format ?? AdFormat.Interstitial;
    }

    /// <summary>Everything an adapter needs at <see cref="IAdsProviderAdapter.InitializeAsync"/>.</summary>
    public sealed class AdsProviderContext
    {
        public AdsProviderContext(AdsModule settings, AdsProviderConfig config, IReadOnlyList<AdUnitAssignment> units,
                                  ConsentState consent)
        {
            Settings = settings;
            Config   = config;
            Units    = units ?? new List<AdUnitAssignment>();
            Consent  = consent ?? new ConsentState();
        }

        /// <summary>The whole settings asset (test mode, test device ids, consent debug...).</summary>
        public AdsModule Settings { get; }

        /// <summary>This provider's row in <see cref="AdsModule.Providers"/>; null when not listed.</summary>
        public AdsProviderConfig Config { get; }

        /// <summary>Placements routed to this provider (one entry per placement), unit id already resolved per platform.</summary>
        public IReadOnlyList<AdUnitAssignment> Units { get; }

        /// <summary>Privacy state at initialization. Later changes arrive via <see cref="IAdsProviderAdapter.ApplyConsent"/>.</summary>
        public ConsentState Consent { get; }

        public bool TestMode => Settings != null && Settings.TestMode;

        /// <summary>The configured app key for the current platform (LevelPlay), or null.</summary>
        public string AppKey => Config?.CurrentAppKey;
    }
}
