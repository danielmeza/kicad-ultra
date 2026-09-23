using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace UltraLibrarianImporter.UI.Services;

/// <summary>
/// Keeps an installed importer current from this repository's GitHub Releases (#128).
/// </summary>
/// <remarks>
/// <para>
/// It only ever <em>downloads</em>. The package is staged next to the installed application and
/// <c>VelopackApp.Build().Run()</c> installs it at the start of the next run, so an import in
/// progress is never interrupted and the user is never prompted. Nothing here touches the UI
/// thread: the host starts this after Avalonia's <c>Startup</c> has fired (Lemon's
/// <c>IHostLifetime</c> waits for it), and every step runs on the thread pool.
/// </para>
/// <para>
/// Registered in the GUI container only. <c>--mcp</c> must not update, and must not write to
/// stdout; <see cref="Program"/> does not bring Velopack into that path at all.
/// </para>
/// </remarks>
internal sealed class AppUpdateService : BackgroundService
{
    /// <summary>
    /// The repository whose Releases carry the packages. GitHub <em>Packages</em> answers 401 to an
    /// anonymous request, so it cannot serve plugin users; Releases answers 200, and that is what
    /// <see cref="GithubSource"/> reads.
    /// </summary>
    public const string ReleasesRepositoryUrl = "https://github.com/danielmeza/kicad-ultra";

    // Long enough for CEF and the main window to be up before anything competes for the network,
    // then a slow repeat for the rare session left open all day. Both are deliberately far from
    // GitHub's 60-requests-per-hour limit for unauthenticated callers.
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly ILogger<AppUpdateService> _logger;
    private readonly TimeProvider _time;

    public AppUpdateService(ILogger<AppUpdateService> logger, TimeProvider time)
    {
        _logger = logger;
        _time = time;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        UpdateManager? updates = CreateUpdateManager();
        if (updates == null)
        {
            return;
        }

        try
        {
            await Task.Delay(FirstCheckDelay, _time, stoppingToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(CheckInterval, _time);
            do
            {
                await DownloadIfNewerAsync(updates, stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // The application is closing. Whatever was downloaded stays staged for the next run.
        }
    }

    /// <summary>
    /// An <see cref="UpdateManager"/> for this installation, or <see langword="null"/> when this copy
    /// cannot update itself.
    /// </summary>
    /// <remarks>
    /// A build started by <c>dotnet run</c>, or an application directory someone copied out of a
    /// package, is not a Velopack installation: <see cref="UpdateManager.IsInstalled"/> is false and
    /// every check would throw <c>NotInstalledException</c>. Saying so once and stopping is the
    /// honest answer - it is not a failure, and it is the normal case during development.
    /// </remarks>
    private UpdateManager? CreateUpdateManager()
    {
        try
        {
            // Velopack's own diagnostics go to a file of its own beside the application. This bridge
            // puts them in the app's NLog files as well, which is where #121 and #123 say every line
            // belongs. The locator exists because Program called VelopackApp.Build().Run().
            VelopackLocator.Current.AddLogger(new VelopackLoggerBridge(_logger));

            var updates = new UpdateManager(new GithubSource(ReleasesRepositoryUrl, accessToken: null, prerelease: false));
            if (!updates.IsInstalled)
            {
                _logger.LogInformation("This importer did not come from a Velopack package, so it will not update itself.");
                return null;
            }

            _logger.LogInformation("Update checks are on; this is version {Version}.", updates.CurrentVersion);
            return updates;
        }
        catch (InvalidOperationException ex)
        {
            // VelopackLocator.Current throws when VelopackApp.Build().Run() has not run - the XAML
            // designer, or a future entry point that forgets it.
            _logger.LogWarning(ex, "Velopack is not initialised, so this importer will not update itself.");
            return null;
        }
    }

    private async Task DownloadIfNewerAsync(UpdateManager updates, CancellationToken cancellationToken)
    {
        try
        {
            UpdateInfo? available = await updates.CheckForUpdatesAsync().ConfigureAwait(false);
            if (available == null)
            {
                _logger.LogDebug("No newer release; staying on {Version}.", updates.CurrentVersion);
                return;
            }

            _logger.LogInformation("Release {Version} is available; downloading it in the background.", available.TargetFullRelease.Version);
            await updates.DownloadUpdatesAsync(available, progress: null, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Release {Version} is staged, and is installed the next time the importer starts.",
                available.TargetFullRelease.Version);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Broader than the narrow catches the providers use, on purpose: this runs behind the
            // user's back, so nothing it does may reach them or end the host. Velopack raises plain
            // exceptions for a torn download, a feed it cannot parse and an update lock another copy
            // holds, and none of those is worth more than a line in the log.
            _logger.LogWarning(ex, "Checking for updates failed; the importer keeps running on {Version}.", updates.CurrentVersion);
        }
    }

    /// <summary>Sends Velopack's own log lines to <see cref="ILogger"/>, and so to NLog.</summary>
    private sealed class VelopackLoggerBridge : IVelopackLogger
    {
        private readonly ILogger _logger;

        public VelopackLoggerBridge(ILogger logger) => _logger = logger;

        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception) =>
            _logger.Log(Map(logLevel), exception, "Velopack: {Message}", message);

        // Written out rather than cast: the two enumerations happen to agree today, and a cast would
        // turn a future member of Velopack's into a silently wrong severity.
        private static LogLevel Map(VelopackLogLevel level) => level switch
        {
            VelopackLogLevel.Trace => LogLevel.Trace,
            VelopackLogLevel.Debug => LogLevel.Debug,
            VelopackLogLevel.Information => LogLevel.Information,
            VelopackLogLevel.Warning => LogLevel.Warning,
            VelopackLogLevel.Error => LogLevel.Error,
            VelopackLogLevel.Critical => LogLevel.Critical,
            _ => LogLevel.Information,
        };
    }
}
