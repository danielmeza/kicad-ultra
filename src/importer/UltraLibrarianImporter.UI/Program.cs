using System;
using System.Diagnostics;
using System.Runtime.Versioning;

using Avalonia;

using Lemon.Hosting.AvaloniauiDesktop;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NLog;

using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

using KiCadSharp;
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

        bool isMcp = Array.Exists(args, a =>
            string.Equals(a, "--mcp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "-mcp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "mcp", StringComparison.OrdinalIgnoreCase));

        if (isMcp)
        {
            try
            {
                var consoleTarget = LogManager.Configuration?.FindTargetByName("console");
                if (consoleTarget != null)
                {
                    LogManager.Configuration?.RemoveTarget("console");
                    LogManager.ReconfigExistingLoggers();
                }

                RunMcpHostAsync(args).GetAwaiter().GetResult();
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

    private static async System.Threading.Tasks.Task RunMcpHostAsync(string[] args)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddNLog();
        });

        services.AddSingleton<IConfigService, ConfigService>();
        services.AddUltraLibrarianKiCadServices();

        using var serviceProvider = services.BuildServiceProvider();
        var mcpServer = serviceProvider.GetRequiredService<Services.Mcp.McpServer>();
        await mcpServer.RunStdioAsync();
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
        services.AddTransient<ViewModels.MainViewModel>();
        services.AddTransient<ViewModels.SettingsViewModel>();
        services.AddTransient<ViewModels.AboutViewModel>();

        services.AddSingleton<IConfigService, ConfigService>();

        //Register KiCad
        services.AddUltraLibrarianKiCadServices();

        services.AddAvaloniauiDesktopApplication<App>(BuildAvaloniaApp);
    }


}
