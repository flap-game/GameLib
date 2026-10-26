using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.AppsFlyer
{
    /// <summary>Who shows the iOS App Tracking Transparency prompt before AppsFlyer starts.</summary>
    public enum AttMode
    {
        /// <summary>GameLib shows the prompt (after the CMP answer), then starts AppsFlyer.</summary>
        Request = 0,
        /// <summary>Another SDK shows it (e.g. MAX's consent flow); AppsFlyer waits until it was answered.</summary>
        WaitForOther = 1,
        /// <summary>Start without waiting: the first session carries no IDFA even if the user allows tracking later.</summary>
        DoNotWait = 2,
    }

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
    /// answered (iOS) → Purchase Connector → start. Every wait has a timeout, so the launch is always reported.
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
            public AttMode Att;
            public TimeSpan AttWait = TimeSpan.FromSeconds(60);
            public AppsFlyerPurchaseMode PurchaseMode;
            public bool PurchaseConnectorSandbox;
            public bool StoreKit2;
        }

        private static readonly TimeSpan AttPollInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan AttRetryDelay = TimeSpan.FromSeconds(1);
        private const int AttRequestAttempts = 3;

        private readonly IAppsFlyerBackend backend;
        private readonly IConsentState consent;
        private readonly IAttPrompt att;
        private readonly Options options;
        private readonly Func<Task> bootstrapReady;
        private readonly Func<bool> hasCmp;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;

        /// <param name="bootstrapReady">Completes once every module installed (the Ads module registers the CMP then).</param>
        /// <param name="hasCmp">True when a CMP (<see cref="IConsentService"/>) is registered, so an answer will come.</param>
        public AppsFlyerStartup(IAppsFlyerBackend backend, IConsentState consent, IAttPrompt att, Options options,
                                Func<Task> bootstrapReady, Func<bool> hasCmp, Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.consent = consent;
            this.att = att;
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.bootstrapReady = bootstrapReady;
            this.hasCmp = hasCmp;
            this.delay = delay ?? Task.Delay;
        }

        /// <summary>Upper bound of <see cref="RunAsync"/> before the SDK itself answers.</summary>
        public TimeSpan MaxWait => (options.WaitForConsent ? options.ConsentWait : TimeSpan.Zero) +
                                   (options.Att == AttMode.DoNotWait ? TimeSpan.Zero : options.AttWait);

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            await backend.InitializeAsync(options.DevKey, string.IsNullOrEmpty(options.AppId) ? null : options.AppId, options.Debug);

            if (options.WaitForConsent && consent != null)
            {
                if (bootstrapReady != null) await bootstrapReady();
                if (!consent.Current.IsResolved && (hasCmp == null || hasCmp()))
                    await WaitForConsentAsync(cancellationToken);
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

            if (options.PurchaseMode == AppsFlyerPurchaseMode.PurchaseConnector)
                backend.StartPurchaseConnector(options.PurchaseConnectorSandbox, options.StoreKit2);

            cancellationToken.ThrowIfCancellationRequested();
            await backend.StartAsync();
        }

        private async Task WaitForConsentAsync(CancellationToken cancellationToken)
        {
            var answered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged(ConsentState state)
            {
                if (state.IsResolved) answered.TrySetResult(true);
            }

            consent.Changed += OnChanged;
            try
            {
                if (consent.Current.IsResolved) return;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task timeout = delay(options.ConsentWait, cts.Token);
                if (await Task.WhenAny(answered.Task, timeout) != answered.Task)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Debug.LogWarning($"[AppsFlyer] No consent answer after {options.ConsentWait.TotalSeconds:0}s - starting without it.");
                }
                cts.Cancel();
            }
            finally
            {
                consent.Changed -= OnChanged;
            }
        }

        private async Task<AttStatus> ResolveAttAsync(CancellationToken cancellationToken)
        {
            if (att == null || options.Att == AttMode.DoNotWait) return att?.Status ?? AttStatus.NotApplicable;
            AttStatus status = att.Status;
            if (status != AttStatus.NotDetermined) return status;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<AttStatus> answer = options.Att == AttMode.Request ? RequestAsync(cts.Token) : PollAsync(cts.Token);
            Task timeout = delay(options.AttWait, cts.Token);
            Task finished = await Task.WhenAny(answer, timeout);
            cts.Cancel();
            if (finished == answer && answer.Status == TaskStatus.RanToCompletion) return answer.Result;

            cancellationToken.ThrowIfCancellationRequested();
            Debug.LogWarning($"[AppsFlyer] App Tracking Transparency not answered after {options.AttWait.TotalSeconds:0}s - starting without IDFA.");
            return att.Status;
        }

        // iOS answers "not determined" at once when the app is not active (e.g. right after another prompt); ask again.
        private async Task<AttStatus> RequestAsync(CancellationToken cancellationToken)
        {
            AttStatus status = AttStatus.NotDetermined;
            for (int attempt = 0; attempt < AttRequestAttempts && status == AttStatus.NotDetermined; attempt++)
            {
                if (attempt > 0) await delay(AttRetryDelay, cancellationToken);
                status = await att.RequestAsync();
            }
            return status;
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
