using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using UltraLibrarianImporter.KiCadBindings;

using WebViewControl;

using Xilium.CefGlue;

namespace UltraLibrarianImporter.UI.ViewModels
{
    public partial class AboutViewModel : ViewModelBase
    {
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
        private string _kicadVersion = "Not connected to KiCad";

        [ObservableProperty]
        private string _projectName = "No project open";

        [ObservableProperty]
        private string _projectPath = "No project open";

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

            // Load KiCad information asynchronously
            LoadKiCadInfoAsync();
        }

        private void LoadKiCadInfoAsync()
        {
            if (_kiCad == null)
            {
                return;
            }

            var isConnected = false;
            var projectName = "Loading ...";
            var kicadVersion = "Loading ...";
            var projectPath = "Loading ...";
            IsConnected = false;
            ProjectName = projectName;
            KicadVersion = kicadVersion;
            ProjectPath = projectPath;
            Task.Run(async () =>
            {

                try
                {
                    // Try to connect to KiCad and get version information
                    await _kiCad.Ping();
                    isConnected = true;

                    // Get KiCad version
                    var versionInfo = await _kiCad.GetVersion();
                    kicadVersion = $"{versionInfo}";

                    // Try to get active project information
                    try
                    {
                        var board = await _kiCad.GetBoard();
                        var project = board.GetProject();
                        projectName = project.Name;

                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not get project information");
                    }
                    _logger.LogInformation($"Connected to KiCad {kicadVersion}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not connect to KiCad");
                }

                RunOnUIThread(() =>
                {
                    IsConnected = isConnected;
                    ProjectName = projectName;
                    KicadVersion = kicadVersion;
                });
            });
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
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    System.Diagnostics.Process.Start("open", url);
                }
                else if (OperatingSystem.IsLinux())
                {
                    System.Diagnostics.Process.Start("xdg-open", url);
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
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "1.0.0";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting version info");
                return "1.0.0";
            }
        }

    
    }
}