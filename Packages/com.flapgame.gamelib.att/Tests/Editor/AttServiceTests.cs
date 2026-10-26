using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FlapGame.GameLib.Att.Tests
{
    public class AttServiceTests
    {
        private sealed class FakePrompt : IAttPrompt
        {
            public AttStatus Status { get; set; } = AttStatus.NotDetermined;
            public AttStatus Answer = AttStatus.Authorized;
            public TaskCompletionSource<AttStatus> Pending;
            public Action OnRequest;
            public int Requests;

            public Task<AttStatus> RequestAsync()
            {
                Requests++;
                OnRequest?.Invoke();
                if (Pending != null) return Pending.Task;
                Status = Answer;
                return Task.FromResult(Answer);
            }
        }

        // The lifecycle the app open ad listens to, minus the MonoBehaviour.
        private sealed class FakeLifecycle : IAppLifecycle
        {
            public readonly AppResumeTracker Tracker = new();
            public AppResumeInfo? LastResume;

            public bool IsPaused { get; private set; }
            public bool HasFocus => true;
            public event Action<bool> PauseChanged { add { } remove { } }
            public event Action<bool> FocusChanged { add { } remove { } }
            public event Action<AppResumeInfo> Resumed { add { } remove { } }
            public event Action Quitting { add { } remove { } }
            public IDisposable BeginExternalActivity(string reason) => Tracker.BeginExternalActivity(reason);
            public void MarkExternalActivity(string reason) => Tracker.MarkExternalActivity(reason);

            public void Pause() => Tracker.OnPaused(0);

            public void Resume()
            {
                if (Tracker.TryResume(1, out AppResumeInfo info)) LastResume = info;
            }
        }

        private static Task NoDelay(TimeSpan time, CancellationToken cancellationToken) => Task.CompletedTask;

        [Test]
        public async Task Request_ShowsThePrompt_AndSharesTheAnswer()
        {
            var prompt = new FakePrompt();
            var consent = new ConsentStateStore();
            var service = new AttService(prompt, consent, null, () => true, NoDelay);

            LogAssert.Expect(LogType.Log, new Regex("Authorized"));
            AttStatus status = await service.RequestAsync();

            Assert.That(status, Is.EqualTo(AttStatus.Authorized));
            Assert.That(prompt.Requests, Is.EqualTo(1));
            Assert.That(consent.Current.Att, Is.EqualTo(AttStatus.Authorized));
            Assert.That(consent.Current.IsResolved, Is.False, "ATT is not the CMP answer");
        }

        [Test]
        public async Task Request_AlreadyAnswered_DoesNotPrompt()
        {
            var prompt = new FakePrompt { Status = AttStatus.Denied };
            var consent = new ConsentStateStore();
            var service = new AttService(prompt, consent, null, () => true, NoDelay);

            Assert.That(await service.RequestAsync(), Is.EqualTo(AttStatus.Denied));
            Assert.That(prompt.Requests, Is.EqualTo(0));
            Assert.That(consent.Current.Att, Is.EqualTo(AttStatus.Denied));
        }

        [Test]
        public async Task Request_NotApplicable_OnAndroidAndEditor()
        {
            var prompt = new FakePrompt { Status = AttStatus.NotApplicable };
            var consent = new ConsentStateStore();
            var service = new AttService(prompt, consent, null, () => true, NoDelay);

            Assert.That(await service.RequestAsync(), Is.EqualTo(AttStatus.NotApplicable));
            Assert.That(prompt.Requests, Is.EqualTo(0));
            Assert.That(consent.Current.Att, Is.EqualTo(AttStatus.NotApplicable));
        }

        [Test]
        public async Task Request_NotDeterminedWhileInactive_IsAskedAgain()
        {
            var prompt = new FakePrompt { Answer = AttStatus.NotDetermined };
            var service = new AttService(prompt, null, null, () => true, NoDelay);

            LogAssert.Expect(LogType.Log, new Regex("NotDetermined"));
            await service.RequestAsync();

            Assert.That(prompt.Requests, Is.EqualTo(3));
        }

        [Test]
        public async Task Request_WaitsForFocus()
        {
            var prompt = new FakePrompt();
            bool focused = false;
            int polls = 0;
            var service = new AttService(prompt, null, null, () => focused, (_, _) =>
            {
                if (++polls == 5) focused = true;
                return Task.CompletedTask;
            });
            prompt.OnRequest = () => Assert.That(focused, Is.True, "asked only once the app is active");

            LogAssert.Expect(LogType.Log, new Regex("Authorized"));
            await service.RequestAsync();

            Assert.That(prompt.Requests, Is.EqualTo(1));
        }

        [Test]
        public async Task Request_Concurrent_ShareOnePrompt()
        {
            var prompt = new FakePrompt { Pending = new TaskCompletionSource<AttStatus>() };
            var service = new AttService(prompt, null, null, () => true, NoDelay);

            Task<AttStatus> first = service.RequestAsync();
            Task<AttStatus> second = service.RequestAsync();
            prompt.Status = AttStatus.Denied;
            LogAssert.Expect(LogType.Log, new Regex("Denied"));
            prompt.Pending.SetResult(AttStatus.Denied);

            Assert.That(await first, Is.EqualTo(AttStatus.Denied));
            Assert.That(await second, Is.EqualTo(AttStatus.Denied));
            Assert.That(prompt.Requests, Is.EqualTo(1));
        }

        [Test]
        public async Task Request_LeavingForThePrompt_IsNotAReturnToTheApp()
        {
            var lifecycle = new FakeLifecycle();
            var prompt = new FakePrompt();
            prompt.OnRequest = lifecycle.Pause; // iOS pauses Unity while the system alert is up
            var service = new AttService(prompt, null, lifecycle, () => true, NoDelay);

            LogAssert.Expect(LogType.Log, new Regex("Authorized"));
            await service.RequestAsync();
            lifecycle.Resume(); // delivered after the answer

            Assert.That(lifecycle.LastResume.HasValue, Is.True);
            Assert.That(lifecycle.LastResume.Value.FromExternalActivity, Is.True, "no app open ad after the prompt");
            Assert.That(lifecycle.LastResume.Value.ExternalReason, Is.EqualTo(AttService.ExternalActivityReason));
        }

        [Test]
        public async Task AfterConsent_WaitsForTheCmp_ThenTheDelay_ThenPrompts()
        {
            var prompt = new FakePrompt();
            var consent = new ConsentStateStore();
            var delays = new System.Collections.Generic.List<TimeSpan>();
            var service = new AttService(prompt, consent, null, () => true, (time, ct) =>
            {
                delays.Add(time);
                return time == TimeSpan.FromSeconds(60) ? Task.Delay(Timeout.Infinite, ct) : Task.CompletedTask;
            });

            Task<AttStatus> run = service.RequestAfterConsentAsync(() => Task.CompletedTask, () => true,
                                                                   TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(3));
            await Task.Yield();
            Assert.That(prompt.Requests, Is.EqualTo(0), "never over the consent form");

            LogAssert.Expect(LogType.Log, new Regex("Authorized"));
            consent.Update(new ConsentState { GdprApplies = true, HasUserConsent = true });
            Assert.That(await run, Is.EqualTo(AttStatus.Authorized));

            Assert.That(prompt.Requests, Is.EqualTo(1));
            Assert.That(delays, Does.Contain(TimeSpan.FromSeconds(3)), "the configured delay");
        }

        [Test]
        public async Task AfterConsent_NoCmp_DoesNotWaitForIt()
        {
            var prompt = new FakePrompt();
            var service = new AttService(prompt, new ConsentStateStore(), null, () => true, (time, ct) =>
                time == TimeSpan.FromSeconds(60) ? Task.Delay(Timeout.Infinite, ct) : Task.CompletedTask);

            LogAssert.Expect(LogType.Log, new Regex("Authorized"));
            AttStatus status = await service.RequestAfterConsentAsync(null, () => false, TimeSpan.FromSeconds(60), TimeSpan.Zero);

            Assert.That(status, Is.EqualTo(AttStatus.Authorized));
        }

        [Test]
        public async Task AfterConsent_CmpTimeout_AsksAnyway()
        {
            var prompt = new FakePrompt();
            var service = new AttService(prompt, new ConsentStateStore(), null, () => true, NoDelay);

            LogAssert.Expect(LogType.Warning, new Regex("No consent answer"));
            LogAssert.Expect(LogType.Log, new Regex("Authorized"));
            AttStatus status = await service.RequestAfterConsentAsync(null, () => true, TimeSpan.FromSeconds(60), TimeSpan.Zero);

            Assert.That(status, Is.EqualTo(AttStatus.Authorized));
        }
    }
}
