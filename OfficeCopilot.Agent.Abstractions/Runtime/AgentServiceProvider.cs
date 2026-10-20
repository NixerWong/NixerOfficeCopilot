using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Abstractions
{
    public sealed class AgentServiceProvider : IServiceProvider
    {
        private readonly object _syncRoot;
        private readonly Dictionary<Type, object> _services;
        private readonly IServiceProvider _fallbackServiceProvider;

        public AgentServiceProvider()
            : this(null)
        {
        }

        public AgentServiceProvider(IServiceProvider fallbackServiceProvider)
        {
            _syncRoot = new object();
            _services = new Dictionary<Type, object>();
            _fallbackServiceProvider = fallbackServiceProvider;
        }

        public object GetService(Type serviceType)
        {
            if (serviceType == null)
            {
                return null;
            }

            lock (_syncRoot)
            {
                object service;
                if (_services.TryGetValue(serviceType, out service))
                {
                    return service;
                }

                foreach (var pair in _services)
                {
                    if (pair.Key != null && serviceType.IsAssignableFrom(pair.Key))
                    {
                        return pair.Value;
                    }

                    if (pair.Value != null && serviceType.IsInstanceOfType(pair.Value))
                    {
                        return pair.Value;
                    }
                }
            }

            return _fallbackServiceProvider == null
                ? null
                : _fallbackServiceProvider.GetService(serviceType);
        }

        public void AddService<T>(T service) where T : class
        {
            AddService(typeof(T), service);
        }

        public void AddService(Type serviceType, object service)
        {
            if (serviceType == null)
            {
                throw new ArgumentNullException(nameof(serviceType));
            }

            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (!serviceType.IsInstanceOfType(service))
            {
                throw new ArgumentException("服务实例类型与服务声明类型不兼容。", nameof(service));
            }

            lock (_syncRoot)
            {
                _services[serviceType] = service;
            }
        }

        public bool RemoveService<T>() where T : class
        {
            return RemoveService(typeof(T));
        }

        public bool RemoveService(Type serviceType)
        {
            if (serviceType == null)
            {
                return false;
            }

            lock (_syncRoot)
            {
                return _services.Remove(serviceType);
            }
        }

        public bool TryGetService<T>(out T service) where T : class
        {
            service = GetService(typeof(T)) as T;
            return service != null;
        }
    }
}