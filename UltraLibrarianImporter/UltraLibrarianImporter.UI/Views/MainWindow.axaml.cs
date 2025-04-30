using System.IO;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.ViewModels;

using WebViewControl;

namespace UltraLibrarianImporter.UI.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel ViewModel => (MainWindowViewModel) DataContext;

        public MainWindow()
        {
          


            InitializeComponent();



            // Get the WebView control and set up event handlers
            var webView = this.FindControl<WebView>("OSWebView");
            webView.DownloadCompleted += DownloadComplete;
            webView.Loaded += (s, e) =>
            {

                ViewModel.SetWebViewLoaded(true);
            };
        }

        private void DownloadComplete(string resourcePath)
        {
            if (resourcePath.EndsWith(".zip"))
                ViewModel.OnLibraryDowloaded(resourcePath);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}