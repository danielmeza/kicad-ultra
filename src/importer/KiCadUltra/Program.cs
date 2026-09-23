using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Avalonia;
using KiCadUltra.Services;
using KiCadUltra.Services.Interfaces;
using KiCadUltra.Services.Mcp;
using KiCadUltra.Services.Providers.Jlcpcb;
using KiCadUltra.Services.Secrets;
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
using Velopack;

namespace KiCadUltra;

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
        // Read first, because both startup contracts below key off it. Reading the array has no side
        // effects, which is what lets it precede VelopackApp's "first code in Main".
        var isMcp = Array.Exists(args, a =>
            string.Equals(a, "--mcp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "-mcp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "mcp", StringComparison.OrdinalIgnoreCase));

        // Before anything works out a path under the application-data folder, in either mode: an
        // installation from before #132 keeps its settings, logs and browser cache under the names
        // this application used to have, and they are moved here or they are lost. Nothing is logged
        // yet - this is what decides where the log files go - so the notes are logged below, once a
        // configuration is assigned.
        IReadOnlyList<AppDataFolderNote> appDataMigration = AppDataFolder.Migrate();

        if (isMcp)
        {
            try
            {
                ConfigureMcpLogging();
                LogAppDataMigration(appDataMigration);
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
        //
        // Above Velopack, and only just: both of these are in-memory and touch no file until
        // something is logged, so a restart three lines down wastes nothing and leaves nothing
        // half-done - which is the whole reason Velopack asks to come first. What it buys is that
        // Velopack's own account of this start, "Auto apply is true, so restarting to apply
        // update..." included, lands in the application's log rather than only in a file of its own
        // in the temporary directory, and that a failure in Run() is reported rather than silent.
        SetLogDirectory();
        _ = LogManager.Setup(b => b.LoadConfigurationFromFile("nlog.config"));
        LogAppDataMigration(appDataMigration);

        // Velopack's contract is that VelopackApp.Build().Run() comes first, and the reason is that
        // it may not return: it runs the --veloapp-* install and uninstall hooks and exits, and when
        // a package this app downloaded earlier is waiting it hands the process to the updater and
        // exits, so the update is applied before the application starts (#128).
        //
        // Which is why it sits above HarfBuzzPreload.Apply() rather than below, and that only looks
        // like a contradiction of #78. #78's rule is about the dynamic loader: the preload has to
        // bind libHarfBuzzSharp's own hb_* slots to itself before CEF pulls in GTK - and with it the
        // system libharfbuzz, RTLD_GLOBAL - or those calls land in the system copy and the process
        // dies. Run() loads no graphics stack at all: it reads the package manifest, may start the
        // separate updater process, and returns. The preload still happens before the first line of
        // Avalonia or CEF code, which is what #78 actually asks for.
        //
        // Never in --mcp mode: stdout there is the JSON-RPC channel, and a staged update would
        // restart the process in the middle of a session. That path returned above.
        //
        // The logger given here is the locator's, so it also covers the background update checks;
        // AppUpdateService does not install one of its own.
        try
        {
            VelopackApp.Build()
                .SetLogger(new VelopackNLogBridge())
                .Run();
        }
        catch (Exception ex)
        {
            // Nothing below can run if the update machinery is broken, but dying without a word is
            // what a user reports as "the plugin does nothing".
            LogManager.GetCurrentClassLogger().Error(ex, "Velopack startup failed");
            throw;
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

    // nlog.config names the log files; their folder is set here, before either mode loads it, and
    // reaches it as ${gdc:logDirectory} (#98). NLog's own ${specialfolder} is "" on Linux while
    // ~/.config does not exist, and NLog kept that for the whole session, so on a fresh account every
    // log line went to /KiCadUltra/logs and was lost. SpecialFolders.GetPath does not need
    // the folder to exist (#70). A GDC item, not an NLog variable: a variable belongs to a
    // configuration, and there is none until nlog.config has been parsed.
    //
    // NLog's internal log goes to the same folder. internalLogFile cannot expand ${specialfolder}, and
    // made a directory with that literal name in the working directory on every start. The level is
    // set first because setting LogFile alone turns the internal log on at Info.
    private static void SetLogDirectory()
    {
        var logDirectory = AppDataFolder.Logs;
        GlobalDiagnosticsContext.Set("logDirectory", logDirectory);
        InternalLogger.LogLevel = NLog.LogLevel.Error;
        InternalLogger.LogFile = Path.Combine(logDirectory, "nlog-internal.log");
    }

    // What AppDataFolder.Migrate did, reported once a logging configuration exists. NLog's own
    // logger rather than ILogger: in both modes this runs before any container is built. In MCP mode
    // that configuration sends it to the log files and stderr, never to stdout (#80).
    private static void LogAppDataMigration(IReadOnlyList<AppDataFolderNote> notes)
    {
        if (notes.Count == 0)
        {
            return;
        }

        Logger logger = LogManager.GetCurrentClassLogger();
        foreach (AppDataFolderNote note in notes)
        {
            if (note.Failure is null)
            {
                logger.Info(note.Message);
            }
            else
            {
                logger.Warn(note.Failure, note.Message);
            }
        }
    }

    // The OS credential store, with one migration in front of it: everything an installation from
    // before #132 stored is filed under the service name this application used to have, and a store
    // is asked for a secret by service name, so without the copy every token the user gave is
    // invisible. It runs once - SecretStoreMigration explains the stamp file - and it never deletes
    // what it copied from. Both containers register this: the GUI to read and write the tokens, and
    // `--mcp` to read the Octopart one.
    private static ISecretStore CreateSecretStore(IServiceProvider provider)
    {
        ISecretStore store = PlatformSecretStore.Create();
        _ = SecretStoreMigration.Run(
            store,
            PlatformSecretStore.CreateLegacy(),
            ConfigService.SecretKeys,
            AppDataFolder.Current,
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SecretStoreMigration)));
        return store;
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
            .AddSingleton(CreateSecretStore)
            .AddSingleton<IConfigService, ConfigService>()
            .AddUltraLibrarianKiCadServices(JlcpcbSourcePolicy.WebsiteEndpointOnly)
            .BuildServiceProvider();

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
            .AddSingleton(CreateSecretStore)
            .AddSingleton<IConfigService, ConfigService>()
            // Downloads new releases in the background and stages them for the next start (#128). The
            // GUI container only: the `--mcp` container above must not update or prompt.
            .AddHostedService<AppUpdateService>()
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
