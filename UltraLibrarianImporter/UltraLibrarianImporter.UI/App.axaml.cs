using System;
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

namespace UltraLibrarianImporter.UI;

public partial class App : Application
{
    public readonly IServiceProvider _serviceProvider;
    private readonly ILogger<App> _logger;

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
                // Use dependency injection to create the main window and view model
                var viewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
                var configService = _serviceProvider.GetRequiredService<IConfigService>();
                // Configure WebView settings before loading XAML
                //WebView.Settings.LogFile = "ceflog.txt";
                WebView.Settings.LogFile = "ceflog.txt";
                // Configure WebView settings before loading XAML
                WebView.Settings.CachePath = Path.Combine(configService.DownloadDirectory, ".cache");
                WebView.Settings.AddCommandLineSwitch("download.default_directory", configService.DownloadDirectory);

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
}