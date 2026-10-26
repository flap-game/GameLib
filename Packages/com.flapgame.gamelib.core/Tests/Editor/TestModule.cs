using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlapGame.GameLib.Tests
{
    public sealed class TestModule : GameLibModule
    {
        public int TestOrder;
        public List<string> Log;
        public bool Fail;

        public override int Order => TestOrder;

        // Synchronous on purpose: an EditMode test blocks the main thread, so a continuation posted to Unity's
        // synchronization context would never run.
        public override ValueTask InstallAsync(IServiceRegistry services, CancellationToken cancellationToken = default)
        {
            if (Fail)
                throw new InvalidOperationException(name + " failed");
            Log.Add(name);
            return default;
        }
    }
}
