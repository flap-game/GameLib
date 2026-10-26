using System;
using System.Collections.Generic;

namespace FlapGame.GameLib
{
    public sealed class ServiceRegistry : IServiceRegistry, IDisposable
    {
        private readonly Dictionary<Type, object> services = new();
        private readonly List<IDisposable> disposables = new();

        public void Register<TService>(TService instance) where TService : class
        {
            Register(typeof(TService), instance);
        }

        public void Register(Type serviceType, object instance)
        {
            if (serviceType == null)
                throw new ArgumentNullException(nameof(serviceType));
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            if (services.TryGetValue(serviceType, out object previous) && !ReferenceEquals(previous, instance))
                UnityEngine.Debug.LogWarning($"[GameLib] Service {serviceType.Name} re-registered: {previous.GetType().Name} -> {instance.GetType().Name}. Check installer order / duplicates.");

            services[serviceType] = instance;
            if (instance is IDisposable disposable)
                RegisterDisposable(disposable);
        }

        public void RegisterDisposable(IDisposable disposable)
        {
            if (disposable == null)
                throw new ArgumentNullException(nameof(disposable));
            if (!disposables.Contains(disposable))
                disposables.Add(disposable);
        }

        public bool IsRegistered<TService>() where TService : class =>
            services.ContainsKey(typeof(TService));

        public void Dispose()
        {
            // Reverse registration order: later services may depend on earlier ones.
            for (int i = disposables.Count - 1; i >= 0; i--)
            {
                try { disposables[i].Dispose(); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }
            disposables.Clear();
            services.Clear();
        }

        public bool TryResolve<TService>(out TService service) where TService : class
        {
            if (services.TryGetValue(typeof(TService), out object instance))
            {
                service = instance as TService;
                return service != null;
            }

            service = null;
            return false;
        }

        public TService Resolve<TService>() where TService : class
        {
            if (TryResolve(out TService service))
            {
                return service;
            }

            throw new InvalidOperationException(
                $"Service not registered: {typeof(TService).FullName}. " +
                "Install the module and add its asset in GameLib > Setup, or use TryResolve for optional modules.");
        }
    }
}
