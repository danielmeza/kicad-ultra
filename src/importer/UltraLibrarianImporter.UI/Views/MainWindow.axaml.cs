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
    }

    private void Initialize(WebView view)
    {
        var browser = (BaseCefBrowser)view.GetVisualChildren().First();
        browser.DownloadHandler = new InternalDownloadHandler(this);
    }

    private void DownloadComplete(string resourcePath)
    {
        if (resourcePath.EndsWith(".zip"))
            ViewModel.LibraryDownloaded(resourcePath);
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

        public InternalDownloadHandler(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        protected override void OnBeforeDownload(CefBrowser browser, CefDownloadItem downloadItem, string suggestedName, CefBeforeDownloadCallback callback)
        {
            var header = new ContentDisposition(downloadItem.ContentDisposition);
            // FileName is null when the server sends a Content-Disposition without a filename
            // parameter; fall back to the name CEF already suggested rather than crashing the
            // download inside Path.Combine.
            var fileName = string.IsNullOrEmpty(header.FileName) ? suggestedName : header.FileName;
            var filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UltralibrarianKicad", fileName);
            callback.Continue(filePath, false);
            _mainWindow.AsyncExecuteInUI(() => _mainWindow.DownloadStarted(filePath));
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
        return IsChromeInternalUrl(url)
            || url.StartsWith(DefaultLocalUrl.ToString(), StringComparison.InvariantCultureIgnoreCase);
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
