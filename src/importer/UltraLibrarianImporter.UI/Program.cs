using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Avalonia;
using Lemon.Hosting.AvaloniauiDesktop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Common;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using ReactiveUI.Avalonia;
using UltraLibrarianImporter.UI.Services;
using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.Services.Mcp;
using UltraLibrarianImporter.UI.Services.Providers.Jlcpcb;
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
        var isMcp = Array.Exists(args, a =>
            string.Equals(a, "--mcp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "-mcp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "mcp", StringComparison.OrdinalIgnoreCase));

        if (isMcp)
        {
            try
            {
                ConfigureMcpLogging();
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

        // Initialize NLog. If SetLogDirectory throws, there is no ApplicationData folder, which
        // ConfigService and the browser cache need as well, so its exception is left to end the GUI.
        SetLogDirectory();
        _ = LogManager.Setup(b => b.LoadConfigurationFromFile("nlog.config"));

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

    // nlog.config names the log files; their folder is set here, before either mode loads it, and
    // reaches it as ${gdc:logDirectory} (#98). NLog's own ${specialfolder} is "" on Linux while
    // ~/.config does not exist, and NLog kept that for the whole session, so on a fresh account every
    // log line went to /UltraLibrarianImporter/logs and was lost. SpecialFolders.GetPath does not need
    // the folder to exist (#70). A GDC item, not an NLog variable: a variable belongs to a
    // configuration, and there is none until nlog.config has been parsed.
    //
    // NLog's internal log goes to the same folder. internalLogFile cannot expand ${specialfolder}, and
    // made a directory with that literal name in the working directory on every start. The level is
    // set first because setting LogFile alone turns the internal log on at Info.
    private static void SetLogDirectory()
    {
        var logDirectory = Path.Combine(
            SpecialFolders.GetPath(Environment.SpecialFolder.ApplicationData), "UltraLibrarianImporter", "logs");
        GlobalDiagnosticsContext.Set("logDirectory", logDirectory);
        InternalLogger.LogLevel = NLog.LogLevel.Error;
        InternalLogger.LogFile = Path.Combine(logDirectory, "nlog-internal.log");
    }

    // MCP mode logs to nlog.config's log files and to stderr, never to stdout, which carries the
    // JSON-RPC messages (#80). nlog.config stays the one place that names the log files, but only its
    // file targets are kept: an allowlist, so a console target added to it later, or renamed, cannot
    // reach stdout. autoReload is off, because a reload reads nlog.config again, stdout target
    // included, while the server runs. If the log folder cannot be resolved, or nlog.config is missing
    // or unreadable, the server still runs and logs to stderr alone. SetLogDirectory's failure is
    // caught here rather than in Main because Main's catch asks NLog for a logger, and with no
    // configuration assigned NLog would load nlog.config by itself, stdout target included.
    private static void ConfigureMcpLogging()
    {
        LoggingConfiguration config;
        Exception? loadFailure = null;
        try
        {
            SetLogDirectory();
            config = new XmlLoggingConfiguration(Path.Combine(AppContext.BaseDirectory, "nlog.config"))
            {
                AutoReload = false
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or NLogConfigurationException or IOException or UnauthorizedAccessException)
        {
            config = new LoggingConfiguration();
            loadFailure = ex;
        }

        foreach (Target target in config.AllTargets.Where(target => target is not FileTarget).ToList())
        {
            config.RemoveTarget(target.Name);
        }

        config.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, new ConsoleTarget("stderr")
        {
            StdErr = true,
            Layout = "${time} | ${level:uppercase=true:padding=-5} | ${logger:shortName=true} | ${message} ${exception:format=tostring}"
        });

        LogManager.Configuration = config;

        if (loadFailure != null)
        {
            LogManager.GetCurrentClassLogger().Warn(loadFailure, "The log files could not be set up; MCP mode logs to stderr only");
        }
    }

    private static async System.Threading.Tasks.Task RunMcpHostAsync()
    {
        using ServiceProvider serviceProvider = BuildMcpServiceProvider();
        await serviceProvider.GetRequiredService<McpServer>().RunStdioAsync();
    }

    // No host and no Avalonia. ILogger goes to NLog alone, so only where ConfigureMcpLogging sends it.
    // EasyEDA / LCSC search never uses JLCPCB's official API here, even with credentials stored:
    // JLCPCB's API terms forbid passing API data to a third party, and every result goes to the AI
    // client on the other end of stdio.
    private static ServiceProvider BuildMcpServiceProvider() =>
        new ServiceCollection()
            .AddLogging(logging => logging.ClearProviders().AddNLog())
            .AddSingleton(_ => PlatformSecretStore.Create())
            .AddSingleton<IConfigService, ConfigService>()
            .AddUltraLibrarianKiCadServices(JlcpcbSourcePolicy.WebsiteEndpointOnly)
            .BuildServiceProvider();

    // Avalonia configuration, don't remove; also used by visual designer.
    //
    // UseReactiveUI is ReactiveUI.Avalonia's (ReactiveUI 23): it initialises ReactiveUI and points
    // RxSchedulers.MainThreadScheduler at Avalonia's dispatcher, which the Part Explorer search observes
    // its results on (#49).
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

    // ILogger goes to NLog alone: nlog.config's files, and its console target for `dotnet run`. Before
    // #34 nothing bridged ILogger into NLog. AddConsole() is gone because, next to that console
    // target, it printed every ILogger line twice (#98). NLog's console is the one kept because it
    // also prints what is logged through NLog directly, such as HarfBuzzPreload's line.
    private static void ConfigureLogging(ILoggingBuilder logging) =>
        logging
            .ClearProviders()
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
            // The GUI shows results to the user whose credentials they are, so it may use the API (#51).
            .AddUltraLibrarianKiCadServices(JlcpcbSourcePolicy.OfficialApiWhenConfigured)
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
