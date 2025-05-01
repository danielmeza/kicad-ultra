using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

using UltraLibrarianImporter.UI.ViewModels;

using WebViewControl;

using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Handlers;

namespace UltraLibrarianImporter.UI.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

        public MainWindow()
        {

            WebView.GlobalWebViewInitialized += Initialize;

            InitializeComponent();

            //// Get the WebView control and set up event handlers
            var webView = this.FindControl<WebView>("OSWebView");
            if (webView != null)
            {
                webView.ShowDeveloperTools();
                webView.Loaded += (s, e) =>
                {
                    ViewModel.SetWebViewLoaded(true);
                };
            }
        }

        private void Initialize(WebView view)
        {
            view.DownloadCompleted += DownloadComplete;
            var browser =  (BaseCefBrowser)view.GetVisualChildren().First();
            browser.DownloadHandler = new InternalDownloadHandler(browser.DownloadHandler);
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


    internal class InternalDownloadHandler : DownloadHandler
    {
        private CefDownloadHandler _originalHandler;
        private MethodInfo _onDownloadUpdated;


        public InternalDownloadHandler(CefDownloadHandler originalHandler)
        {
            _originalHandler = originalHandler;
            _onDownloadUpdated = originalHandler.GetType().GetMethod(nameof(OnDownloadUpdated), BindingFlags.NonPublic | BindingFlags.Instance)!;
        }

        protected override void OnBeforeDownload(CefBrowser browser, CefDownloadItem downloadItem, string suggestedName, CefBeforeDownloadCallback callback)
        {
            var header = new ContentDisposition(downloadItem.ContentDisposition);
            var filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UltralibrarianKicad", header.FileName);
            callback.Continue(filePath, false);
        }

        protected override void OnDownloadUpdated(CefBrowser browser, CefDownloadItem downloadItem, CefDownloadItemCallback callback)
        {
            _onDownloadUpdated.Invoke(_originalHandler, [browser, downloadItem, callback]);
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
        private Lazy<HttpResourceRequestHandler> HttpResourceRequestHandler = new Lazy<HttpResourceRequestHandler>(() => new HttpResourceRequestHandler());
        private WebView OwnerWebView { get; }

        private InternalResourceRequestHandler ResourceRequestHandler { get; }

        private RequestHandler _originalRequestHandler;
        private MethodInfo _originalGetAuthCredentials;
        private MethodInfo _onBeforeBrowse;
        private MethodInfo _onCertificateError;
        private MethodInfo _onRenderProcessTerminated;

        public InternalRequestHandler(WebView webView, RequestHandler originalRequestHandler)
        {
            _originalRequestHandler = originalRequestHandler;
            OwnerWebView = webView;

            var type = originalRequestHandler.GetType();
            var property = type.GetProperty(nameof(ResourceRequestHandler), BindingFlags.NonPublic | BindingFlags.GetProperty | BindingFlags.Instance)!;
            var originalResourceRequestHandler = (CefResourceRequestHandler)property.GetValue(originalRequestHandler)!;
            ResourceRequestHandler = new InternalResourceRequestHandler(originalResourceRequestHandler);


            _originalGetAuthCredentials = type.GetMethod(nameof(GetAuthCredentials), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            _onBeforeBrowse = type.GetMethod(nameof(OnBeforeBrowse), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            _onCertificateError = type.GetMethod(nameof(OnCertificateError), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            _onRenderProcessTerminated = type.GetMethod(nameof(OnRenderProcessTerminated), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
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
            _onRenderProcessTerminated.Invoke(_originalRequestHandler, [browser, status]);
        }

        protected override CefResourceRequestHandler GetResourceRequestHandler(CefBrowser browser, CefFrame frame, CefRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling)
        {
            if (OwnerWebView.IsSecurityDisabled && HttpResourceHandler.AcceptedResources.Contains(request.ResourceType) && request.Url != null)
            {
                Uri uri = new Uri(request.Url);
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
        private const string ChromeInternalProtocol = "devtools:";

        public const string AboutBlankUrl = "about:blank";

        public static ResourceUrl DefaultLocalUrl = new ResourceUrl("local", "index.html");

        public static bool IsChromeInternalUrl(string url)
        {
            return url?.StartsWith("devtools:", StringComparison.InvariantCultureIgnoreCase) ?? false;
        }

        public static bool IsInternalUrl(string url)
        {
            if (!IsChromeInternalUrl(url))
            {
                return url.StartsWith(DefaultLocalUrl.ToString(), StringComparison.InvariantCultureIgnoreCase);
            }

            return true;
        }

        public static void OpenInExternalBrowser(string url)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("explorer", "\"" + url + "\"");
            }
            else
            {
                Process.Start("open", url);
            }
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
        private const string AccessControlAllowOriginHeaderKey = "Access-Control-Allow-Origin";

        internal static readonly CefResourceType[] AcceptedResources = new CefResourceType[3]
        {
        CefResourceType.SubFrame,
        CefResourceType.FontResource,
        CefResourceType.Stylesheet
        };

        protected override RequestHandlingFashion ProcessRequestAsync(CefRequest request, CefCallback callback)
        {
            Task.Run(async delegate
            {
                try
                {
                    HttpWebRequest httpWebRequest = WebRequest.CreateHttp(request.Url);
                    NameValueCollection headerMap = request.GetHeaderMap();
                    string[] allKeys = headerMap.AllKeys;
                    foreach (string name in allKeys)
                    {
                        httpWebRequest.Headers.Add(name, headerMap[name]);
                    }

                    HttpWebResponse httpWebResponse = (HttpWebResponse)(await httpWebRequest.GetResponseAsync());
                    base.Response = httpWebResponse.GetResponseStream();
                    base.Headers = httpWebResponse.Headers;
                    base.MimeType = httpWebResponse.ContentType;
                    base.Status = (int)httpWebResponse.StatusCode;
                    base.StatusText = httpWebResponse.StatusDescription;
                    base.Headers.Remove("Access-Control-Allow-Origin");
                    base.Headers.Add("Access-Control-Allow-Origin", "*");
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
            byte[] array = new byte[bytesToRead];
            bytesRead = base.Response?.Read(array, 0, array.Length) ?? 0;
            if (bytesRead == 0)
            {
                return false;
            }

            outResponse.Write(array, 0, bytesRead);
            return bytesRead > 0;
        }
    }
}