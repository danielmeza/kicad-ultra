using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IConfigService _configService;
        private readonly ILogger<SettingsViewModel> _logger;

        [ObservableProperty]
        private string _ipcEndpoint = string.Empty;

        [ObservableProperty]
        private string _ipcToken = string.Empty;

        [ObservableProperty]
        private string _downloadDirectory = string.Empty;

        [ObservableProperty]
        private bool _addToGlobalLibrary;

        [ObservableProperty]
        private bool _cleanupAfterImport;
        
        // Event for folder browsing (will be handled by the view)
        public event EventHandler<EventArgs>? BrowseForFolderRequested;

        // Events for dialog closing
        public event EventHandler<EventArgs>? SaveRequested;
        public event EventHandler<EventArgs>? CancelRequested;

        /// <summary>
        /// Creates a new instance of the settings view model
        /// </summary>
        /// <param name="configService">Configuration service</param>
        /// <param name="logger">Logger for recording operations</param>
        public SettingsViewModel(IConfigService configService, ILogger<SettingsViewModel> logger)
        {
            _configService = configService;
            _logger = logger;

            // Initialize properties from config
            _ipcEndpoint = configService.IpcEndpoint;
            _ipcToken = configService.IpcToken;
            _downloadDirectory = configService.DownloadDirectory;
            _addToGlobalLibrary = configService.AddToGlobalLibrary;
            _cleanupAfterImport = configService.CleanupAfterImport;
            
            _logger.LogInformation("SettingsViewModel initialized");
        }

        [RelayCommand]
        private void Save()
        {
            try
            {
                // Update config with new values
                _configService.IpcEndpoint = IpcEndpoint;
                _configService.IpcToken = IpcToken;
                _configService.DownloadDirectory = DownloadDirectory;
                _configService.AddToGlobalLibrary = AddToGlobalLibrary;
                _configService.CleanupAfterImport = CleanupAfterImport;

                // Save to file
                _configService.Save();
                _configService.EnsureDownloadDirectoryExists();

                _logger.LogInformation("Settings saved");
                
                // Trigger dialog close with success
                SaveRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving settings");
            }
        }

        [RelayCommand]
        private void BrowseDownloadDir()
        {
            // Raise an event for the view to handle
            BrowseForFolderRequested?.Invoke(this, EventArgs.Empty);
        }
        
        [RelayCommand]
        private void Cancel()
        {
            // Trigger dialog close without saving
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}