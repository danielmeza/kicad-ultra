using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using KiCadSharp;

using Microsoft.Extensions.Logging;

namespace UltraLibrarianImporter.UI.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    /// <summary>How long KiCad has to answer each question, as for the import engine's version query.</summary>
    private static readonly TimeSpan KiCadQueryTimeout = TimeSpan.FromSeconds(5);

    private const string Loading = "Loading ...";
    private const string Unknown = "Unknown";
    private const string NotConnected = "Not connected to KiCad";
    private const string KiCadError = "KiCad answered with an error";

    private readonly ILogger<AboutViewModel> _logger;
    private readonly KiCad? _kiCad;

    [ObservableProperty]
    private string _applicationName = "UltraLibrarian Importer for KiCad";

    [ObservableProperty]
    private string _version;

    [ObservableProperty]
    private string _gitHubUrl = "https://github.com/danielmeza/kicad-ultralibrarian-importer";

    [ObservableProperty]
    private string _apiSocket;

    [ObservableProperty]
    private string _apiToken;

    [ObservableProperty]
    private string _copyright = $"Copyright © {DateTime.Now.Year}";

    [ObservableProperty]
    private string _kicadVersion = Loading;

    [ObservableProperty]
    private string _projectName = Loading;

    /// <summary>What came of asking KiCad: connected, not connected, or why it did not answer.</summary>
    [ObservableProperty]
    private string _connectionStatus = Loading;

    [ObservableProperty]
    private bool _isConnected = false;

    /// <summary>
    /// Creates a new AboutViewModel
    /// </summary>
    /// <param name="logger">The logger</param>
    /// <param name="kiCad">KiCad client (can be null)</param>
    public AboutViewModel(ILogger<AboutViewModel> logger, KiCad? kiCad = null)
    {
        _logger = logger;
        _kiCad = kiCad;

        // Get KiCad environment variables
        ApiSocket = KiCadEnvironment.GetApiSocket() ?? "Not connected to KiCad";
        ApiToken = KiCadEnvironment.GetApiToken() ?? "Not connected to KiCad";

        // Get version info
        Version = GetVersionInfo();

        // Posted rather than run here, so that it starts on the UI thread whoever builds the view
        // model, and every continuation of the query comes back to it.
        Dispatcher.UIThread.Post(() => AskKiCadCommand.Execute(null));
    }

    /// <summary>
    /// Asks KiCad whether it is there, which version it is and which project it has open, and shows
    /// the answers, or why there are none.
    /// </summary>
    /// <remarks>
    /// Each call is made directly, with a deadline of its own passed as a token, as the import
    /// engine's version query does. KiCadSharp 0.4.0 dials on a thread of its own and waits for the
    /// reply without blocking the calling thread (kicad-sharp#127), so this runs on the UI thread and
    /// sets the bound properties there. The token ends the dial or the wait, so a KiCad that never
    /// answers shows as one within <see cref="KiCadQueryTimeout"/> instead of loading for good.
    /// </remarks>
    [RelayCommand]
    private async Task AskKiCad()
    {
        if (_kiCad is not { } kiCad)
        {
            // Only the XAML designer builds the window without a client.
            ShowUnanswered(NotConnected);
            return;
        }

        if (await AskAsync(nameof(KiCad.Ping), KiCadError, kiCad.Ping) is { } noPing)
        {
            ShowUnanswered(noPing);
            return;
        }

        IsConnected = true;
        ConnectionStatus = "Connected";

        KiCadVersion? version = null;
        var noVersion = await AskAsync(nameof(KiCad.GetVersion), KiCadError, async token => version = await kiCad.GetVersion(token));
        KicadVersion = noVersion ?? version?.ToString() ?? Unknown;
        if (noVersion is not null)
        {
            ProjectName = Unknown;
            return;
        }

        _logger.LogInformation("Connected to KiCad {Version}", KicadVersion);

        // KiCad 10 names the project only through the board open in pcbnew, and answers with an error
        // when there is none. KiCad is still connected then.
        Project? project = null;
        var noProject = await AskAsync(nameof(KiCad.GetProject), "None reported by KiCad", async token => project = await kiCad.GetProject(token));
        ProjectName = noProject ?? project?.Name ?? Unknown;
    }

    /// <summary>
    /// Makes one call to KiCad with a <see cref="KiCadQueryTimeout"/> deadline, and returns what to
    /// show instead of its answer, or <see langword="null"/> when there is an answer.
    /// </summary>
    /// <param name="request">The call, for the log.</param>
    /// <param name="whenRefused">What to show when KiCad answers with an error.</param>
    /// <param name="ask">The call.</param>
    private async Task<string?> AskAsync(string request, string whenRefused, Func<CancellationToken, ValueTask> ask)
    {
        using var deadline = new CancellationTokenSource(KiCadQueryTimeout);
        try
        {
            await ask(deadline.Token);
            return null;
        }
        catch (KiCadIpcException ex)
        {
            // Every IPC failure is one of these since KiCadSharp 0.4.0: KiCadConnectionException when
            // there is no socket to dial, nothing answers at it, or its native nng library cannot be
            // loaded, and ApiException, with KiCad's status, when KiCad answers with an error.
            _logger.LogWarning(ex, "Could not ask KiCad: {Request} failed", request);
            return ex is ApiException ? whenRefused : NotConnected;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            _logger.LogWarning("KiCad did not answer {Request} within {Timeout}", request, KiCadQueryTimeout);
            return $"KiCad did not answer within {KiCadQueryTimeout.TotalSeconds:0} s";
        }
    }

    /// <summary>Shows <paramref name="status"/>, and nothing known about KiCad.</summary>
    private void ShowUnanswered(string status)
    {
        IsConnected = false;
        ConnectionStatus = status;
        KicadVersion = Unknown;
        ProjectName = Unknown;
    }

    [RelayCommand]
    private void OpenGitHub()
    {
        try
        {
            var url = GitHubUrl;

            // Open the URL in the default browser
            if (OperatingSystem.IsWindows())
            {
                _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                _ = System.Diagnostics.Process.Start("open", url);
            }
            else if (OperatingSystem.IsLinux())
            {
                _ = System.Diagnostics.Process.Start("xdg-open", url);
            }

            _logger.LogInformation($"Opened GitHub URL: {url}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening GitHub URL");
        }
    }

    [RelayCommand]
    private void Close()
    {
        // This event will be handled by the view
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Event that signals the view to close
    /// </summary>
    public event EventHandler<EventArgs>? CloseRequested;

    /// <summary>
    /// Gets the version information from the assembly
    /// </summary>
    private string GetVersionInfo()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            Version? version = assembly.GetName().Version;
            return version?.ToString() ?? "1.0.0";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting version info");
            return "1.0.0";
        }
    }


}
