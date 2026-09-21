using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UltraLibrarianImporter.UI.Services;
using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.ViewModels;

using WebViewControl;

using Xilium.CefGlue;
using Xilium.CefGlue.Common;
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

        WebView.GlobalWebViewInitialized += Initialize;

        InitializeComponent();

        //// Get the WebView control and set up event handlers
        WebView? webView = this.FindControl<WebView>("OSWebView");
        if (webView != null)
        {
            webView.Loaded += (s, e) =>
            {
                ViewModel.SetWebViewLoaded(true);
            };
            webView.DownloadCompleted += DownloadComplete;
        }

        DataContextChanged += (s, e) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.RequestBrowserBack += () =>
                {
                    WebView? wv = this.FindControl<WebView>("OSWebView");
                    if (wv != null)
                    {
                        try { wv.GoBack(); } catch { }
                    }
                };
                vm.RequestBrowserForward += () =>
                {
                    WebView? wv = this.FindControl<WebView>("OSWebView");
                    if (wv != null)
                    {
                        try { wv.GoForward(); } catch { }
                    }
                };
                vm.RequestBrowserReload += () =>
                {
                    WebView? wv = this.FindControl<WebView>("OSWebView");
                    if (wv != null)
                    {
                        try { wv.Reload(); } catch { }
                    }
                };
                vm.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.WebviewUrl))
                    {
                        WebView? wv = this.FindControl<WebView>("OSWebView");
                        if (wv != null && !string.IsNullOrEmpty(vm.WebviewUrl) && wv.Address != vm.WebviewUrl)
                        {
                            wv.Address = vm.WebviewUrl;
                        }
                    }
                };
            }
        };
    }

    private void Initialize(WebView view)
    {
        var browser = (BaseCefBrowser)view.GetVisualChildren().First();
        browser.DownloadHandler = new InternalDownloadHandler(this);
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

    // The only route from the browser to an import. WebView.DownloadCompleted, also wired to
    // DownloadComplete, is raised only by the WebView's own download handler, and Initialize replaces
    // that handler with this one while the WebView is still being constructed.
    private class InternalDownloadHandler : DownloadHandler
    {
        private readonly MainWindow _mainWindow;

        // The downloads this handler continued, by CEF download id, with the path each was given. Only
        // these reach the view model (#97). Both collections are touched only by OnBeforeDownload and
        // OnDownloadUpdated, which CEF calls on its browser-process UI thread.
        private readonly Dictionary<uint, string> _acceptedDownloads = [];

        // The downloads this handler refused, until CEF reports them canceled.
        private readonly HashSet<uint> _refusedDownloads = [];

        public InternalDownloadHandler(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
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
                downloadDir = !string.IsNullOrWhiteSpace(_mainWindow.ViewModel.DownloadDirectory)
                    ? _mainWindow.ViewModel.DownloadDirectory
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



internal class InternalResourceRequestHandler : CefResourceRequestHandler
{
    private readonly CefResourceRequestHandler _originalResourceRequestHandler;
    private readonly MethodInfo _getResourceHandler;

    public InternalResourceRequestHandler(CefResourceRequestHandler originalResourceRequestHandler)
    {
        _originalResourceRequestHandler = originalResourceRequestHandler;
        _getResourceHandler = _originalResourceRequestHandler.GetType().GetMethod(nameof(GetResourceHandler), BindingFlags.NonPublic | BindingFlags.Instance)!;
    }

    protected override CefCookieAccessFilter GetCookieAccessFilter(CefBrowser browser, CefFrame frame, CefRequest request)
    {
        return new CookiesAccessFilter();
    }

    protected override CefResourceHandler GetResourceHandler(CefBrowser browser, CefFrame frame, CefRequest request)
    {
        return (CefResourceHandler)_getResourceHandler.Invoke(_originalResourceRequestHandler, [browser, frame, request])!;
    }

}

internal class InternalRequestHandler : RequestHandler
{
    private readonly Lazy<HttpResourceRequestHandler> HttpResourceRequestHandler = new(() => new HttpResourceRequestHandler());
    private WebView OwnerWebView { get; }

    private InternalResourceRequestHandler ResourceRequestHandler { get; }

    private readonly RequestHandler _originalRequestHandler;
    private readonly MethodInfo _originalGetAuthCredentials;
    private readonly MethodInfo _onBeforeBrowse;
    private readonly MethodInfo _onCertificateError;
    private readonly MethodInfo _onRenderProcessTerminated;

    public InternalRequestHandler(WebView webView, RequestHandler originalRequestHandler)
    {
        _originalRequestHandler = originalRequestHandler;
        OwnerWebView = webView;

        Type type = originalRequestHandler.GetType();
        PropertyInfo property = type.GetProperty(nameof(ResourceRequestHandler), BindingFlags.NonPublic | BindingFlags.GetProperty | BindingFlags.Instance)!;
        var originalResourceRequestHandler = (CefResourceRequestHandler)property.GetValue(originalRequestHandler)!;
        ResourceRequestHandler = new InternalResourceRequestHandler(originalResourceRequestHandler);


        _originalGetAuthCredentials = type.GetMethod(nameof(GetAuthCredentials), BindingFlags.NonPublic | BindingFlags.Instance)!;
        _onBeforeBrowse = type.GetMethod(nameof(OnBeforeBrowse), BindingFlags.NonPublic | BindingFlags.Instance)!;
        _onCertificateError = type.GetMethod(nameof(OnCertificateError), BindingFlags.NonPublic | BindingFlags.Instance)!;
        _onRenderProcessTerminated = type.GetMethod(nameof(OnRenderProcessTerminated), BindingFlags.NonPublic | BindingFlags.Instance)!;
    }

    protected override bool GetAuthCredentials(CefBrowser browser, string originUrl, bool isProxy, string host, int port, string realm, string scheme, CefAuthCallback callback)
    {
        var result = _originalGetAuthCredentials.Invoke(_originalRequestHandler, [originUrl, isProxy, host, port, realm, scheme, callback])!;
        return (bool)result;
    }

    protected override bool OnBeforeBrowse(CefBrowser browser, CefFrame frame, CefRequest request, bool userGesture, bool isRedirect)
    {
        return (bool)_onBeforeBrowse.Invoke(_originalRequestHandler, [browser, frame, request, userGesture, isRedirect])!;
    }

    protected override bool OnCertificateError(CefBrowser browser, CefErrorCode certError, string requestUrl, CefSslInfo sslInfo, CefCallback callback)
    {
        return (bool)_onCertificateError.Invoke(_originalRequestHandler, [browser, certError, requestUrl, sslInfo, callback])!;
    }

    protected override void OnRenderProcessTerminated(CefBrowser browser, CefTerminationStatus status)
    {
        _ = _onRenderProcessTerminated.Invoke(_originalRequestHandler, [browser, status]);
    }

    protected override CefResourceRequestHandler GetResourceRequestHandler(CefBrowser browser, CefFrame frame, CefRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling)
    {
        if (OwnerWebView.IsSecurityDisabled && HttpResourceHandler.AcceptedResources.Contains(request.ResourceType) && request.Url != null)
        {
            var uri = new Uri(request.Url);
            if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            {
                return HttpResourceRequestHandler.Value;
            }
        }

        return ResourceRequestHandler;
    }
}

internal class HttpResourceRequestHandler : CefResourceRequestHandler
{
    protected override CefCookieAccessFilter GetCookieAccessFilter(CefBrowser browser, CefFrame frame, CefRequest request)
    {
        return new CookiesAccessFilter();
    }

    protected override CefResourceHandler GetResourceHandler(CefBrowser browser, CefFrame frame, CefRequest request)
    {
        return new HttpResourceHandler();
    }
}


internal static class UrlHelper
{
    public const string AboutBlankUrl = "about:blank";

    public static ResourceUrl DefaultLocalUrl = new("local", "index.html");

    public static bool IsChromeInternalUrl(string url)
    {
        return url?.StartsWith("devtools:", StringComparison.InvariantCultureIgnoreCase) ?? false;
    }

    public static bool IsInternalUrl(string url)
    {
        return IsChromeInternalUrl(url) || url.StartsWith(DefaultLocalUrl.ToString(), StringComparison.InvariantCultureIgnoreCase);
    }

    public static void OpenInExternalBrowser(string url)
    {
        _ = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Process.Start("explorer", "\"" + url + "\"")
            : Process.Start("open", url);
    }
}

internal class CookiesAccessFilter : CefCookieAccessFilter
{
    protected override bool CanSaveCookie(CefBrowser browser, CefFrame frame, CefRequest request, CefResponse response, CefCookie cookie)
    {
        return true;
    }

    protected override bool CanSendCookie(CefBrowser browser, CefFrame frame, CefRequest request, CefCookie cookie)
    {
        return true;
    }
}

internal class HttpResourceHandler : DefaultResourceHandler
{

    // A single client for every proxied request. HttpClient is intended to be long-lived; a new
    // one per request leaks sockets in TIME_WAIT, which is exactly the failure mode the obsolete
    // HttpWebRequest API used to hide.
    private static readonly HttpClient SharedHttpClient = new();

    internal static readonly CefResourceType[] AcceptedResources = new CefResourceType[3]
    {
    CefResourceType.SubFrame,
    CefResourceType.FontResource,
    CefResourceType.Stylesheet
    };

    protected override RequestHandlingFashion ProcessRequestAsync(CefRequest request, CefCallback callback)
    {
        _ = Task.Run(async delegate
        {
            try
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.Url);
                NameValueCollection headerMap = request.GetHeaderMap();
                // AllKeys is string?[]: a NameValueCollection may hold one null-keyed entry.
                foreach (var name in headerMap.AllKeys)
                {
                    if (name is null)
                    {
                        continue;
                    }

                    _ = httpRequest.Headers.TryAddWithoutValidation(name, headerMap[name]);
                }

                HttpResponseMessage httpResponse = await SharedHttpClient.SendAsync(
                    httpRequest, HttpCompletionOption.ResponseHeadersRead);

                var responseHeaders = new WebHeaderCollection();
                foreach (KeyValuePair<string, IEnumerable<string>> pair in httpResponse.Headers)
                {
                    responseHeaders.Add(pair.Key, string.Join(", ", pair.Value));
                }

                foreach (KeyValuePair<string, IEnumerable<string>> pair in httpResponse.Content.Headers)
                {
                    responseHeaders.Add(pair.Key, string.Join(", ", pair.Value));
                }

                Response = await httpResponse.Content.ReadAsStreamAsync();
                Headers = responseHeaders;
                MimeType = httpResponse.Content.Headers.ContentType?.MediaType;
                Status = (int)httpResponse.StatusCode;
                StatusText = httpResponse.ReasonPhrase;
                Headers.Remove("Access-Control-Allow-Origin");
                Headers.Add("Access-Control-Allow-Origin", "*");
            }
            catch
            {
            }
            finally
            {
                callback.Continue();
            }
        });
        return RequestHandlingFashion.ContinueAsync;
    }

    protected override bool Read(Stream outResponse, int bytesToRead, out int bytesRead, CefResourceReadCallback callback)
    {
        var array = new byte[bytesToRead];
        bytesRead = Response?.Read(array, 0, array.Length) ?? 0;
        if (bytesRead == 0)
        {
            return false;
        }

        outResponse.Write(array, 0, bytesRead);
        return bytesRead > 0;
    }
}
