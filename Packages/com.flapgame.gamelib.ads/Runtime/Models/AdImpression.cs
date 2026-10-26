namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// One paid impression reported by a provider (impression-level revenue). The installer forwards it to analytics
    /// as the standard <c>ad_impression</c> event.
    /// </summary>
    public readonly struct AdImpression
    {
        /// <summary>GameLib placement id.</summary>
        public readonly string PlacementId;
        /// <summary>Provider id that served it (<see cref="AdProviderIds"/>).</summary>
        public readonly string ProviderId;
        public readonly AdFormat Format;
        /// <summary>SDK ad unit id.</summary>
        public readonly string AdUnitId;
        /// <summary>Network that actually filled the ad (e.g. "AppLovin", "Meta"), when the SDK reports it.</summary>
        public readonly string AdSource;
        /// <summary>Revenue in <see cref="Currency"/> (not micros).</summary>
        public readonly double Value;
        /// <summary>ISO 4217, usually "USD".</summary>
        public readonly string Currency;
        /// <summary>SDK precision label ("exact", "estimated", "publisher_defined", ...), when reported.</summary>
        public readonly string Precision;

        public AdImpression(string placementId, string providerId, AdFormat format, string adUnitId,
                            string adSource, double value, string currency, string precision = null)
        {
            PlacementId = placementId;
            ProviderId  = providerId;
            Format      = format;
            AdUnitId    = adUnitId;
            AdSource    = adSource;
            Value       = value;
            Currency    = string.IsNullOrEmpty(currency) ? "USD" : currency;
            Precision   = precision;
        }
    }
}
