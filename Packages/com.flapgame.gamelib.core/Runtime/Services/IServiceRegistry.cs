using System;

namespace FlapGame.GameLib
{
    /// <summary>
    /// Service locator that holds the runtime's registered services. Installers register into it
    /// during bootstrap; gameplay resolves interfaces out of it. Reached via <c>GameLibBootstrap.Services</c>.
    /// </summary>
    public interface IServiceRegistry
    {
        /// <summary>Registers <paramref name="instance"/> under its <typeparamref name="TService"/> interface. Re-registering replaces the previous instance.</summary>
        void Register<TService>(TService instance) where TService : class;

        /// <summary>Registers <paramref name="instance"/> under the explicit <paramref name="serviceType"/>.</summary>
        void Register(Type serviceType, object instance);

        /// <summary>
        /// Attaches an <see cref="IDisposable"/> (event subscription, native handle, helper object) to the
        /// registry lifetime without exposing it as a service. Disposed together with the services on shutdown.
        /// </summary>
        void RegisterDisposable(IDisposable disposable);

        /// <summary>True when a service is registered under <typeparamref name="TService"/>.</summary>
        bool IsRegistered<TService>() where TService : class;

        /// <summary>Resolves <typeparamref name="TService"/>, returning false instead of throwing when it is not registered (e.g. the module was stripped).</summary>
        bool TryResolve<TService>(out TService service) where TService : class;

        /// <summary>Resolves <typeparamref name="TService"/>, throwing if it is not registered.</summary>
        TService Resolve<TService>() where TService : class;
    }
}
