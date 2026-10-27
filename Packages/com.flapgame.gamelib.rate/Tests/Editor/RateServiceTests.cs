using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace FlapGame.GameLib.Rate.Tests
{
    public class RateServiceTests
    {
        private DateTime now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        private readonly List<string> opened = new();

        [SetUp]
        public void SetUp()
        {
            opened.Clear();
            new RateService(new RateSettings()).ResetState();
        }

        [TearDown]
        public void TearDown() => new RateService(new RateSettings()).ResetState();

        private RateService Create(RateSettings settings, IReviewBackend backend = null) =>
            new(settings, backend, utcNow: () => now, openUrl: opened.Add);

        [Test]
        public void EveryNthTrigger_Prompts()
        {
            RateService rate = Create(new RateSettings { PromptEveryTriggers = 3 });
            var answers = new List<bool>();
            for (int i = 0; i < 7; i++) answers.Add(rate.NotifyTrigger());
            Assert.That(answers, Is.EqualTo(new[] { false, false, true, false, false, true, false }));
            Assert.That(rate.PromptCount, Is.EqualTo(2));
        }

        [Test]
        public void CountsSurviveARestart()
        {
            var settings = new RateSettings { PromptEveryTriggers = 3 };
            Create(settings).NotifyTrigger();
            Create(settings).NotifyTrigger();
            Assert.That(Create(settings).NotifyTrigger(), Is.True, "the third trigger of three sessions");
        }

        [Test]
        public void MaxPrompts_StopsPrompting()
        {
            RateService rate = Create(new RateSettings { PromptEveryTriggers = 1, MaxPrompts = 2 });
            Assert.That(rate.NotifyTrigger(), Is.True);
            Assert.That(rate.NotifyTrigger(), Is.True);
            Assert.That(rate.NotifyTrigger(), Is.False);
        }

        [Test]
        public void MinHours_HoldsThePrompt_AndTheNextTriggerAsks()
        {
            RateService rate = Create(new RateSettings { PromptEveryTriggers = 2, MinHoursBetweenPrompts = 24f });
            rate.NotifyTrigger();
            Assert.That(rate.NotifyTrigger(), Is.True);
            now = now.AddHours(1);
            rate.NotifyTrigger();
            Assert.That(rate.NotifyTrigger(), Is.False, "too soon");
            now = now.AddHours(24);
            Assert.That(rate.NotifyTrigger(), Is.True, "a blocked trigger still counted");
        }

        [Test]
        public void AfterRating_NoMorePrompts_UnlessConfigured()
        {
            RateService rate = Create(new RateSettings { PromptEveryTriggers = 1 });
            rate.OpenStorePage();
            Assert.That(rate.IsDone, Is.True);
            Assert.That(rate.NotifyTrigger(), Is.False);

            rate = Create(new RateSettings { PromptEveryTriggers = 1, StopAfterRated = false });
            Assert.That(rate.NotifyTrigger(), Is.True);
        }

        [Test]
        public void StopPrompting_IsDone()
        {
            RateService rate = Create(new RateSettings { PromptEveryTriggers = 1 });
            rate.StopPrompting();
            Assert.That(rate.NotifyTrigger(), Is.False);
        }

        [Test]
        public async Task ReviewSheetUnavailable_OpensTheStorePage()
        {
            var backend = new FakeBackend { Result = false };
            RateService rate = Create(new RateSettings { AppStoreId = "123" }, backend);
            await rate.RequestReviewAsync();
            Assert.That(backend.Calls, Is.EqualTo(1));
            Assert.That(rate.IsDone, Is.True);
            // The Editor platform has no store page; the URL itself is covered below.
            Assert.That(opened, Is.Empty);
        }

        [Test]
        public async Task ReviewSheetShown_NoStorePage()
        {
            var backend = new FakeBackend { Result = true };
            RateService rate = Create(new RateSettings(), backend);
            await rate.RequestReviewAsync();
            Assert.That(backend.Calls, Is.EqualTo(1));
            Assert.That(opened, Is.Empty);
        }

        [Test]
        public void StoreUrls()
        {
            Assert.That(RateService.StoreUrl(RuntimePlatform.Android, "com.a.b", null), Is.EqualTo("https://play.google.com/store/apps/details?id=com.a.b"));
            Assert.That(RateService.StoreUrl(RuntimePlatform.IPhonePlayer, "com.a.b", " 6785376366 "),
                Is.EqualTo("itms-apps://itunes.apple.com/app/id6785376366?action=write-review"));
            Assert.That(RateService.StoreUrl(RuntimePlatform.IPhonePlayer, "com.a.b", "id678"), Is.Null);
            Assert.That(RateService.StoreUrl(RuntimePlatform.WindowsPlayer, "com.a.b", "1"), Is.Null);
        }

        [Test]
        public void Validate_RejectsANonNumericAppStoreId()
        {
            var module = ScriptableObject.CreateInstance<RateModule>();
            try
            {
                module.Settings.AppStoreId = "id123";
                var issues = new List<GameLibIssue>();
                module.Validate(issues);
                Assert.That(issues.Exists(i => i.Severity == GameLibIssueSeverity.Error), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(module);
            }
        }

        [Test]
        public void Gradle_AddsTheReviewLibraryOnce()
        {
            const string gradle = "apply plugin: 'com.android.library'\n\ndependencies {\n    implementation fileTree(dir: 'libs', include: ['*.jar'])\n}\n";
            Assert.That(Editor.RateGradle.TryAddDependency(gradle, "com.google.android.play:review:2.0.2", out string patched), Is.True);
            StringAssert.Contains("dependencies {\n    implementation 'com.google.android.play:review:2.0.2'\n    implementation fileTree", patched);
            Assert.That(Editor.RateGradle.TryAddDependency(patched, "com.google.android.play:review:2.0.3", out string again), Is.True);
            Assert.That(again, Is.EqualTo(patched), "already listed (any version)");
            Assert.That(Editor.RateGradle.TryAddDependency("android {}", "a:b:1", out _), Is.False);
            string other = gradle.Replace("fileTree(dir: 'libs', include: ['*.jar'])", "'com.google.android.play:app-update:2.1.0'");
            Assert.That(Editor.RateGradle.TryAddDependency(other, "com.google.android.play:review:2.0.2", out string both), Is.True);
            StringAssert.Contains("com.google.android.play:review:2.0.2", both, "another Play library is not the review one");
        }

        private sealed class FakeBackend : IReviewBackend
        {
            public bool Result;
            public int Calls;

            public Task<bool> RequestReviewAsync()
            {
                Calls++;
                return Task.FromResult(Result);
            }
        }
    }
}
