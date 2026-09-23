using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Velopack;
using Velopack.Sources;

namespace KiCadUltra.Services;

/// <summary>
/// Keeps an installed importer current from this repository's GitHub Releases (#128).
/// </summary>
/// <remarks>
/// <para>
/// It only ever <em>downloads</em>. The package is staged next to the installed application and
/// <c>VelopackApp.Build().Run()</c> installs it at the start of the next run, so an import in
/// progress is never interrupted and the user is never prompted.
/// </para>
/// <para>
/// <b>The first statement has to be the delay.</b> Lemon's <c>IHostLifetime</c> completes on
/// Avalonia's <c>Startup</c> event, so the host finishes starting on the UI thread, and
/// <see cref="BackgroundService.StartAsync"/> runs <see cref="ExecuteAsync"/> synchronously up to
/// its first <c>await</c>. Everything after that await is on the thread pool; anything moved in
/// front of it runs while the main window is being built.
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
        try
        {
            // Before anything else, so that everything below this line is off the caller's thread.
            // BackgroundService.StartAsync runs ExecuteAsync up to its first await, and Lemon's
            // IHostLifetime completes on Avalonia's Startup event, so that stretch runs on the UI
            // thread while the main window is being built. Constructing the locator reads the
            // package manifest from disk; it belongs after the delay, not in front of the window.
            await Task.Delay(FirstCheckDelay, _time, stoppingToken).ConfigureAwait(false);

            UpdateManager? updates = CreateUpdateManager();
            if (updates == null)
            {
                return;
            }

            using var timer = new PeriodicTimer(CheckInterval, _time);
            do
            {
                await DownloadIfNewerAsync(updates, stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The application is closing. Whatever was downloaded stays staged for the next run.
            // The filter matters: HttpClient reports its own request timeout as a
            // TaskCanceledException with nobody's token cancelled, and catching that here would end
            // the loop for the rest of the session over one slow answer from GitHub.
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
            // No logger is attached here: Program gave the locator a VelopackNLogBridge before
            // Run(), and UpdateManager takes its logger from that same locator.
            var updates = new UpdateManager(new GithubSource(ReleasesRepositoryUrl, accessToken: null, prerelease: false));
            if (!updates.IsInstalled)
            {
                _logger.LogInformation("This importer did not come from a Velopack package, so it will not update itself.");
                return null;
            }

            _logger.LogInformation("Update checks are on; this is version {Version}.", updates.CurrentVersion);
            return updates;
        }
        catch (Exception ex)
        {
            // Everything, not just the InvalidOperationException VelopackLocator.Current raises when
            // VelopackApp.Build().Run() has not run: the platform locator also reads a manifest from
            // disk and creates directories, and on an unknown OS it throws
            // PlatformNotSupportedException. A throw escaping here would fault this BackgroundService,
            // and Lemon starts the host with an un-awaited RunAsync, so nobody would ever observe it.
            _logger.LogWarning(ex, "Velopack could not be started, so this importer will not update itself.");
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
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Broader than the narrow catches the providers use, on purpose: this runs behind the
            // user's back, so nothing it does may reach them or end the host. Velopack raises plain
            // exceptions for a torn download, a feed it cannot parse and an update lock another copy
            // holds, and none of those is worth more than a line in the log.
            //
            // The token test is what separates the two kinds of cancellation. HttpClient reports its
            // own request timeout as a TaskCanceledException with nothing cancelled, and that is an
            // ordinary failure to log and try again for later; only a cancellation that really is the
            // application closing is left to the caller, which ends the loop.
            _logger.LogWarning(ex, "Checking for updates failed; the importer keeps running on {Version}.", updates.CurrentVersion);
        }
    }
}
