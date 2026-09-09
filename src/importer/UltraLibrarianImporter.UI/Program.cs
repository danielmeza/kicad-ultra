using System;
using System.Runtime.Versioning;
using Avalonia;
using Lemon.Hosting.AvaloniauiDesktop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;
using UltraLibrarianImporter.UI.Services;
using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI;

internal sealed class Program
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
        _ = LogManager.Setup(b => b.LoadConfigurationFromFile("nlog.config"));

        try
        {
            // Create the host
            using IHost host = CreateApplicationBuilder(args).Build();

            _ = host.RunAvaloniaAppAsync();
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
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        ConfigureServices(builder.Services);
        return builder;
    }

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
#pragma warning disable CS0618 // AddAvaloniauiDesktopApplication is obsolete in favour of AddAppBuilder.
    // AddAppBuilder invokes its Func<AppBuilder> eagerly, with no service provider in scope. App's
    // constructor requires the container, so it cannot be constructed at that point;
    // AddAvaloniauiDesktopApplication resolves App lazily from the provider, which is the behaviour
    // this app depends on. Revisit if AddAppBuilder gains a provider-aware overload.
    private static void ConfigureServices(IServiceCollection services) =>
        services
            .AddSingleton(p => new App(p))
            .AddTransient<ViewModels.MainViewModel>()
            .AddTransient<ViewModels.SettingsViewModel>()
            .AddTransient<ViewModels.AboutViewModel>()
            .AddSingleton<IConfigService, ConfigService>()
            .AddUltraLibrarianKiCadServices()
            .AddAvaloniauiDesktopApplication<App>(BuildAvaloniaApp);
#pragma warning restore CS0618


}
