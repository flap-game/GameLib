using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace FlapGame.GameLib.Firebase.Tests
{
    public class FirebaseStartupTests
    {
        private Func<CancellationToken, Task> saved;

        [SetUp]
        public void SetUp() => saved = FirebaseStartup.Initializer;

        [TearDown]
        public void TearDown() => FirebaseStartup.Initializer = saved;

        [Test]
        public void WithoutSdk_EnsureReady_FaultsInsteadOfThrowing()
        {
            FirebaseStartup.Initializer = null;

            Task task = FirebaseStartup.EnsureReadyAsync();

            Assert.That(FirebaseStartup.IsSdkInstalled, Is.False);
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(task.Exception.InnerException.Message, Does.Contain("not installed"));
        }

        [Test]
        public void ThrowingInitializer_BecomesAFaultedTask()
        {
            FirebaseStartup.Initializer = _ => throw new InvalidOperationException("no google-services.json");

            Task task = FirebaseStartup.EnsureReadyAsync();

            Assert.That(task.IsFaulted, Is.True);
            Assert.That(task.Exception.InnerException.Message, Is.EqualTo("no google-services.json"));
        }
    }
}
