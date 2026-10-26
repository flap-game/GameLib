using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Att
{
    /// <summary>When the App Tracking Transparency prompt shows.</summary>
    public enum AttRequestTiming
    {
        /// <summary>At launch: after the consent form answer (if a CMP is installed), then <see cref="AttModule.DelaySeconds"/>.</summary>
        AfterConsent = 0,
        /// <summary>Only when the game calls <see cref="IAttService.RequestAsync"/> (e.g. after the tutorial).</summary>
        Manual = 1,
    }

    /// <summary>
    /// iOS App Tracking Transparency. Registers <see cref="IAttService"/> and, with <see cref="AttRequestTiming.AfterConsent"/>,
    /// shows the prompt by itself after the consent form. Apple's order: consent form first, ATT after. The prompt
    /// shows once per install; once answered (here or by another SDK, e.g. MAX's consent flow) it is never asked again.
    /// Android and the Editor: nothing to show, the status is <see cref="AttStatus.NotApplicable"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "GameLib/Modules/App Tracking Transparency", fileName = "AttModule")]
    public sealed class AttModule : GameLibModule
    {
        public const string LogTag = "[ATT]";
        public const string DefaultUsageDescription = "Your data will be used to deliver personalized ads to you.";

        [Tooltip("AfterConsent: GameLib shows the prompt at launch, after the consent form (and the delay below). " +
                 "Manual: only when the game calls IAttService.RequestAsync().")]
        public AttRequestTiming Timing = AttRequestTiming.AfterConsent;

        [Tooltip("AfterConsent: extra seconds between the consent answer (or the launch, without a consent form) and the prompt.")]
        [Min(0f)] public float DelaySeconds;

        [Tooltip("AfterConsent: longest wait for the consent form answer, in seconds.")]
        [Min(0f)] public float ConsentWaitSeconds = 60f;

        [Tooltip("Info.plist NSUserTrackingUsageDescription: the sentence the prompt shows. iOS kills an app that asks without it.")]
        [TextArea(2, 4)] public string UsageDescription = DefaultUsageDescription;

        // Before Analytics: AppsFlyer reads IAttService when it installs.
        public override int Order => GameLibModuleOrder.Consent;

        public override ValueTask InstallAsync(IServiceRegistry services, CancellationToken cancellationToken = default)
        {
            services.TryResolve(out IConsentState consent);
            services.TryResolve(out IAppLifecycle lifecycle);
            var service = new AttService(new AttPrompt(), consent, lifecycle);
            services.Register<IAttService>(service);
            service.Publish();

            if (Timing == AttRequestTiming.AfterConsent && service.Status == AttStatus.NotDetermined)
                _ = RequestAsync(service, () => services.IsRegistered<IConsentService>(), cancellationToken);
            return default;
        }

        public override void Validate(ICollection<GameLibIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(UsageDescription))
                issues.Add(GameLibIssue.Error("ATT: Usage Description is empty - iOS kills an app that shows the prompt without it."));
        }

        private async Task RequestAsync(AttService service, Func<bool> hasCmp, CancellationToken cancellationToken)
        {
            try
            {
                await service.RequestAfterConsentAsync(WaitForBootstrapAsync, hasCmp, TimeSpan.FromSeconds(ConsentWaitSeconds),
                                                       TimeSpan.FromSeconds(DelaySeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static async Task WaitForBootstrapAsync()
        {
            try
            {
                await GameLibBootstrap.WhenReadyAsync(TimeSpan.FromSeconds(30));
            }
            catch (Exception)
            {
                // A failed or stuck bootstrap must not keep the prompt (and AppsFlyer's start) waiting.
            }
        }
    }
}
