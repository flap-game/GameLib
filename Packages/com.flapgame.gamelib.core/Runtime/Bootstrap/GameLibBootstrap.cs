using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib
{
    /// <summary>
    /// Static entry point of GameLib. <see cref="InitializeAsync(CancellationToken)"/> registers the core services
    /// (lifecycle, consent state, entitlements, analytics), runs every module once in <see cref="GameLibModule.Order"/>
    /// and exposes the result as <see cref="Services"/>. With <see cref="GameLibSettings.AutoInitialize"/> on (default)
    /// this happens by itself before the first scene loads.
    /// Gameplay that may run before bootstrap finishes should <c>await GameLibBootstrap.WhenReadyAsync()</c>
    /// (or subscribe to <see cref="Initialized"/>) instead of polling <see cref="IsInitialized"/>.
    /// </summary>
    public static class GameLibBootstrap
    {
        private static readonly object SyncRoot = new();
        private static Task<IServiceRegistry> initializationTask;
        private static TaskCompletionSource<IServiceRegistry> readySource;

        /// <summary>The registry. Non-null while initializing and after; null before bootstrap and after <see cref="Reset"/>.</summary>
        public static IServiceRegistry Services { get; private set; }

        /// <summary>True once every module has completed.</summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>True while modules are still running.</summary>
        public static bool IsInitializing
        {
            get { lock (SyncRoot) return initializationTask != null; }
        }

        /// <summary>Raised once per successful initialization, after <see cref="IsInitialized"/> becomes true.</summary>
        public static event Action<IServiceRegistry> Initialized;

        /// <summary>Initializes from <c>Resources/GameLibSettings</c>. Without that asset only the core services are registered.</summary>
        public static ValueTask<IServiceRegistry> InitializeAsync(CancellationToken cancellationToken = default)
        {
            GameLibSettings settings = GameLibSettings.Load();
            if (settings == null)
                Debug.LogWarning("[GameLib] Resources/GameLibSettings not found; starting with core services only. Create it in GameLib > Setup.");
            return InitializeAsync(settings, cancellationToken);
        }

        public static ValueTask<IServiceRegistry> InitializeAsync(GameLibSettings settings, CancellationToken cancellationToken = default)
        {
            IEnumerable<GameLibModule> modules = settings != null ? settings.Modules : Array.Empty<GameLibModule>();
            bool logAnalytics = settings == null || settings.LogAnalyticsInDevelopment;
            return InitializeAsync(modules, logAnalytics, cancellationToken);
        }

        public static async ValueTask<IServiceRegistry> InitializeAsync(IEnumerable<GameLibModule> modules, bool logAnalytics, CancellationToken cancellationToken = default)
        {
            if (modules == null)
                throw new ArgumentNullException(nameof(modules));

            Task<IServiceRegistry> pendingTask;
            lock (SyncRoot)
            {
                if (IsInitialized)
                    return Services;

                if (initializationTask != null)
                {
                    pendingTask = initializationTask;
                }
                else
                {
                    var registry = new ServiceRegistry();
                    Services = registry;
                    RegisterCoreServices(registry, logAnalytics);
                    // Stable sort: modules with the same Order keep their list order.
                    GameLibModule[] ordered = modules.Where(m => m != null).Distinct().OrderBy(m => m.Order).ToArray();
                    pendingTask = InitializeInternalAsync(registry, ordered, cancellationToken);
                    // A fully synchronous run (every module completed or threw before its first await) has
                    // already finished - and already unwound its own state in the catch block - by the time we
                    // get here. Publishing it would leave IsInitializing stuck at true.
                    initializationTask = pendingTask.IsCompleted ? null : pendingTask;
                }
            }

            return await pendingTask;
        }

        private static void RegisterCoreServices(ServiceRegistry registry, bool logAnalytics)
        {
            registry.Register<IAppLifecycle>(AppLifecycleBehaviour.Create());
            registry.Register<IConsentState>(new ConsentStateStore());
            registry.Register<IEntitlements>(new PlayerPrefsEntitlements());

            var analytics = new AnalyticsService();
            registry.Register<IAnalytics>(analytics);
            registry.Register<IAnalyticsSinkRegistry>(analytics);
            if (logAnalytics && (Application.isEditor || Debug.isDebugBuild))
                analytics.AddSink(new DebugLogAnalyticsSink());
        }

        /// <summary>
        /// Completes when bootstrap has finished (immediately if it already has). Safe to call before
        /// <see cref="InitializeAsync(CancellationToken)"/> has started - it waits for the first successful initialization.
        /// Cancelled if <see cref="Reset"/> runs first; faulted if initialization throws.
        /// <para>
        /// <b>Never completes</b> if nothing ever bootstraps (<see cref="GameLibSettings.AutoInitialize"/> off and no
        /// <see cref="InitializeAsync(CancellationToken)"/> call). Prefer
        /// <see cref="WhenReadyAsync(TimeSpan, CancellationToken)"/> anywhere a missing bootstrap is possible.
        /// </para>
        /// </summary>
        public static Task<IServiceRegistry> WhenReadyAsync()
        {
            lock (SyncRoot)
            {
                if (IsInitialized)
                    return Task.FromResult(Services);

                readySource ??= new TaskCompletionSource<IServiceRegistry>(TaskCreationOptions.RunContinuationsAsynchronously);
                return readySource.Task;
            }
        }

        /// <summary>
        /// Like <see cref="WhenReadyAsync()"/> but gives up after <paramref name="timeout"/> with a
        /// <see cref="TimeoutException"/> that names the likely cause. <paramref name="cancellationToken"/> cancels the
        /// wait only (OperationCanceledException), never the bootstrap itself. <see cref="Timeout.InfiniteTimeSpan"/>
        /// waits forever. The continuation runs on the caller's synchronization context (Unity main thread when awaited from it).
        /// </summary>
        public static async Task<IServiceRegistry> WhenReadyAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be >= 0 or Timeout.InfiniteTimeSpan.");

            Task<IServiceRegistry> ready = WhenReadyAsync();
            if (ready.IsCompleted) return await ready;
            cancellationToken.ThrowIfCancellationRequested();

            using (var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                Task delay = Task.Delay(timeout, delayCts.Token);
                Task first = await Task.WhenAny(ready, delay);
                if (first == ready)
                {
                    delayCts.Cancel(); // stop the timer
                    return await ready;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"GameLibBootstrap was not ready after {timeout.TotalSeconds:0.###} s. Is AutoInitialize on in Resources/GameLibSettings " +
                "(or is GameLibBootstrap.InitializeAsync called)? Without either WhenReadyAsync() never completes.");
        }

        private static async Task<IServiceRegistry> InitializeInternalAsync(ServiceRegistry registry, IReadOnlyList<GameLibModule> modules, CancellationToken cancellationToken)
        {
            try
            {
                foreach (GameLibModule module in modules)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (module == null)
                        continue;

                    if (!IsCurrent(registry))
                    {
                        // Reset() ran while the previous module was awaiting: this registry is dead, stop feeding it.
                        break;
                    }

                    try
                    {
                        await module.InstallAsync(registry, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception) when (!module.Required)
                    {
                        // One broken optional module must not take the registry down: log it with the module name,
                        // keep the services already registered, and move on. Required modules still fail-fast.
                        Debug.LogError(
                            $"[GameLib] Module '{DescribeModule(module)}' threw {exception.GetType().Name}: {exception.Message}. " +
                            $"Bootstrap continues without its services; turn on Required on the module to fail-fast instead.\n{exception}");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                bool current;
                TaskCompletionSource<IServiceRegistry> ready = null;
                lock (SyncRoot)
                {
                    current = ReferenceEquals(Services, registry);
                    if (current)
                    {
                        IsInitialized = true;
                        initializationTask = null;
                        ready = readySource;
                        readySource = null;
                    }
                }

                if (!current)
                {
                    // Reset() ran while a module was awaiting. Modules that resumed after Reset() may have
                    // registered into this dead registry - dispose it again so nothing leaks, and never publish it.
                    registry.Dispose();
                    throw new OperationCanceledException("[GameLib] Bootstrap was reset while initializing.");
                }

                ready?.TrySetResult(registry);
                RaiseInitialized(registry);
                return registry;
            }
            catch (Exception exception)
            {
                // Only unwind state that still belongs to this run. After Reset() (and possibly a fresh
                // InitializeAsync) the static fields describe a newer registry and must not be touched.
                bool current;
                TaskCompletionSource<IServiceRegistry> ready = null;
                lock (SyncRoot)
                {
                    current = ReferenceEquals(Services, registry);
                    if (current)
                    {
                        Services = null;
                        IsInitialized = false;
                        initializationTask = null;
                        ready = readySource;
                        readySource = null;
                    }
                }

                if (current)
                    registry.Dispose();

                if (exception is OperationCanceledException)
                    ready?.TrySetCanceled();
                else
                    ready?.TrySetException(exception);
                throw;
            }
        }

        private static bool IsCurrent(ServiceRegistry registry)
        {
            lock (SyncRoot) return ReferenceEquals(Services, registry);
        }

        // Destroyed ScriptableObjects still have a managed shell; use the Unity null check before touching .name.
        private static string DescribeModule(GameLibModule module) =>
            module != null ? module.name : module?.GetType().Name ?? "null";

        /// <summary>
        /// Disposes every registered service and returns to the uninitialized state. Safe while initializing:
        /// the pending <see cref="InitializeAsync(CancellationToken)"/> and <see cref="WhenReadyAsync()"/> are cancelled
        /// and the half-built registry is never published.
        /// </summary>
        public static void Reset()
        {
            IServiceRegistry toDispose;
            TaskCompletionSource<IServiceRegistry> ready;
            lock (SyncRoot)
            {
                toDispose = Services;
                Services = null;
                IsInitialized = false;
                initializationTask = null;
                ready = readySource;
                readySource = null;
            }

            ready?.TrySetCanceled();
            (toDispose as IDisposable)?.Dispose();
        }

        private static void RaiseInitialized(IServiceRegistry registry)
        {
            Action<IServiceRegistry> handlers = Initialized;
            if (handlers == null)
                return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try { ((Action<IServiceRegistry>)handler)(registry); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
