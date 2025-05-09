using System;

using Avalonia;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

namespace UltraLibrarianImporter.UI.ViewModels
{
    [ObservableObject]
    public partial class ViewModelBase : IDisposable
    {

        protected void RunOnUIThread(Action action)
        {
            Dispatcher.UIThread.Invoke(action);
        }
        public virtual void Dispose()
        {
            // Base implementation does nothing
        }
    }
}
