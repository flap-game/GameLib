using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Consent service for MAX-only setups. First-time consent is handled by AppLovin inside
    /// <c>MaxSdk.InitializeSdk()</c> when "Google UMP" is enabled in the AppLovin dashboard, so
    /// <see cref="RequestAsync"/> is a no-op before init; <see cref="ShowPrivacyOptionsAsync"/> re-opens
    /// the CMP for an existing user (Settings screen "Privacy options" button).
    /// <para>
    /// <see cref="Status"/> and <see cref="IsPrivacyOptionsRequired"/> come from the geography MAX determined at
    /// init (<c>SdkConfiguration.ConsentFlowUserGeography</c>): GDPR users get Obtained (the CMP flow ran during init;
    /// the user's actual choice lives in the TCF string) and the privacy-options entry point; everyone else gets
    /// NotRequired; before init / unknown geography stays Unknown.
    /// </para>
    /// When both AdMob and MAX are present, AdMobConsentService runs UMP; UMP writes the IAB TCF string that MAX
    /// reads on its own, so nothing is forwarded to MAX (see <see cref="SyncFromUmp"/>). MAX's own flow is enabled
    /// at build time in the AppLovin Integration Manager (no runtime switch in MAX 8.6.6): disable it in that setup,
    /// see <see cref="MaxAdsProviderFactory"/>.
    /// </summary>
    [Preserve]
    public sealed class MaxConsentService : IConsentService
    {
        private bool cmpFailed;

        public ConsentStatus Status
        {
            get
            {
#if GAMELIB_MAX
                if (cmpFailed) return ConsentStatus.Unknown;
                switch (Geography())
                {
                    case MaxSdkBase.ConsentFlowUserGeography.Gdpr:  return ConsentStatus.Obtained;
                    case MaxSdkBase.ConsentFlowUserGeography.Other: return ConsentStatus.NotRequired;
                    default:                                        return ConsentStatus.Unknown;
                }
#else
                return ConsentStatus.NotRequired;
#endif
            }
        }

        /// <summary>Always true: MAX gates personalised ads itself from the TCF string / its own consent flow.</summary>
        public bool CanRequestAds => true;

        public bool IsPrivacyOptionsRequired
        {
#if GAMELIB_MAX
            get => Geography() == MaxSdkBase.ConsentFlowUserGeography.Gdpr && MaxSdk.CmpService.HasSupportedCmp;
#else
            get => false;
#endif
        }

        /// <summary>
        /// Kept for source compatibility; does nothing. It used to call <c>MaxSdk.SetHasUserConsent(canRequestAds)</c>,
        /// which told MAX "consent granted" even when the user declined in UMP (CanRequestAds is true after a refusal).
        /// MAX reads the TCF string UMP writes, so forwarding is neither needed nor correct.
        /// </summary>
        [Obsolete("MAX reads the IAB TCF string written by UMP; forcing SetHasUserConsent overrode the user's choice. This method is now a no-op.")]
        public static void SyncFromUmp(bool canRequestAds) { }

        public ValueTask RequestAsync(CancellationToken cancellationToken = default)
        {
            // Initial consent is driven by MaxSdk.InitializeSdk(); nothing to do here.
            return default;
        }

        public async ValueTask ShowPrivacyOptionsAsync(CancellationToken cancellationToken = default)
        {
#if GAMELIB_MAX
            if (!MaxSdk.IsInitialized())
            {
                Debug.LogWarning("[Consent/MAX] ShowPrivacyOptionsAsync called before SDK is initialized - skipping.");
                return;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            MaxSdk.CmpService.ShowCmpForExistingUser(cmpError =>
            {
                if (cmpError != null)
                    Debug.LogWarning($"[Consent/MAX] CMP error ({cmpError.Code}): {cmpError.Message}");
                cmpFailed = cmpError != null;
                tcs.TrySetResult(true);
            });

            using (cancellationToken.Register(() => tcs.TrySetCanceled()))
                await tcs.Task;
#else
            await System.Threading.Tasks.Task.CompletedTask;
#endif
        }

#if GAMELIB_MAX
        // Cached once MAX reported a successful init: the configuration does not change during a session.
        private static MaxSdkBase.SdkConfiguration cachedConfiguration;

        private static MaxSdkBase.ConsentFlowUserGeography Geography()
        {
            if (cachedConfiguration == null)
            {
                if (!MaxSdk.IsInitialized()) return MaxSdkBase.ConsentFlowUserGeography.Unknown;
                MaxSdkBase.SdkConfiguration configuration = MaxSdk.GetSdkConfiguration();
                if (configuration == null || !configuration.IsSuccessfullyInitialized) return MaxSdkBase.ConsentFlowUserGeography.Unknown;
                cachedConfiguration = configuration;
            }
            return cachedConfiguration.ConsentFlowUserGeography;
        }
#endif
    }
}
