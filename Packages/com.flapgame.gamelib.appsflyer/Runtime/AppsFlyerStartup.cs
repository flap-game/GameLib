using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.AppsFlyer
{
    /// <summary>Where AppsFlyer's DMA consent comes from.</summary>
    public enum AppsFlyerConsentSource
    {
        /// <summary>From the shared <see cref="IConsentState"/> (the CMP answer the Ads module publishes).</summary>
        ConsentState = 0,
        /// <summary>The SDK reads the TCF v2.2 strings a TCF-certified CMP stored on the device.</summary>
        Tcf = 1,
    }

    /// <summary>
    /// The start order AppsFlyer needs: SDK created → CMP answered (or no CMP) → consent sent → App Tracking Transparency
    /// answered (iOS; the ATT module or another SDK shows the prompt) → customer user id → Purchase Connector → start.
    /// Every wait has a timeout, so the launch is always reported.
    /// </summary>
    public sealed class AppsFlyerStartup
    {
        public sealed class Options
        {
            public string DevKey;
            public string AppId;
            public bool Debug;
            public bool WaitForConsent = true;
            public TimeSpan ConsentWait = TimeSpan.FromSeconds(60);
            public AppsFlyerConsentSource ConsentSource;
            public bool WaitForAtt = true;
            public TimeSpan AttWait = TimeSpan.FromSeconds(60);
            public AppsFlyerPurchaseMode PurchaseMode;
            public bool PurchaseConnectorSandbox;
            public bool StoreKit2;
        }

        private static readonly TimeSpan AttPollInterval = TimeSpan.FromMilliseconds(500);

        private readonly IAppsFlyerBackend backend;
        private readonly IConsentState consent;
        private readonly IAttService att;
        private readonly Options options;
        private readonly Func<Task> bootstrapReady;
        private readonly Func<bool> hasCmp;
        private readonly Func<string> pendingUserId;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;

        /// <param name="bootstrapReady">Completes once every module installed (the Ads module registers the CMP then).</param>
        /// <param name="hasCmp">True when a CMP (<see cref="IConsentService"/>) is registered, so an answer will come.</param>
        /// <param name="att">The ATT module's service; null when it is not installed (no wait for the ATT answer).</param>
        /// <param name="pendingUserId">The user id the game set so far (sent before the start, so the install carries it), or null.</param>
        public AppsFlyerStartup(IAppsFlyerBackend backend, IConsentState consent, IAttService att, Options options,
                                Func<Task> bootstrapReady, Func<bool> hasCmp, Func<string> pendingUserId = null,
                                Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.consent = consent;
            this.att = att;
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.bootstrapReady = bootstrapReady;
            this.hasCmp = hasCmp;
            this.pendingUserId = pendingUserId;
            this.delay = delay ?? Task.Delay;
        }

        /// <summary>Upper bound of <see cref="RunAsync"/> before the SDK itself answers.</summary>
        public TimeSpan MaxWait => (options.WaitForConsent ? options.ConsentWait : TimeSpan.Zero) +
                                   (options.WaitForAtt ? options.AttWait : TimeSpan.Zero);

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            await backend.InitializeAsync(options.DevKey, string.IsNullOrEmpty(options.AppId) ? null : options.AppId, options.Debug);

            if (options.WaitForConsent && consent != null)
            {
                if (bootstrapReady != null) await bootstrapReady();
                if (!consent.Current.IsResolved && (hasCmp == null || hasCmp()) &&
                    !await consent.WaitUntilResolvedAsync(options.ConsentWait, cancellationToken, delay))
                    Debug.LogWarning($"[AppsFlyer] No consent answer after {options.ConsentWait.TotalSeconds:0}s - starting without it.");
            }

            if (options.ConsentSource == AppsFlyerConsentSource.Tcf)
                backend.EnableTcfDataCollection(true);
            else if (consent != null)
            {
                AppsFlyerConsentData? data = AppsFlyerConsentData.From(consent.Current);
                if (data.HasValue) backend.SetConsent(data.Value);
            }

            AttStatus status = await ResolveAttAsync(cancellationToken);
            if (status != AttStatus.NotApplicable) consent?.SetAtt(status);

            string userId = pendingUserId?.Invoke();
            if (!string.IsNullOrEmpty(userId)) backend.SetCustomerUserId(userId);

            if (options.PurchaseMode == AppsFlyerPurchaseMode.PurchaseConnector)
                backend.StartPurchaseConnector(options.PurchaseConnectorSandbox, options.StoreKit2);

            cancellationToken.ThrowIfCancellationRequested();
            await backend.StartAsync();
        }

        private async Task<AttStatus> ResolveAttAsync(CancellationToken cancellationToken)
        {
            if (att == null) return AttStatus.NotApplicable;
            AttStatus status = att.Status;
            if (status != AttStatus.NotDetermined || !options.WaitForAtt) return status;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<AttStatus> answer = PollAsync(cts.Token);
            Task timeout = delay(options.AttWait, cts.Token);
            Task finished = await Task.WhenAny(answer, timeout);
            cts.Cancel();
            if (finished == answer && answer.Status == TaskStatus.RanToCompletion) return answer.Result;

            cancellationToken.ThrowIfCancellationRequested();
            Debug.LogWarning($"[AppsFlyer] App Tracking Transparency not answered after {options.AttWait.TotalSeconds:0}s - starting without IDFA.");
            return att.Status;
        }

        private async Task<AttStatus> PollAsync(CancellationToken cancellationToken)
        {
            AttStatus status;
            while ((status = att.Status) == AttStatus.NotDetermined)
                await delay(AttPollInterval, cancellationToken);
            return status;
        }
    }
}
