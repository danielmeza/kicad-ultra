using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.ViewModels;
using UltraLibrarianImporter.UI.Views;

using WebViewControl;

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

    public App(IServiceProvider serviceProvider = null)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetRequiredService<ILogger<App>>();
        _logger.LogInformation("Application starting up");
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public static MainWindow MainWindow { get; private set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            if (_serviceProvider != null)
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UltralibrarianKicad");
                var browserPath = Path.Combine(directory, "browser");
                var cachePath = Path.Combine(browserPath, "cache");
                var cacheRootPath = Path.Combine(browserPath, "root");
                var resourcesPath = Path.Combine(browserPath, "resources");


                if (!CefRuntimeLoader.IsLoaded)
                {

                    Directory.CreateDirectory(cachePath);
                    Directory.CreateDirectory(cacheRootPath);
                    Directory.CreateDirectory(resourcesPath);
                    CefSettings settings2 = new CefSettings
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

                    GlobalSettings settings = new()
                    {
                        CachePath = cachePath,
                        PersistCache = true,
                    };


                    settings.AddCommandLineSwitch("enable-experimental-web-platform-features", null);
                    CefRuntimeLoader.Initialize(settings2, settings.CommandLineSwitches.ToArray(), customSchemes);

                    AppDomain.CurrentDomain.ProcessExit += delegate
                    {
                        Cleanup();
                    };
                }

                // Use dependency injection to create the main window and view model
                var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();
                var configService = _serviceProvider.GetRequiredService<IConfigService>();
                // Configure WebView settings before loading XAML
                WebView.Settings.PersistCache = true;
                WebView.Settings.CachePath = cachePath;

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

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    [DebuggerNonUserCode]
    public static void Cleanup()
    {
        CefRuntime.Shutdown();
    }

    internal class SchemeHandlerFactory : CefSchemeHandlerFactory
    {
        protected override CefResourceHandler Create(CefBrowser browser, CefFrame frame, string schemeName, CefRequest request)
        {
            return null;
        }
    }
}