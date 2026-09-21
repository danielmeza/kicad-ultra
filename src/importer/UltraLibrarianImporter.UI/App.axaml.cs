using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.ViewModels;
using UltraLibrarianImporter.UI.Views;

using Xilium.CefGlue;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Shared;

namespace UltraLibrarianImporter.UI;

public partial class App : Application
{
    public readonly IServiceProvider _serviceProvider;
    private readonly ILogger<App> _logger;

    private static string[] CustomSchemes { get; } = new string[5]
    {
            "local",
            "embedded",
            "custom",
            Uri.UriSchemeHttp,
            Uri.UriSchemeHttps
    };

    public App(IServiceProvider? serviceProvider = null)
    {
        // The optional parameter exists so the Avalonia XAML designer can construct App with no DI
        // container. Every real code path supplies one, and the previous body dereferenced it
        // unconditionally, so a null already crashed here - with a NullReferenceException that named
        // nothing. Fail explicitly instead.
        _serviceProvider = serviceProvider
            ?? throw new ArgumentNullException(nameof(serviceProvider),
                "App requires the dependency-injection container built in Program.Main.");
        _logger = _serviceProvider.GetRequiredService<ILogger<App>>();
        _logger.LogInformation("Application starting up");
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        // Avalonia 12's developer tools (AvaloniaUI.DiagnosticsSupport, a Debug-only reference). F12
        // connects to them; Avalonia.Diagnostics' AttachDevTools, which this replaces, has no 12.x.
        _ = this.AttachDeveloperTools();
#endif
    }

    /// <summary>
    /// The application's main window. Null until <see cref="OnFrameworkInitializationCompleted"/>
    /// has run, which is why it is declared nullable rather than silenced with <c>null!</c>.
    /// </summary>
    public static MainWindow? MainWindow { get; private set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // No DataAnnotations validator to remove any more: Avalonia 12 removed binding plugins and
            // leaves that validator off, which is what the CommunityToolkit needed.
            if (_serviceProvider != null)
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UltralibrarianKicad");
                var browserPath = Path.Combine(directory, "browser");
                var cachePath = Path.Combine(browserPath, "cache");
                var cacheRootPath = Path.Combine(browserPath, "root");
                var resourcesPath = Path.Combine(browserPath, "resources");


                if (!CefRuntimeLoader.IsLoaded)
                {

                    _ = Directory.CreateDirectory(cachePath);
                    _ = Directory.CreateDirectory(cacheRootPath);
                    _ = Directory.CreateDirectory(resourcesPath);
                    var settings2 = new CefSettings
                    {
                        LogSeverity = CefLogSeverity.Disable,
                        UncaughtExceptionStackSize = 100,
                        CachePath = cachePath,
                        PersistSessionCookies = true,
                        PersistUserPreferences = true,
                        CookieableSchemesList = string.Join(",", CustomSchemes),
                        LogFile = "browser.txt",
                    };
                    CustomScheme[] customSchemes = CustomSchemes.Select((string s) => new CustomScheme
                    {
                        SchemeName = s,
                        SchemeHandlerFactory = new SchemeHandlerFactory()
                    }).ToArray();

                    // A switch with no value. These are the only CEF settings in effect now: until #67,
                    // WebViewControl's WebView re-initialised CEF with its own settings before the first
                    // browser loaded it, and those won.
                    KeyValuePair<string, string>[] switches = [new("enable-experimental-web-platform-features", null!)];
                    CefRuntimeLoader.Initialize(settings2, switches, customSchemes);

                    AppDomain.CurrentDomain.ProcessExit += delegate
                    {
                        Cleanup();
                    };
                }

                // Use dependency injection to create the main window and view model
                MainViewModel viewModel = _serviceProvider.GetRequiredService<MainViewModel>();
                IConfigService configService = _serviceProvider.GetRequiredService<IConfigService>();

                MainWindow = new MainWindow() { DataContext = viewModel };

                // Set up file watcher with the configured directory

                configService.EnsureDownloadDirectoryExists();
                _logger.LogInformation("Main window created and configured");


            }
            else
            {
                // Fallback for design-time or when DI is not available
                MainWindow = new MainWindow();
                _logger?.LogWarning("Creating main window without DI (possibly design mode)");
            }

            desktop.MainWindow = MainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    [DebuggerNonUserCode]
    public static void Cleanup()
    {
        CefRuntime.Shutdown();
    }

    internal class SchemeHandlerFactory : CefSchemeHandlerFactory
    {
        protected override CefResourceHandler? Create(CefBrowser browser, CefFrame frame, string schemeName, CefRequest request)
        {
            // Returning null is CEF's documented way of saying "no custom handler for this scheme";
            // the signature is widened to match rather than returning a dummy handler.
            return null;
        }
    }
}
