using System;
using System.IO;
using System.Net.Mime;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.ViewModels;

using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common.Handlers;

namespace UltraLibrarianImporter.UI.Views;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel =>
        DataContext as MainViewModel
        ?? throw new InvalidOperationException(
            "MainWindow was used before its MainViewModel DataContext was assigned.");

    public MainWindow()
    {
        InitializeComponent();

        // The browser is CefGlue's own control (#67). WebViewControl, which wrapped it until then, has
        // no Avalonia 12 build; this window only ever used it as a host for these handlers.
        var downloadHandler = new InternalDownloadHandler(this);
        AvaloniaCefBrowser? browser = this.FindControl<AvaloniaCefBrowser>("OSWebView");
        if (browser != null)
        {
            browser.DownloadHandler = downloadHandler;
            browser.LifeSpanHandler = new SameBrowserPopupHandler();
            browser.Loaded += (s, e) =>
            {
                ViewModel.SetWebViewLoaded(true);
            };
        }

        DataContextChanged += (s, e) =>
        {
            downloadHandler.ViewModel = DataContext as MainViewModel;
            if (DataContext is MainViewModel vm)
            {
                vm.RequestBrowserBack += () =>
                {
                    AvaloniaCefBrowser? wv = this.FindControl<AvaloniaCefBrowser>("OSWebView");
                    if (wv != null)
                    {
                        try { wv.GoBack(); } catch { }
                    }
                };
                vm.RequestBrowserForward += () =>
                {
                    AvaloniaCefBrowser? wv = this.FindControl<AvaloniaCefBrowser>("OSWebView");
                    if (wv != null)
                    {
                        try { wv.GoForward(); } catch { }
                    }
                };
                vm.RequestBrowserReload += () =>
                {
                    AvaloniaCefBrowser? wv = this.FindControl<AvaloniaCefBrowser>("OSWebView");
                    if (wv != null)
                    {
                        try { wv.Reload(); } catch { }
                    }
                };

                // AvaloniaCefBrowser.Address is a CLR property, not an Avalonia one, so it cannot be
                // bound in XAML the way WebViewControl's was: set the provider's page now, and follow
                // WebviewUrl from here on.
                NavigateTo(vm.WebviewUrl);
                vm.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.WebviewUrl))
                    {
                        NavigateTo(vm.WebviewUrl);
                    }
                };
            }
        };
    }

    private void NavigateTo(string url)
    {
        AvaloniaCefBrowser? wv = this.FindControl<AvaloniaCefBrowser>("OSWebView");
        if (wv != null && !string.IsNullOrEmpty(url) && wv.Address != url)
        {
            wv.Address = url;
        }
    }

    // A page's request for a new window (target="_blank", window.open) opens in this browser instead.
    // WebViewControl handed such links to the system browser, where a download never reaches
    // InternalDownloadHandler (a JLCPCB datasheet link went to xdg-open); CEF's own default is a
    // separate, unmanaged native window. In this browser the page keeps the download handler and the
    // Back button.
    private sealed class SameBrowserPopupHandler : LifeSpanHandler
    {
        protected override bool OnBeforePopup(CefBrowser browser, CefFrame frame, string targetUrl, string targetFrameName, CefWindowOpenDisposition targetDisposition, bool userGesture, CefPopupFeatures popupFeatures, CefWindowInfo windowInfo, ref CefClient client, CefBrowserSettings settings, ref CefDictionaryValue extraInfo, ref bool noJavascriptAccess)
        {
            if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out Uri? target)
                || (target.Scheme != Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttp))
            {
                // about:blank and friends: let CEF open its window, as it did before.
                return false;
            }

            browser.GetMainFrame().LoadUrl(target.AbsoluteUri);
            return true;
        }
    }

    private void DownloadComplete(string resourcePath)
    {
        IComponentProvider provider = ViewModel.SelectedProvider;
        if (provider != null ? provider.CanHandleDownload(resourcePath) : resourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ViewModel.LibraryDownloaded(resourcePath);
        }
    }


    private void DownloadProgressChanged(string fullPath, long receivedBytes, long totalBytes, int percentComplete)
    {
        ViewModel.ReportDownloadProgressChanged(fullPath, receivedBytes, totalBytes, percentComplete);
    }

    private void DownloadCancelled(string fullPath)
    {
        ViewModel.DownloadCancelled(fullPath);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    private class InternalDownloadHandler : DownloadHandler
    {
        private readonly MainWindow _mainWindow;
        private volatile MainViewModel? _viewModel;

        public InternalDownloadHandler(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        // CEF calls this handler on its own UI thread, which is not Avalonia's, so it must not read
        // the window's DataContext: that is an Avalonia property, and reading one off Avalonia's UI
        // thread throws - an unhandled exception on a CEF thread, which aborted the process the
        // moment a download started (#67). The window hands the view model over instead, on its UI
        // thread, whenever the DataContext changes.
        public MainViewModel? ViewModel
        {
            set => _viewModel = value;
        }

        protected override void OnBeforeDownload(CefBrowser browser, CefDownloadItem downloadItem, string suggestedName, CefBeforeDownloadCallback callback)
        {
            var candidateName = suggestedName;
            if (!string.IsNullOrWhiteSpace(downloadItem.ContentDisposition))
            {
                try
                {
                    var header = new ContentDisposition(downloadItem.ContentDisposition);
                    if (!string.IsNullOrWhiteSpace(header.FileName))
                    {
                        candidateName = header.FileName;
                    }
                }
                catch
                {
                    // Fall back to suggestedName on malformed Content-Disposition header
                }
            }

            var safeName = Path.GetFileName(candidateName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = $"download-{Guid.NewGuid():N}.zip";
            }

            var defaultDownloadDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KiCadComponentDownloads");
            var configuredDir = _viewModel?.DownloadDirectory;
            var downloadDir = !string.IsNullOrWhiteSpace(configuredDir)
                ? configuredDir
                : defaultDownloadDir;

            try
            {
                _ = Directory.CreateDirectory(downloadDir);
            }
            catch
            {
                downloadDir = defaultDownloadDir;
                try
                {
                    _ = Directory.CreateDirectory(downloadDir);
                }
                catch
                {
                    callback.Continue(string.Empty, false);
                    return;
                }
            }

            var fullPath = Path.GetFullPath(Path.Combine(downloadDir, safeName));
            var rootPath = Path.GetFullPath(downloadDir);
            if (!rootPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                rootPath += Path.DirectorySeparatorChar;
            }

            if (!fullPath.StartsWith(rootPath, StringComparison.Ordinal))
            {
                callback.Continue(string.Empty, false); // Refuse rather than write outside target folder
                return;
            }

            callback.Continue(fullPath, false);
            _mainWindow.AsyncExecuteInUI(() => _mainWindow.DownloadStarted(fullPath));
        }

        protected override void OnDownloadUpdated(CefBrowser browser, CefDownloadItem downloadItem, CefDownloadItemCallback callback)
        {

            var fullPath = downloadItem.FullPath;
            var receivedBytes = downloadItem.ReceivedBytes;
            var totalBytes = downloadItem.TotalBytes;
            var percentageComplete = downloadItem.PercentComplete;
            if (downloadItem.IsComplete)
            {
                _mainWindow.AsyncExecuteInUI(delegate
                {
                    _mainWindow.DownloadComplete(fullPath);
                });
            }
            else if (downloadItem.IsCanceled || downloadItem.IsInterrupted)
            {
                _mainWindow.AsyncExecuteInUI(delegate
                {
                    _mainWindow.DownloadCancelled(fullPath);
                });
            }
            else
            {
                _mainWindow.AsyncExecuteInUI(delegate
                {
                    _mainWindow.DownloadProgressChanged(fullPath, receivedBytes, totalBytes, percentageComplete);
                });
            }
        }


    }

    private void DownloadStarted(string filePath)
    {
        ViewModel.DownloadStarted(filePath);
    }

    private void AsyncExecuteInUI(Action action)
    {
        if (!IsLoaded)
        {
            return;
        }

        _ = Dispatcher.UIThread.InvokeAsync(delegate
        {
            if (IsLoaded)
            {
                action();
            }
        }, DispatcherPriority.Normal);
    }

}
