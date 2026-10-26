using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Per-provider switches and keys. Providers that read their key from their own settings asset (AdMob app id in
    /// <c>GoogleMobileAdsSettings</c>, MAX SDK key in <c>AppLovinSettings</c>) leave the key fields empty;
    /// LevelPlay reads its app key from here.
    /// </summary>
    [Serializable, Preserve]
    public sealed class AdsProviderConfig
    {
        [Tooltip("Provider id (admob, max, levelplay, ...).")]
        public string ProviderId;

        [Tooltip("Off = never initialized, whatever the routes or the remote plan say.")]
        public bool Enabled = true;

        [Tooltip("App key on Android (LevelPlay app key, ...). Empty when the SDK reads its own settings asset.")]
        public string AndroidAppKey;

        [Tooltip("App key on iOS.")]
        public string IosAppKey;

        public string CurrentAppKey
        {
            get
            {
#if UNITY_IOS
                return IosAppKey;
#else
                return AndroidAppKey;
#endif
            }
        }
    }
}
