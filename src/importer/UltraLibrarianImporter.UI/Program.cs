using System;
using System.Runtime.Versioning;
using Avalonia;
using Lemon.Hosting.AvaloniauiDesktop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using NLog.Targets;
using ReactiveUI.Avalonia;
using UltraLibrarianImporter.UI.Services;
using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.Services.Mcp;
using UltraLibrarianImporter.UI.Services.Secrets;

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

        var isMcp = Array.Exists(args, a =>
            string.Equals(a, "--mcp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "-mcp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "mcp", StringComparison.OrdinalIgnoreCase));

        if (isMcp)
        {
            try
            {
                Target? consoleTarget = LogManager.Configuration?.FindTargetByName("console");
                if (consoleTarget != null)
                {
                    LogManager.Configuration?.RemoveTarget("console");
                    LogManager.ReconfigExistingLoggers();
                }

                RunMcpHostAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogManager.GetCurrentClassLogger().Error(ex, "MCP Server error");
                Console.Error.WriteLine($"MCP Server Error: {ex.Message}");
            }
            finally
            {
                LogManager.Shutdown();
            }
            return;
        }

        // Before anything can start Avalonia or CEF: CEF's GTK brings in the system HarfBuzz, and
        // HarfBuzzSharp's own calls would bind to it and crash (#78). MCP mode loads neither, and must
        // not log to stdout, so this is the GUI path only.
        if (OperatingSystem.IsLinux())
        {
            HarfBuzzPreload.Apply();
        }

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

    private static async System.Threading.Tasks.Task RunMcpHostAsync()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging(builder =>
        {
            _ = builder.ClearProviders();
            _ = builder.AddNLog();
        });

        _ = services.AddSingleton(_ => PlatformSecretStore.Create());
        _ = services.AddSingleton<IConfigService, ConfigService>();
        _ = services.AddUltraLibrarianKiCadServices();

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        McpServer mcpServer = serviceProvider.GetRequiredService<McpServer>();
        await mcpServer.RunStdioAsync();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    //
    // UseReactiveUI is ReactiveUI.Avalonia's (ReactiveUI 23): it initialises ReactiveUI and points
    // RxSchedulers.MainThreadScheduler at Avalonia's dispatcher, which the Part Explorer search observes
    // its results on (#49). CefGlue.Avalonia also brings in the older Avalonia.ReactiveUI 11.0.9, whose
    // parameterless UseReactiveUI() lives in the Avalonia.ReactiveUI namespace and was built against
    // ReactiveUI 18: it sets RxApp.MainThreadScheduler, and RxApp no longer exists in ReactiveUI 23. Do
    // not import that namespace or call it.
    public static AppBuilder BuildAvaloniaApp(AppBuilder builder)
        => builder
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI(_ => { });

    // Create the host builder with all the services
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static HostApplicationBuilder CreateApplicationBuilder(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        ConfigureLogging(builder.Logging);
        ConfigureServices(builder.Services);
        return builder;
    }

    // Console for `dotnet run`; NLog so the file targets declared in nlog.config actually receive
    // ILogger output. Before #34 nothing bridged ILogger into NLog.
    private static void ConfigureLogging(ILoggingBuilder logging) =>
        logging
            .ClearProviders()
            .AddConsole()
            .AddNLog();

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
            .AddSingleton(_ => PlatformSecretStore.Create())
            .AddSingleton<IConfigService, ConfigService>()
            .AddUltraLibrarianKiCadServices()
            .AddAvaloniauiDesktopApplication<App>(BuildAvaloniaApp)
            // Replaces the IHostLifetime AddAvaloniauiDesktopApplication registered (#77). Lemon 1.1.1
            // gave AvaloniauiApplicationLifetime<App> a second constructor that takes App itself, and
            // DI picks it because it can satisfy more parameters. That builds App while the host
            // starts, before Avalonia's platform setup: AvaloniaObject() then binds the UI dispatcher
            // to NullDispatcherImpl, and Dispatcher.MainLoop throws PlatformNotSupportedException.
            // The lazy constructor resolves App in WaitForStartAsync, after setup, as Lemon 1.0.0 did.
            .AddSingleton<IHostLifetime>(provider => new AvaloniauiApplicationLifetime<App>(
                provider.GetRequiredService<IHostApplicationLifetime>(),
                provider,
                provider.GetService<ILogger<AvaloniauiApplicationLifetime<App>>>()));
#pragma warning restore CS0618


}
