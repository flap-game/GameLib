using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FlapGame.GameLib.RemoteConfig.Tests
{
    public class RemoteConfigServiceTests
    {
        private sealed class FakeProvider : IRemoteConfigProvider
        {
            public Func<Task> Fetch = () => Task.CompletedTask;
            public Func<Task> SetDefaults = () => Task.CompletedTask;
            public int SetDefaultsCalls;
            public int FetchCalls;
            public IReadOnlyDictionary<string, string> Defaults;
            // Server values only, like Firebase's RemoteValue source.
            public readonly Dictionary<string, string> Values = new();
            public bool Started;

            public Task SetDefaultsAsync(IReadOnlyDictionary<string, string> defaults, CancellationToken cancellationToken)
            {
                SetDefaultsCalls++;
                Defaults = defaults;
                return SetDefaults();
            }

            public async Task FetchAndActivateAsync(CancellationToken cancellationToken)
            {
                FetchCalls++;
                await Fetch();
                Started = true;
            }

            public bool TryGetRemoteString(string key, out string value)
            {
                value = null;
                return Started && Values.TryGetValue(key, out value);
            }

            public IReadOnlyDictionary<string, string> GetRemoteValues() => Values;
        }

        private string cachePath;

        [SetUp]
        public void SetUp() => cachePath = Path.Combine(Path.GetTempPath(), $"gamelib_rc_test_{Guid.NewGuid():N}.json");

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(cachePath)) File.Delete(cachePath);
        }

        private RemoteConfigService Create(FakeProvider provider, Dictionary<string, string> defaults = null, double timeoutSeconds = 5) =>
            new(provider, defaults, new RemoteConfigCache(cachePath), TimeSpan.FromSeconds(timeoutSeconds));

        private static async Task<bool> CompletesWithin(Task task, int ms) => await Task.WhenAny(task, Task.Delay(ms)) == task;

        [Test]
        public async Task Reads_ProviderWins_ThenCache_ThenDefaults_ThenCallerDefault()
        {
            new RemoteConfigCache(cachePath).Save(new Dictionary<string, string> { ["b"] = "cached", ["c"] = "cached" });
            var provider = new FakeProvider();
            provider.Values["c"] = "remote";
            RemoteConfigService service = Create(provider, new Dictionary<string, string> { ["a"] = "default", ["b"] = "default" });

            Assert.That(service.GetString("a"), Is.EqualTo("default"));
            Assert.That(service.GetString("b"), Is.EqualTo("cached"));
            Assert.That(service.GetString("c"), Is.EqualTo("cached"), "the provider has not fetched yet");
            Assert.That(service.GetString("missing", "x"), Is.EqualTo("x"));

            await service.FetchAndActivateAsync();

            Assert.That(service.GetString("c"), Is.EqualTo("remote"));
            Assert.That(provider.Defaults["b"], Is.EqualTo("cached"), "the provider gets defaults merged with the cache");
        }

        [Test]
        public async Task Activation_SavesServerValuesToTheCache_AndRaisesActivated()
        {
            var provider = new FakeProvider();
            provider.Values["ads_plan"] = "{\"Name\":\"p\"}";
            RemoteConfigService service = Create(provider);
            int activated = 0;
            service.Activated += () => activated++;

            await service.FetchAndActivateAsync();

            Assert.That(activated, Is.EqualTo(1));
            Assert.That(new RemoteConfigCache(cachePath).Load()["ads_plan"], Is.EqualTo("{\"Name\":\"p\"}"));

            RemoteConfigService nextLaunch = Create(new FakeProvider());
            Assert.That(nextLaunch.GetString("ads_plan"), Is.EqualTo("{\"Name\":\"p\"}"), "the next launch starts from the cache");
        }

        [Test]
        public async Task Activation_CachesServerValuesOnly_SoDefaultsAndRemovedKeysComeBackNextLaunch()
        {
            new RemoteConfigCache(cachePath).Save(new Dictionary<string, string> { ["removed"] = "old_remote", ["kept"] = "old_remote" });
            var provider = new FakeProvider();
            provider.Values["kept"] = "remote";
            RemoteConfigService service = Create(provider, new Dictionary<string, string> { ["tuning"] = "v1", ["removed"] = "default" });

            await service.FetchAndActivateAsync();

            Dictionary<string, string> cached = new RemoteConfigCache(cachePath).Load();
            Assert.That(cached, Is.EquivalentTo(new Dictionary<string, string> { ["kept"] = "remote" }), "pushed defaults are not cached");

            RemoteConfigService nextLaunch = Create(new FakeProvider(), new Dictionary<string, string> { ["tuning"] = "v2", ["removed"] = "default" });
            Assert.That(nextLaunch.GetString("tuning"), Is.EqualTo("v2"), "a default changed in an app update reaches existing users");
            Assert.That(nextLaunch.GetString("removed"), Is.EqualTo("default"), "a key removed in the console falls back to its default");
            Assert.That(nextLaunch.GetString("kept"), Is.EqualTo("remote"));
        }

        [Test]
        public void TypedReads_ParseInvariant_AndFallBackOnBadValues()
        {
            RemoteConfigService service = Create(new FakeProvider(), new Dictionary<string, string>
            {
                ["on"] = "Yes", ["off"] = "0", ["n"] = "42", ["d"] = "1.5", ["bad"] = "abc",
            });

            Assert.That(service.GetBool("on"), Is.True);
            Assert.That(service.GetBool("off", true), Is.False);
            Assert.That(service.GetBool("bad", true), Is.True);
            Assert.That(service.GetLong("n"), Is.EqualTo(42L));
            Assert.That(service.GetLong("bad", 7), Is.EqualTo(7L));
            Assert.That(service.GetDouble("d"), Is.EqualTo(1.5));
        }

        [Test]
        public async Task NoProvider_IsReadyAfterTheFirstFetch_WithDefaults()
        {
            var service = new RemoteConfigService(null, new Dictionary<string, string> { ["a"] = "1" }, new RemoteConfigCache(cachePath), TimeSpan.FromSeconds(5));
            Assert.That(service.IsReady, Is.False);

            await service.FetchAndActivateAsync();

            Assert.That(service.IsReady, Is.True);
            Assert.That(service.GetLong("a"), Is.EqualTo(1L));
        }

        [Test]
        public async Task WhenReady_Completes_WhenProviderThrows()
        {
            var provider = new FakeProvider { Fetch = () => Task.FromException(new InvalidOperationException("dependency check failed")) };
            RemoteConfigService service = Create(provider);

            LogAssert.Expect(LogType.Warning, new Regex("Fetch failed"));
            await service.FetchAndActivateAsync();

            Assert.That(service.IsReady, Is.True);
            Assert.That(await CompletesWithin(service.WhenReadyAsync(), 100), Is.True);
        }

        [Test]
        public async Task WhenReady_Completes_OnTimeout_AndLateFetchRaisesActivated()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new FakeProvider { Fetch = () => gate.Task };
            RemoteConfigService service = Create(provider, timeoutSeconds: 0.05);
            var activated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.Activated += () => activated.TrySetResult(true);

            LogAssert.Expect(LogType.Warning, new Regex("timed out"));
            await service.FetchAndActivateAsync();

            Assert.That(service.IsReady, Is.True, "readiness does not wait for a hanging provider");
            Assert.That(activated.Task.IsCompleted, Is.False);

            LogAssert.Expect(LogType.Log, new Regex("Late fetch activated"));
            gate.SetResult(true);
            Assert.That(await CompletesWithin(activated.Task, 2000), Is.True, "a fetch landing after the timeout is still activated");
        }

        [Test]
        public async Task FetchAfterTimeout_JoinsTheLateFetch_InsteadOfStartingAnother()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new FakeProvider { Fetch = () => gate.Task };
            RemoteConfigService service = Create(provider, timeoutSeconds: 0.05);
            int activated = 0;
            var firstActivation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.Activated += () => { activated++; firstActivation.TrySetResult(true); };

            LogAssert.Expect(LogType.Warning, new Regex("timed out"));
            await service.FetchAndActivateAsync();
            LogAssert.Expect(LogType.Warning, new Regex("timed out"));
            await service.FetchAndActivateAsync(); // a gameplay refresh while the first fetch still runs

            Assert.That(provider.FetchCalls, Is.EqualTo(1));
            Assert.That(provider.SetDefaultsCalls, Is.EqualTo(1));

            LogAssert.Expect(LogType.Log, new Regex("Late fetch activated"));
            gate.SetResult(true);
            Assert.That(await CompletesWithin(firstActivation.Task, 2000), Is.True);
            await Task.Delay(100);
            Assert.That(activated, Is.EqualTo(1), "one round trip, one activation");

            await service.FetchAndActivateAsync();
            Assert.That(provider.FetchCalls, Is.EqualTo(2), "a fetch after the late one ended starts a new round trip");
        }

        [Test]
        public async Task WhenReady_Completes_WhenProviderHangsInsideSetDefaults()
        {
            var provider = new FakeProvider { SetDefaults = () => new TaskCompletionSource<bool>().Task };
            RemoteConfigService service = Create(provider, timeoutSeconds: 0.05);

            LogAssert.Expect(LogType.Warning, new Regex("timed out"));
            await service.FetchAndActivateAsync();

            Assert.That(await CompletesWithin(service.WhenReadyAsync(), 100), Is.True);
        }

        [Test]
        public async Task SetDefaults_IsRetried_OnNextFetch_AfterAFailure()
        {
            int failures = 1;
            var provider = new FakeProvider
            {
                SetDefaults = () => failures-- > 0 ? Task.FromException(new InvalidOperationException("not started")) : Task.CompletedTask,
            };
            RemoteConfigService service = Create(provider);

            LogAssert.Expect(LogType.Warning, new Regex("Fetch failed"));
            await service.FetchAndActivateAsync();
            await service.FetchAndActivateAsync();
            await service.FetchAndActivateAsync();

            Assert.That(provider.SetDefaultsCalls, Is.EqualTo(2), "retried once after the failure, then applied once only");
        }

        [Test]
        public void GetObject_ParsesTopLevelArray_AndEnumNames()
        {
            RemoteConfigService service = Create(new FakeProvider(), new Dictionary<string, string>
            {
                ["list"] = "[{\"Name\":\"a\",\"Kind\":\"Beta\"},{\"Name\":\"b\",\"Kind\":0}]",
            });

            List<Row> rows = service.GetObject<List<Row>>("list");

            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0].Kind, Is.EqualTo(RowKind.Beta));
            Assert.That(rows[1].Name, Is.EqualTo("b"));
            Assert.That(rows[1].Kind, Is.EqualTo(RowKind.Alpha));
            Assert.That(service.GetObject("missing", rows), Is.SameAs(rows));
        }

        public enum RowKind { Alpha, Beta }

        [Serializable]
        public sealed class Row
        {
            public string Name;
            public RowKind Kind;
        }
    }
}
