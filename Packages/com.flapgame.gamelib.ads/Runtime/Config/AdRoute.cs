using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace FlapGame.GameLib.Ads
{
    /// <summary>One way to fill a placement: a provider (SDK) plus the ad unit ids it should load on each platform.</summary>
    [Serializable, Preserve]
    public sealed class AdRoute
    {
        [Tooltip("Provider id (AdProviderIds: admob, max, levelplay, none) - or the id of a provider assembly you added.")]
        public string ProviderId;

        [Tooltip("Ad unit id on Android. Empty = use 'Ad Unit Id (both platforms)'.")]
        public string AndroidAdUnitId;

        [Tooltip("Ad unit id on iOS. Empty = use 'Ad Unit Id (both platforms)'.")]
        public string IosAdUnitId;

        [Tooltip("Fallback ad unit id used on any platform whose own field is empty (pre-0.6 single id).")]
        public string AdUnitId;

        [Tooltip("Pre-0.6 provider enum. Read only when Provider Id is empty.")]
        [HideInInspector]
        public AdProviderType Provider;

        [Preserve]
        public AdRoute() { }

        public AdRoute(string providerId, string androidAdUnitId = null, string iosAdUnitId = null)
        {
            ProviderId      = providerId;
            AndroidAdUnitId = androidAdUnitId;
            IosAdUnitId     = iosAdUnitId;
            Provider        = AdProviderIds.ToLegacy(providerId);
        }

        /// <summary>Pre-0.6 constructor: one unit id for every platform.</summary>
        public AdRoute(AdProviderType provider, string adUnitId = null)
        {
            Provider = provider;
            AdUnitId = adUnitId;
        }

        /// <summary>The provider id this route uses: <see cref="ProviderId"/>, else the legacy enum mapped to an id.</summary>
        public string EffectiveProviderId =>
            string.IsNullOrWhiteSpace(ProviderId) ? AdProviderIds.FromLegacy(Provider) : AdProviderIds.Normalize(ProviderId);

        /// <summary>Unit id for the platform this build targets (the active build target in the Editor). May be null.</summary>
        public string CurrentAdUnitId
        {
            get
            {
#if UNITY_IOS
                return !string.IsNullOrEmpty(IosAdUnitId) ? IosAdUnitId : AdUnitId;
#else
                return !string.IsNullOrEmpty(AndroidAdUnitId) ? AndroidAdUnitId : AdUnitId;
#endif
            }
        }

        public AdRoute Clone() => (AdRoute)MemberwiseClone();
    }
}
