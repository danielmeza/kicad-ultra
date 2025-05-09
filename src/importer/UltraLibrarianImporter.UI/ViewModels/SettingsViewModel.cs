using System;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using KiCadSharp;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IConfigService _configService;
        private readonly ILogger<SettingsViewModel> _logger;
        private readonly IOptionsMonitor<KiCadClientSettings> _kicadSettings;
        private KiCadClientSettings _originalSettings;

        [ObservableProperty]
        private string _pipeName = string.Empty;

        [ObservableProperty]
        private string _token = string.Empty;

        [ObservableProperty]
        private string _clientName = string.Empty;

        [ObservableProperty]
        private string _downloadDirectory = string.Empty;

        [ObservableProperty]
        private bool _addToGlobalLibrary;

        [ObservableProperty]
        private bool _cleanupAfterImport;
        
        [ObservableProperty]
        private string _targetPath = string.Empty;
        
        [ObservableProperty]
        private bool _useProjectPath = true;
        
        [ObservableProperty]
        private bool _autoImportWhenDownloaded = true;
        
        [ObservableProperty]
        private string _libraryName = string.Empty;
        
        // Event for folder browsing (will be handled by the view)
        public event EventHandler<EventArgs>? BrowseForFolderRequested;
        
        // Event for target path browsing
        public event EventHandler<EventArgs>? BrowseForTargetPathRequested;

        // Events for dialog closing
        public event EventHandler<bool>? SettingsSaved;

        /// <summary>
        /// Creates a new instance of the settings view model
        /// </summary>
        /// <param name="configService">Configuration service</param>
        /// <param name="logger">Logger for recording operations</param>
        /// <param name="kicadSettings">KiCad client settings</param>
        public SettingsViewModel(
            IConfigService configService, 
            ILogger<SettingsViewModel> logger,
            IOptionsMonitor<KiCadClientSettings> kicadSettings)
        {
            _configService = configService;
            _logger = logger;
            _kicadSettings = kicadSettings;
            
            // Store original settings for comparison on save
            _originalSettings = new KiCadClientSettings
            {
                PipeName = _kicadSettings.CurrentValue.PipeName,
                Token = _kicadSettings.CurrentValue.Token,
            };

            // Initialize properties from settings
            InitializeSettings();
            
            _logger.LogInformation("SettingsViewModel initialized");
        }
        
        private void InitializeSettings()
        {
            // Initialize KiCad client settings
            PipeName = _kicadSettings.CurrentValue.PipeName ?? 
                      KiCadEnvironment.GetDefaultSocketPath();
            Token = _kicadSettings.CurrentValue.Token ?? 
                   KiCadEnvironment.GetApiToken() ?? string.Empty;
            ClientName = _kicadSettings.CurrentValue.ClientName ?? 
                        KiCadEnvironment.GenerateRandomClientName();
            
            // Initialize app configuration settings
            DownloadDirectory = _configService.DownloadDirectory;
            AddToGlobalLibrary = _configService.AddToGlobalLibrary;
            CleanupAfterImport = _configService.CleanupAfterImport;
            TargetPath = _configService.TargetPath;
            UseProjectPath = _configService.UseProjectPath;
            AutoImportWhenDownloaded = _configService.AutoImportWhenDownloaded;
            LibraryName = _configService.LibraryName;
        }

        [RelayCommand]
        private void Save()
        {
            try
            {
                // Update KiCad client settings
                var currentSettings = _kicadSettings.CurrentValue;
                currentSettings.PipeName = PipeName;
                currentSettings.Token = Token;
                
                // Update app configuration
                _configService.DownloadDirectory = DownloadDirectory;
                _configService.AddToGlobalLibrary = AddToGlobalLibrary;
                _configService.CleanupAfterImport = CleanupAfterImport;
                _configService.TargetPath = TargetPath;
                _configService.UseProjectPath = UseProjectPath;
                _configService.AutoImportWhenDownloaded = AutoImportWhenDownloaded;
                _configService.LibraryName = LibraryName;

                // Save to file
                _configService.Save();
                _configService.EnsureDownloadDirectoryExists();

                _logger.LogInformation("Settings saved");
                
                // Trigger dialog close with success
                SettingsSaved?.Invoke(this, true);
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
        private void BrowseTargetPath()
        {
            // Raise an event for the view to handle
            BrowseForTargetPathRequested?.Invoke(this, EventArgs.Empty);
        }
        
        [RelayCommand]
        private void Cancel()
        {
            // Revert any changes to KiCad settings
            var currentSettings = _kicadSettings.CurrentValue;
            currentSettings.PipeName = _originalSettings.PipeName;
            currentSettings.Token = _originalSettings.Token;
            
            // Trigger dialog close without saving
            SettingsSaved?.Invoke(this, false);
        }
    }
}