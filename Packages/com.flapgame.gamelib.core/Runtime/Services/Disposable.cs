using System;
using System.Threading;

namespace FlapGame.GameLib
{
    /// <summary>
    /// <c>services.RegisterDisposable(Disposable.Create(() => source.Changed -= OnChanged))</c>: undo a subscription when
    /// the registry shuts down. The action runs once.
    /// </summary>
    public static class Disposable
    {
        public static IDisposable Create(Action dispose) => new ActionDisposable(dispose);

        private sealed class ActionDisposable : IDisposable
        {
            private Action dispose;

            public ActionDisposable(Action dispose) => this.dispose = dispose;

            public void Dispose() => Interlocked.Exchange(ref dispose, null)?.Invoke();
        }
    }
}
