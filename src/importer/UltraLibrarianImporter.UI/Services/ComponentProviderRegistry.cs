using System;
using System.Collections.Generic;
using System.Linq;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services
{
    public class ComponentProviderRegistry : IComponentProviderRegistry
    {
        private readonly List<IComponentProvider> _providers;
        private IComponentProvider _selectedProvider;

        public event Action<IComponentProvider>? ProviderChanged;

        public ComponentProviderRegistry(IEnumerable<IComponentProvider> providers)
        {
            _providers = providers.ToList();
            if (_providers.Count == 0)
            {
                throw new InvalidOperationException("At least one component provider must be registered.");
            }

            _selectedProvider = _providers[0];
        }

        public IReadOnlyList<IComponentProvider> Providers => _providers;

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
            _providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
