using System;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UltraLibrarianImporter.KiCadBindings;

namespace UltraLibrarianImporter.UI.ViewModels
{
    public partial class AboutViewModel : ObservableObject
    {
        private readonly ILogger<AboutViewModel> _logger;

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

        /// <summary>
        /// Creates a new AboutViewModel
        /// </summary>
        /// <param name="logger">The logger</param>
        public AboutViewModel(ILogger<AboutViewModel> logger)
        {
            _logger = logger;

            // Get KiCad environment variables
            ApiSocket = KiCadEnvironment.GetApiSocket() ?? "Not connected to KiCad";
            ApiToken = KiCadEnvironment.GetApiToken() ?? "Not connected to KiCad";

            // Get version info
            Version = GetVersionInfo();
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