using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Kiapi.Common;

using KiCadSharp;

using KiCadUltra.Services.Interfaces;

using Microsoft.Extensions.Logging;

namespace KiCadUltra.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    /// <summary>
    /// How long KiCad has to answer each question, as for the import engine's version query. Internal
    /// because the view says how long that was when KiCad did not answer in time.
    /// </summary>
    internal static readonly TimeSpan KiCadQueryTimeout = TimeSpan.FromSeconds(5);

    private const string Loading = "Loading ...";
    private const string Unknown = "Unknown";
    private const string NoProject = "None reported by KiCad";

    private readonly ILogger<AboutViewModel> _logger;
    private readonly KiCad? _kiCad;
    private readonly IKiCadCompatibility? _compatibility;

    [ObservableProperty]
    private string _applicationName = "UltraLibrarian Importer for KiCad";

    [ObservableProperty]
    private string _version;

    [ObservableProperty]
    private string _gitHubUrl = "https://github.com/danielmeza/kicad-ultra";

    /// <summary>The socket address the KiCad client dials.</summary>
    [ObservableProperty]
    private string _apiSocket;

    /// <summary>Where <see cref="ApiSocket"/> came from.</summary>
    [ObservableProperty]
    private ApiSocketSource _apiSocketSource;

    /// <summary>Whether an API token is set. Never the token itself.</summary>
    [ObservableProperty]
    private ApiTokenState _apiToken;

    [ObservableProperty]
    private string _copyright = $"Copyright © {DateTime.Now.Year}";

    [ObservableProperty]
    private string _kicadVersion = Loading;

    /// <summary>The KiCad versions this build declares support for, always shown (#138).</summary>
    [ObservableProperty]
    private string _supportedKicadVersions = string.Empty;

    /// <summary>
    /// Why the running KiCad is outside that range, or empty when it is inside it, when nothing is
    /// declared, or when KiCad did not answer.
    /// </summary>
    [ObservableProperty]
    private string _kicadVersionWarning = string.Empty;

    /// <summary>Whether <see cref="KicadVersionWarning"/> has something to say.</summary>
    [ObservableProperty]
    private bool _hasKicadVersionWarning;

    [ObservableProperty]
    private string _projectName = Loading;

    /// <summary>What came of asking KiCad: connected, not connected, or why it did not answer.</summary>
    [ObservableProperty]
    private KiCadQueryState _connectionState = KiCadQueryState.Asking;

    /// <summary>Whether KiCad answered what was asked, for the dot beside the Connection Status line.</summary>
    [ObservableProperty]
    private bool _isConnected = false;

    /// <summary>
    /// Creates a new AboutViewModel
    /// </summary>
    /// <param name="logger">The logger</param>
    /// <param name="kiCad">KiCad client (can be null)</param>
    /// <param name="compatibility">
    /// What this build supports (#138), or <see langword="null"/> for the XAML designer, which has no
    /// container. The window then says nothing about compatibility rather than failing to open.
    /// </param>
    public AboutViewModel(ILogger<AboutViewModel> logger, KiCad? kiCad = null, IKiCadCompatibility? compatibility = null)
    {
        _logger = logger;
        _kiCad = kiCad;
        _compatibility = compatibility;

        SupportedKicadVersions = compatibility?.DescribeShipped() ?? string.Empty;

        // What the client dials and whether it has a token -- not KiCad's environment alone, which is
        // set only for a plugin KiCad launched itself. The words for both lines are the view's, so
        // that nothing here can put a token value on screen.
        ApiSocket = KiCadEnvironment.GetDefaultSocketPath();
        ApiSocketSource = string.IsNullOrEmpty(KiCadEnvironment.GetApiSocket())
            ? ApiSocketSource.DefaultPath
            : ApiSocketSource.KiCadEnvironment;
        ApiToken = string.IsNullOrEmpty(KiCadEnvironment.GetApiToken())
            ? ApiTokenState.NotSet
            : ApiTokenState.Set;

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
            ShowUnanswered(KiCadQueryState.NotConnected);
            return;
        }

        if (await AskAsync(nameof(KiCad.Ping), kiCad.Ping) is { } noPing)
        {
            ShowUnanswered(noPing);
            return;
        }

        ConnectionState = KiCadQueryState.Answered;
        IsConnected = true;

        KiCadVersion? version = null;
        if (await AskAsync(nameof(KiCad.GetVersion), async token => version = await kiCad.GetVersion(token)) is { } noVersion)
        {
            // The reason goes on the Connection Status line, which is the one that names the state.
            ShowUnanswered(noVersion);
            return;
        }

        KicadVersion = version?.ToString() ?? Unknown;
        _logger.LogInformation("Connected to KiCad {Version}", KicadVersion);

        // A line beside the version, never a dialog and never a refusal (#138). A KiCad newer than
        // this build was tested against is worth saying and nothing more; one outside a declared
        // minimum or maximum is worth saying more loudly, and still nothing more.
        KicadVersionWarning = (version is null ? null : _compatibility?.WarnAbout(version)) ?? string.Empty;
        HasKicadVersionWarning = KicadVersionWarning.Length > 0;
        if (HasKicadVersionWarning)
        {
            _logger.LogWarning("{Warning}", KicadVersionWarning);
        }

        // KiCad 10 names the project only through the board open in pcbnew, and answers with an error
        // when there is none. KiCad is still connected then, so that alone leaves the status line.
        Project? project = null;
        KiCadQueryState? noProject = await AskAsync(nameof(KiCad.GetProject), async token => project = await kiCad.GetProject(token));
        if (noProject is null)
        {
            ProjectName = project?.Name ?? Unknown;
        }
        else if (noProject == KiCadQueryState.AnsweredWithError)
        {
            ProjectName = NoProject;
        }
        else
        {
            // Not ShowUnanswered: KiCad did say which version it is, and that answer stands.
            ConnectionState = noProject.Value;
            IsConnected = false;
            ProjectName = Unknown;
        }
    }

    /// <summary>
    /// Makes one call to KiCad with a <see cref="KiCadQueryTimeout"/> deadline, and returns why there
    /// is no answer, or <see langword="null"/> when there is one.
    /// </summary>
    /// <param name="request">The call, for the log.</param>
    /// <param name="ask">The call.</param>
    private async Task<KiCadQueryState?> AskAsync(string request, Func<CancellationToken, ValueTask> ask)
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
            return ex switch
            {
                // KiCad's own status, not its message (#129): AS_NOT_READY is what it answers while
                // something else owns its main loop -- a modal dialog, or the second before a fresh
                // start can serve the API -- and AS_BUSY while it is in the middle of an operation.
                // Both mean "there, but ask again", which is a different answer from a real failure.
                ApiException { StatusCode: ApiStatusCode.AsNotReady or ApiStatusCode.AsBusy } => KiCadQueryState.Busy,
                ApiException => KiCadQueryState.AnsweredWithError,
                _ => KiCadQueryState.NotConnected,
            };
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            _logger.LogWarning("KiCad did not answer {Request} within {Timeout}", request, KiCadQueryTimeout);
            return KiCadQueryState.TimedOut;
        }
    }

    /// <summary>Shows <paramref name="state"/>, and nothing known about KiCad.</summary>
    private void ShowUnanswered(KiCadQueryState state)
    {
        IsConnected = false;
        ConnectionState = state;
        KicadVersion = Unknown;
        KicadVersionWarning = string.Empty;
        HasKicadVersionWarning = false;
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

/// <summary>
/// What came of asking KiCad a question: its answer, or why there is none. The About window's
/// Connection Status line is one of these, and the view turns it into words.
/// </summary>
public enum KiCadQueryState
{
    /// <summary>The question has been asked and KiCad has not answered yet.</summary>
    Asking,

    /// <summary>KiCad answered.</summary>
    Answered,

    /// <summary>There was nothing at the socket to answer: no KiCad, or none reachable.</summary>
    NotConnected,

    /// <summary>KiCad did not answer within <see cref="AboutViewModel.KiCadQueryTimeout"/>.</summary>
    TimedOut,

    /// <summary>KiCad answered with an error, so it is there but did not say what was asked.</summary>
    AnsweredWithError,

    /// <summary>
    /// KiCad is there and answered, but cannot take the question now: <c>AS_NOT_READY</c>, which is
    /// what it says while a modal dialog owns its main loop, or <c>AS_BUSY</c>. Asking again later
    /// is the whole remedy, so this is not an error.
    /// </summary>
    Busy,
}

/// <summary>
/// Where the address the KiCad client dials came from. <c>AddKiCad</c> takes it from
/// <see cref="KiCadEnvironment.GetDefaultSocketPath"/>, which answers one or the other.
/// </summary>
public enum ApiSocketSource
{
    /// <summary>
    /// The address KiCad 10 listens on when nothing names one, worked out as KiCad works it out:
    /// <c>&lt;temp&gt;/kicad/api.sock</c>, or on Linux a Flathub KiCad's socket when only that exists.
    /// </summary>
    DefaultPath,

    /// <summary><c>KICAD_API_SOCKET</c>, which KiCad sets for a plugin it launched itself.</summary>
    KiCadEnvironment,
}

/// <summary>
/// Whether KiCad's API token is set. The value itself is never shown and never logged, so this is
/// all there is to say about it.
/// </summary>
public enum ApiTokenState
{
    /// <summary>No <c>KICAD_API_TOKEN</c>; the client reaches KiCad with an empty token.</summary>
    NotSet,

    /// <summary><c>KICAD_API_TOKEN</c> is set, as it is for a plugin KiCad launched itself.</summary>
    Set,
}
