using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FlapGame.GameLib.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FlapGame.GameLib.Tests
{
    public class GameLibBootstrapTests
    {
        private readonly List<Object> created = new();

        [SetUp]
        public void SetUp() => GameLibBootstrap.Reset();

        [TearDown]
        public void TearDown()
        {
            GameLibBootstrap.Reset();
            foreach (Object o in created)
                Object.DestroyImmediate(o);
            created.Clear();
        }

        [Test]
        public async Task Initialize_RegistersCoreServices()
        {
            IServiceRegistry services = await GameLibBootstrap.InitializeAsync(Array.Empty<GameLibModule>(), logAnalytics: false);

            Assert.IsTrue(GameLibBootstrap.IsInitialized);
            Assert.IsTrue(services.IsRegistered<IAppLifecycle>());
            Assert.IsTrue(services.IsRegistered<IConsentState>());
            Assert.IsTrue(services.IsRegistered<IEntitlements>());
            Assert.IsTrue(services.IsRegistered<IAnalytics>());
            Assert.IsTrue(services.IsRegistered<IAnalyticsSinkRegistry>());
        }

        [Test]
        public async Task Modules_RunInOrder()
        {
            var log = new List<string>();
            TestModule late = Module("late", 10, log);
            TestModule early = Module("early", -10, log);

            await GameLibBootstrap.InitializeAsync(new GameLibModule[] { late, early }, logAnalytics: false);

            CollectionAssert.AreEqual(new[] { "early", "late" }, log);
        }

        [Test]
        public async Task OptionalModuleFailure_IsLoggedAndSkipped()
        {
            var log = new List<string>();
            TestModule broken = Module("broken", 0, log, fail: true);
            TestModule after = Module("after", 1, log);

            LogAssert.Expect(LogType.Error, new Regex("broken"));
            await GameLibBootstrap.InitializeAsync(new GameLibModule[] { broken, after }, logAnalytics: false);

            Assert.IsTrue(GameLibBootstrap.IsInitialized);
            CollectionAssert.Contains(log, "after");
        }

        [Test]
        public void RequiredModuleFailure_AbortsBootstrap()
        {
            TestModule broken = Module("broken", 0, new List<string>(), fail: true);
            broken.Required = true;

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await GameLibBootstrap.InitializeAsync(new GameLibModule[] { broken }, logAnalytics: false));
            Assert.IsFalse(GameLibBootstrap.IsInitialized);
            Assert.IsNull(GameLibBootstrap.Services);
        }

        [Test]
        public async Task WhenReady_CompletesAfterInitialize()
        {
            Task<IServiceRegistry> ready = GameLibBootstrap.WhenReadyAsync();
            Assert.IsFalse(ready.IsCompleted);

            IServiceRegistry services = await GameLibBootstrap.InitializeAsync(Array.Empty<GameLibModule>(), logAnalytics: false);

            Assert.AreSame(services, await ready);
        }

        [Test]
        public void WhenReady_AfterFailedInitialize_IsFaulted()
        {
            TestModule broken = Module("broken", 0, new List<string>(), fail: true);
            broken.Required = true;
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await GameLibBootstrap.InitializeAsync(new GameLibModule[] { broken }, logAnalytics: false));

            Task<IServiceRegistry> ready = GameLibBootstrap.WhenReadyAsync();

            Assert.IsTrue(ready.IsFaulted);
            Assert.ThrowsAsync<InvalidOperationException>(async () => await ready);
        }

        [Test]
        public async Task WhenReady_AfterResetFollowingFailure_WaitsForNextInitialize()
        {
            TestModule broken = Module("broken", 0, new List<string>(), fail: true);
            broken.Required = true;
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await GameLibBootstrap.InitializeAsync(new GameLibModule[] { broken }, logAnalytics: false));
            GameLibBootstrap.Reset();

            Task<IServiceRegistry> ready = GameLibBootstrap.WhenReadyAsync();
            Assert.IsFalse(ready.IsCompleted);

            IServiceRegistry services = await GameLibBootstrap.InitializeAsync(Array.Empty<GameLibModule>(), logAnalytics: false);
            Assert.AreSame(services, await ready);
        }

        [Test]
        public async Task Reset_DropsInitializedHandlers()
        {
            int raised = 0;
            GameLibBootstrap.Initialized += _ => raised++;
            GameLibBootstrap.Reset();

            await GameLibBootstrap.InitializeAsync(Array.Empty<GameLibModule>(), logAnalytics: false);

            Assert.AreEqual(0, raised);
        }

        [Test]
        public void Catalog_ListsDependenciesFirst()
        {
            IReadOnlyList<GameLibModuleInfo> chain = GameLibModuleCatalog.WithDependencies(GameLibModuleCatalog.Find("ads.max"));
            Assert.AreEqual("ads", chain[0].Id);
            Assert.AreEqual("ads.max", chain[1].Id);
        }

        private TestModule Module(string name, int order, List<string> log, bool fail = false)
        {
            var module = ScriptableObject.CreateInstance<TestModule>();
            module.name = name;
            module.TestOrder = order;
            module.Log = log;
            module.Fail = fail;
            created.Add(module);
            return module;
        }
    }
}
