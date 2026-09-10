using System;
using System.Collections.Generic;
using System.Linq;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services
{
    public class ComponentProviderRegistry : IComponentProviderRegistry
    {
        private readonly List<IComponentProvider> _allProviders;
        private readonly IConfigService? _configService;
        private IComponentProvider _selectedProvider;

        public event Action<IComponentProvider>? ProviderChanged;
        public event Action? RegistryUpdated;

        public ComponentProviderRegistry(IEnumerable<IComponentProvider> providers, IConfigService? configService = null)
        {
            _allProviders = providers.ToList();
            if (_allProviders.Count == 0)
            {
                throw new InvalidOperationException("At least one component provider must be registered.");
            }

            _configService = configService;
            _selectedProvider = ResolveInitialProvider();
        }

        private IComponentProvider ResolveInitialProvider()
        {
            var enabled = EnabledProvidersList;
            if (_configService != null && !string.IsNullOrEmpty(_configService.DefaultProviderId))
            {
                var def = enabled.FirstOrDefault(p => string.Equals(p.Id, _configService.DefaultProviderId, StringComparison.OrdinalIgnoreCase));
                if (def != null) return def;
            }
            return enabled.FirstOrDefault() ?? _allProviders[0];
        }

        private List<IComponentProvider> EnabledProvidersList
        {
            get
            {
                if (_configService == null) return _allProviders;
                var list = _allProviders.Where(p => _configService.IsProviderEnabled(p.Id)).ToList();
                return list.Count > 0 ? list : _allProviders;
            }
        }

        public IReadOnlyList<IComponentProvider> Providers => EnabledProvidersList;
        public IReadOnlyList<IComponentProvider> AllProviders => _allProviders;

        public IComponentProvider SelectedProvider
        {
            get => _selectedProvider;
            set
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                if (!ReferenceEquals(_selectedProvider, value))
                {
                    _selectedProvider = value;
                    ProviderChanged?.Invoke(_selectedProvider);
                }
            }
        }

        public IComponentProvider? GetProvider(string id) =>
            _allProviders.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        public void RefreshProviders()
        {
            var enabled = EnabledProvidersList;
            if (!enabled.Contains(_selectedProvider))
            {
                SelectedProvider = enabled.FirstOrDefault() ?? _allProviders[0];
            }
            RegistryUpdated?.Invoke();
        }
    }
}
