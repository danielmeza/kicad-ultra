using System;
using System.Collections.Generic;

namespace UltraLibrarianImporter.UI.Services.Interfaces
{
    /// <summary>
    /// Registry for managing available component providers and tracking the active one.
    /// </summary>
    public interface IComponentProviderRegistry
    {
        IReadOnlyList<IComponentProvider> Providers { get; }
        IReadOnlyList<IComponentProvider> AllProviders { get; }
        IComponentProvider SelectedProvider { get; set; }
        event Action<IComponentProvider>? ProviderChanged;
        event Action? RegistryUpdated;
        IComponentProvider? GetProvider(string id);
        void RefreshProviders();
    }
}
