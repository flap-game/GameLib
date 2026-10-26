using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#if GAMELIB_ADMOB
using UmpConsentStatus         = GoogleMobileAds.Ump.Api.ConsentStatus;
using UmpConsentInfo           = GoogleMobileAds.Ump.Api.ConsentInformation;
using UmpConsentForm           = GoogleMobileAds.Ump.Api.ConsentForm;
using UmpConsentRequestParams  = GoogleMobileAds.Ump.Api.ConsentRequestParameters;
using UmpConsentDebugSettings  = GoogleMobileAds.Ump.Api.ConsentDebugSettings;
using UmpDebugGeography        = GoogleMobileAds.Ump.Api.DebugGeography;
using UmpPrivacyOptionsStatus  = GoogleMobileAds.Ump.Api.PrivacyOptionsRequirementStatus;
#endif

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Google UMP consent. <c>AdsInstaller</c> runs <see cref="RequestAsync"/> before any SDK initializes and only
    /// initializes / loads ads once <see cref="CanRequestAds"/> is true. UMP callbacks are awaited from the caller's
    /// context (the Unity main thread during bootstrap), so continuations never run on a native callback thread.
    /// </summary>
    public sealed class AdMobConsentService : IConsentService
    {
        private readonly AdsModule settings;

        public ConsentStatus Status { get; private set; } = ConsentStatus.Unknown;

        /// <summary>True while the consent / privacy form is on screen. The installer never times out while it is.</summary>
        public bool IsFormShowing { get; private set; }

        public bool CanRequestAds
        {
#if GAMELIB_ADMOB
            get => UmpConsentInfo.CanRequestAds();
#else
            get => true;
#endif
        }

        public bool IsPrivacyOptionsRequired
        {
#if GAMELIB_ADMOB
            get => UmpConsentInfo.PrivacyOptionsRequirementStatus == UmpPrivacyOptionsStatus.Required;
#else
            get => false;
#endif
        }

        /// <summary>Creates the UMP consent service. <paramref name="settings"/> supplies the consent debug options.</summary>
        public AdMobConsentService(AdsModule settings)
        {
            this.settings = settings;
        }

        /// <summary>
        /// Snapshot of the privacy state for every ad provider. UMP's status cannot tell consent from refusal (both end
        /// Obtained with CanRequestAds true), so consent is read from the IAB TCF purposes UMP wrote
        /// (<see cref="IabPrivacy.Map(ConsentStatus, bool, IIabPrivacyStore)"/>).
        /// </summary>
        public ConsentState BuildConsentState() => IabPrivacy.Map(Status, CanRequestAds);

        public async ValueTask RequestAsync(CancellationToken cancellationToken = default)
        {
#if GAMELIB_ADMOB
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var parameters = new UmpConsentRequestParams();
            var debug = settings != null ? settings.ConsentDebug : null;
            if (debug != null && debug.Enabled)
            {
                parameters.ConsentDebugSettings = new UmpConsentDebugSettings
                {
                    DebugGeography      = (UmpDebugGeography)(int)debug.Geography,
                    TestDeviceHashedIds = debug.TestDeviceIds,
                };
            }

            UmpConsentInfo.Update(parameters, updateError =>
            {
                if (updateError != null)
                {
                    Debug.LogWarning($"[Consent] Update error ({updateError.ErrorCode}): {updateError.Message}");
                    Status = ConsentStatus.Unknown;
                    tcs.TrySetResult(false);
                    return;
                }

                Status = MapStatus(UmpConsentInfo.ConsentStatus);

                // Show form only when explicitly required - not for Obtained/NotRequired
                // (IsConsentFormAvailable also returns true for the Privacy Options button case)
                if (UmpConsentInfo.IsConsentFormAvailable()
                    && UmpConsentInfo.ConsentStatus == UmpConsentStatus.Required)
                {
                    UmpConsentForm.Load((form, loadError) =>
                    {
                        if (loadError != null)
                        {
                            Debug.LogWarning($"[Consent] Load error ({loadError.ErrorCode}): {loadError.Message}");
                            tcs.TrySetResult(true);
                            return;
                        }

                        IsFormShowing = true;
                        form.Show(showError =>
                        {
                            IsFormShowing = false;
                            if (showError != null)
                                Debug.LogWarning($"[Consent] Show error ({showError.ErrorCode}): {showError.Message}");

                            Status = MapStatus(UmpConsentInfo.ConsentStatus);
                            tcs.TrySetResult(true);
                        });
                    });
                }
                else
                {
                    tcs.TrySetResult(true);
                }
            });

            using (cancellationToken.Register(() => tcs.TrySetCanceled()))
                await tcs.Task;
#else
            Status = ConsentStatus.NotRequired;
            await System.Threading.Tasks.Task.CompletedTask;
#endif
        }

        public async ValueTask ShowPrivacyOptionsAsync(CancellationToken cancellationToken = default)
        {
#if GAMELIB_ADMOB
            if (!IsPrivacyOptionsRequired)
            {
                Debug.Log("[Consent] Privacy options not required for this user - nothing to show.");
                return;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            IsFormShowing = true;
            UmpConsentForm.ShowPrivacyOptionsForm(showError =>
            {
                IsFormShowing = false;
                if (showError != null)
                    Debug.LogWarning($"[Consent] Privacy options error ({showError.ErrorCode}): {showError.Message}");
                Status = MapStatus(UmpConsentInfo.ConsentStatus);
                tcs.TrySetResult(true);
            });

            using (cancellationToken.Register(() => tcs.TrySetCanceled()))
                await tcs.Task;
#else
            await System.Threading.Tasks.Task.CompletedTask;
#endif
        }

#if GAMELIB_ADMOB
        private static ConsentStatus MapStatus(UmpConsentStatus umpStatus) => umpStatus switch
        {
            UmpConsentStatus.NotRequired => ConsentStatus.NotRequired,
            UmpConsentStatus.Required    => ConsentStatus.Required,
            UmpConsentStatus.Obtained    => ConsentStatus.Obtained,
            _                            => ConsentStatus.Unknown,
        };
#endif
    }
}
