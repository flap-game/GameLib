using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Att
{
    /// <summary>
    /// <see cref="IAttService"/> over <see cref="IAttPrompt"/>. One prompt at a time (concurrent requests share it), shown
    /// only while the app is focused, asked again when iOS answers "not determined" at once (the app was not active yet),
    /// and wrapped in <see cref="IAppLifecycle.BeginExternalActivity"/> so the prompt never counts as a return to the app.
    /// Every answer goes to <see cref="IConsentState.SetAtt"/>.
    /// </summary>
    public sealed class AttService : IAttService
    {
        public const string ExternalActivityReason = "att";

        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan FocusPollInterval = TimeSpan.FromMilliseconds(100);
        private const int RequestAttempts = 3;

        private readonly IAttPrompt prompt;
        private readonly IConsentState consent;
        private readonly IAppLifecycle lifecycle;
        private readonly Func<bool> isFocused;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;
        private Task<AttStatus> pending;

        /// <param name="isFocused">Defaults to <see cref="Application.isFocused"/>.</param>
        /// <param name="delay">Defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>; replaced in tests.</param>
        public AttService(IAttPrompt prompt, IConsentState consent, IAppLifecycle lifecycle,
                          Func<bool> isFocused = null, Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            this.prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
            this.consent = consent;
            this.lifecycle = lifecycle;
            this.isFocused = isFocused ?? (() => Application.isFocused);
            this.delay = delay ?? Task.Delay;
        }

        public AttStatus Status => prompt.Status;

        public Task<AttStatus> RequestAsync(CancellationToken cancellationToken = default)
        {
            AttStatus status = prompt.Status;
            if (status != AttStatus.NotDetermined)
            {
                Publish(status);
                return Task.FromResult(status);
            }

            Task<AttStatus> task = pending;
            if (task == null)
            {
                task = ShowAsync();
                // ShowAsync clears pending when it ends; one that already ended must not be kept.
                if (!task.IsCompleted) pending = task;
            }
            return cancellationToken.CanBeCanceled ? WithCancellation(task, cancellationToken) : task;
        }

        /// <summary>
        /// The automatic request: after bootstrap and the CMP answer (when a CMP is registered, at most
        /// <paramref name="consentWait"/>), then <paramref name="extraDelay"/>, then the prompt.
        /// </summary>
        public async Task<AttStatus> RequestAfterConsentAsync(Func<Task> bootstrapReady, Func<bool> hasCmp, TimeSpan consentWait,
                                                              TimeSpan extraDelay, CancellationToken cancellationToken = default)
        {
            if (prompt.Status != AttStatus.NotDetermined) return await RequestAsync(cancellationToken);

            if (bootstrapReady != null) await bootstrapReady();
            if (consent != null && !consent.Current.IsResolved && (hasCmp == null || hasCmp()) &&
                !await consent.WaitUntilResolvedAsync(consentWait, cancellationToken, delay))
                Debug.LogWarning($"{AttModule.LogTag} No consent answer after {consentWait.TotalSeconds:0}s - asking anyway.");

            if (extraDelay > TimeSpan.Zero) await delay(extraDelay, cancellationToken);
            return await RequestAsync(cancellationToken);
        }

        /// <summary>Writes the current status to <see cref="IConsentState"/> (nothing on platforms without ATT).</summary>
        public void Publish() => Publish(prompt.Status);

        private async Task<AttStatus> ShowAsync()
        {
            try
            {
                // iOS ignores the request (answers "not determined" without a prompt) while the app is not active, and
                // the prompt shows once per launch at most - so wait however long the player is away.
                while (!isFocused())
                    await delay(FocusPollInterval, CancellationToken.None);

                AttStatus status = AttStatus.NotDetermined;
                using (lifecycle?.BeginExternalActivity(ExternalActivityReason))
                {
                    for (int attempt = 0; attempt < RequestAttempts && status == AttStatus.NotDetermined; attempt++)
                    {
                        if (attempt > 0) await delay(RetryDelay, CancellationToken.None);
                        status = await prompt.RequestAsync();
                    }
                }

                Debug.Log($"{AttModule.LogTag} App Tracking Transparency: {status}.");
                Publish(status);
                return status;
            }
            finally
            {
                pending = null;
            }
        }

        private void Publish(AttStatus status)
        {
            if (status != AttStatus.NotApplicable) consent?.SetAtt(status);
        }

        // The prompt stays on screen for the other callers; only this caller stops waiting.
        private static async Task<AttStatus> WithCancellation(Task<AttStatus> task, CancellationToken cancellationToken)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, cancelled.Task) != task)
                    cancellationToken.ThrowIfCancellationRequested();
            }
            return await task;
        }
    }
}
