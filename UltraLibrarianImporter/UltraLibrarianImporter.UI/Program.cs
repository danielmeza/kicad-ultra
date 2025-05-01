using System;
using System.Diagnostics;
using System.Runtime.Versioning;

using Avalonia;

using Lemon.Hosting.AvaloniauiDesktop;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NLog;

using UltraLibrarianImporter.KiCadBindings;
using UltraLibrarianImporter.UI.Services;
using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public static void Main(string[] args)
    {
        // Initialize NLog
        LogManager.Setup(b => b.LoadConfigurationFromFile("nlog.config"));

        try
        {
            // Create the host
            using IHost host = CreateApplicationBuilder(args).Build();

            host.RunAvaloniauiApplication(args);

        }
        catch (Exception ex)
        {
            // Log any startup errors
            LogManager.GetCurrentClassLogger().Error(ex, "Application startup failed");
            throw;
        }
        finally
        {
            // Ensure to flush and stop internal timers/threads before application-exit
            LogManager.Shutdown();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp(AppBuilder builder)
        => builder
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    // Create the host builder with all the services
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static HostApplicationBuilder CreateApplicationBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        ConfigureServices(builder.Services, builder.Environment, builder.Configuration);
        return builder;
    }

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static void ConfigureServices(IServiceCollection services, IHostEnvironment environment, ConfigurationManager configuration)
    {
        // Register App as a singleton
        services.AddSingleton<App>(p => new App(p));

        // Register ViewModels
        services.AddTransient<ViewModels.MainWindowViewModel>();
        services.AddTransient<ViewModels.SettingsViewModel>();
        services.AddTransient<ViewModels.AboutViewModel>();

        services.AddSingleton<IConfigService, ConfigService>();

        //Register KiCad
        services.AddUltraLibrarianKiCadServices();

        services.AddAvaloniauiDesktopApplication<App>(BuildAvaloniaApp);
    }


}
