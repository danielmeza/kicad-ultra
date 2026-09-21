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
        IComponentProvider SelectedProvider { get; set; }
        event Action<IComponentProvider>? ProviderChanged;
        IComponentProvider? GetProvider(string id);
    }
}
