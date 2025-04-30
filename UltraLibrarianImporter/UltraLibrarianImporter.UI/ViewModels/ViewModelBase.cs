using System;

using CommunityToolkit.Mvvm.ComponentModel;

namespace UltraLibrarianImporter.UI.ViewModels
{
    [ObservableObject]
    public partial class ViewModelBase : IDisposable
    {
        public virtual void Dispose()
        {
            // Base implementation does nothing
        }
    }
}
