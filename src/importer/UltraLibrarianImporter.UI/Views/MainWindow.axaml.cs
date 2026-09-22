using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Mime;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using UltraLibrarianImporter.UI.Services;
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

    // Where the download handler reads the download folder (#108). Null only without DI.
    private readonly IConfigService? _configService;

    // For the XAML designer and App's fallback without DI. Without the configuration, browser
    // downloads go to the default download folder.
    public MainWindow()
        : this(null)
    {
    }

    public MainWindow(IConfigService? configService)
    {
        _configService = configService;

        InitializeComponent();

        // The browser is CefGlue's own control (#67). WebViewControl, which wrapped it until then, has
        // no Avalonia 12 build; this window only ever used it as a host for these handlers. Both are
        // set before the control is shown, so CEF never sees a browser without them.
        AvaloniaCefBrowser? browser = this.FindControl<AvaloniaCefBrowser>("OSWebView");
        if (browser != null)
        {
            browser.DownloadHandler = new InternalDownloadHandler(this, _configService);
            browser.LifeSpanHandler = new SameBrowserPopupHandler();
            browser.Loaded += (s, e) =>
            {
                ViewModel.SetWebViewLoaded(true);
            };
        }

        DataContextChanged += (s, e) =>
        {
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
                // WebviewUrl from here on (a provider switch, Open & Import, Find on Ultra Librarian).
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

    // The only route from the browser to an import: the browser control has no download handler of its
    // own, and the constructor gives it this one before it is shown.
    //
    // CEF calls OnBeforeDownload and OnDownloadUpdated on its browser-process UI thread, not
    // Avalonia's, so neither may touch the window or its view model there: DataContext throws "Call
    // from invalid thread", which is how the configured download folder came to be ignored (#108).
    // Anything for the UI goes through AsyncExecuteInUI.
    private class InternalDownloadHandler : DownloadHandler
    {
        private readonly MainWindow _mainWindow;

        // Plain data, so safe to read on CEF's thread. Settings saves into the same instance, so a
        // folder changed there applies to the next download.
        private readonly IConfigService? _configService;

        // The downloads this handler continued, by CEF download id, with the path each was given. Only
        // these reach the view model (#97). Both collections are touched only by OnBeforeDownload and
        // OnDownloadUpdated, which CEF calls on its browser-process UI thread.
        private readonly Dictionary<uint, string> _acceptedDownloads = [];

        // The downloads this handler refused, until CEF reports them canceled.
        private readonly HashSet<uint> _refusedDownloads = [];

        public InternalDownloadHandler(MainWindow mainWindow, IConfigService? configService)
        {
            _mainWindow = mainWindow;
            _configService = configService;
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

            // Resolved inside the try blocks: SpecialFolders.GetPath throws when there is no absolute
            // path, and an exception must not escape this CEF callback.
            static string DefaultDownloadDir() =>
                Path.Combine(SpecialFolders.GetPath(Environment.SpecialFolder.ApplicationData), "KiCadComponentDownloads");

            string downloadDir;
            try
            {
                // Read once, so the value checked is the value used.
                var configuredDir = _configService?.DownloadDirectory;
                downloadDir = !string.IsNullOrWhiteSpace(configuredDir)
                    ? configuredDir
                    : DefaultDownloadDir();
                _ = Directory.CreateDirectory(downloadDir);
            }
            catch
            {
                try
                {
                    downloadDir = DefaultDownloadDir();
                    _ = Directory.CreateDirectory(downloadDir);
                }
                catch
                {
                    Refuse(downloadItem.Id, callback, "the download folder could not be created");
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
                // Refuse rather than write outside target folder
                Refuse(downloadItem.Id, callback, "its file name would place it outside the download folder");
                return;
            }

            _acceptedDownloads[downloadItem.Id] = fullPath;
            callback.Continue(fullPath, false);
            _mainWindow.AsyncExecuteInUI(() => _mainWindow.DownloadStarted(fullPath));
        }

        // Refusing never calls Continue: with an empty path Continue does not refuse, it saves the file
        // under the server's name in CEF's temp directory (#97). Not continuing is not enough either:
        // CEF 120 leaves the download waiting for a path, its data in a temporary file, so
        // OnDownloadUpdated cancels it at its next update. The callback, never to be run, is released.
        private void Refuse(uint downloadId, CefBeforeDownloadCallback callback, string reason)
        {
            _ = _refusedDownloads.Add(downloadId);
            callback.Dispose();
            _mainWindow.AsyncExecuteInUI(() => _mainWindow.ViewModel.DownloadRefused(reason));
        }

        protected override void OnDownloadUpdated(CefBrowser browser, CefDownloadItem downloadItem, CefDownloadItemCallback callback)
        {
            if (_refusedDownloads.Contains(downloadItem.Id))
            {
                if (downloadItem.IsCanceled)
                {
                    _ = _refusedDownloads.Remove(downloadItem.Id);
                }
                else
                {
                    callback.Cancel();
                }

                return;
            }

            // CEF also reports a download before OnBeforeDownload has seen it. Only a download this
            // handler accepted reaches the view model.
            if (!_acceptedDownloads.TryGetValue(downloadItem.Id, out var acceptedPath))
            {
                return;
            }

            var receivedBytes = downloadItem.ReceivedBytes;
            var totalBytes = downloadItem.TotalBytes;
            var percentageComplete = downloadItem.PercentComplete;
            if (downloadItem.IsComplete)
            {
                // Forgotten at once, so a later update of the finished download cannot import it again.
                _ = _acceptedDownloads.Remove(downloadItem.Id);

                // Import the file at the path that passed the containment check, and only if that is
                // where CEF says it saved it.
                if (!string.Equals(downloadItem.FullPath, acceptedPath, StringComparison.Ordinal))
                {
                    _mainWindow.AsyncExecuteInUI(() => _mainWindow.ViewModel.DownloadRefused(
                        "it was not saved where it was accepted, so it will not be imported"));
                    return;
                }

                _mainWindow.AsyncExecuteInUI(delegate
                {
                    _mainWindow.DownloadComplete(acceptedPath);
                });
            }
            else if (downloadItem.IsCanceled || downloadItem.IsInterrupted)
            {
                _mainWindow.AsyncExecuteInUI(delegate
                {
                    _mainWindow.DownloadCancelled(acceptedPath);
                });
            }
            else
            {
                _mainWindow.AsyncExecuteInUI(delegate
                {
                    _mainWindow.DownloadProgressChanged(acceptedPath, receivedBytes, totalBytes, percentageComplete);
                });
            }
        }


    }

    private void DownloadStarted(string filePath)
    {
        ViewModel.DownloadStarted(filePath);
    }

    // Called on CEF's thread. IsLoaded is the window's own state, so it is checked only once the
    // action is on Avalonia's UI thread.
    private void AsyncExecuteInUI(Action action)
    {
        _ = Dispatcher.UIThread.InvokeAsync(delegate
        {
            if (IsLoaded)
            {
                action();
            }
        }, DispatcherPriority.Normal);
    }

}
