using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UltraLibrarianImporter.KiCadBindings;
using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.Views;

namespace UltraLibrarianImporter.UI.ViewModels
{
    // Change from inheriting ReactiveObject to ObservableObject
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly ILogger<MainWindowViewModel> _logger;
        private readonly IConfigService _configService;
        private readonly KiCad _kiCad;
        private KiCadBindings.UltraLibrarianImporter? _importer;

        // Use [ObservableProperty] for properties that need to notify changes
        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private bool _isProgressVisible;

        [ObservableProperty]
        private int _progressValue;

        [ObservableProperty]
        private bool _canImport;

        [ObservableProperty]
        private bool _webViewLoaded;

        private string? _downloadedFilePath;

        [ObservableProperty]
        private ImportType _selectedImportType = ImportType.All;

        // Observable collections for UI
        public ObservableCollection<string> ImportMessages { get; } = new ObservableCollection<string>();

        public MainWindowViewModel(
            ILogger<MainWindowViewModel> logger,
            IConfigService configService,
            KiCad kiCad)
        {
            _logger = logger;
            _configService = configService;
            _kiCad = kiCad;
  
            // Ensure the download directory exists
            _configService.EnsureDownloadDirectoryExists();

            // Initialize KiCad client and importer
            InitializeKiCadClient();

            // Log that we're ready
            _logger.LogInformation($"MainWindowViewModel initialized, watching for downloads in {_configService.DownloadDirectory}");
        }

        public string WebviewUrl => "https://app.ultralibrarian.com/Account/Login?returnUrl=%252fsearch";

        // Method to update the WebView loaded status from MainWindow
        public void SetWebViewLoaded(bool isLoaded)
        {
            WebViewLoaded = isLoaded;
        }

        private void InitializeKiCadClient()
        {
            try
            {
                // Get the importer with current config
                //_importer = _kicadClientService.GetImporter(_configService.GetImportOptions());
                _logger.LogInformation("KiCad client initialized");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing KiCad client");
                StatusMessage = "Error: Could not connect to KiCad";
            }
        }

        private void OnFileCreated(object? sender, FileSystemEventArgs e)
        {
 
            LibraryDownloaded(e.FullPath);
        }

        public void LibraryDownloaded(string filePath)
        {
            // Only process ZIP files (UltraLibrarian typically downloads as ZIP)
            if (!filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return;
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                _downloadedFilePath = filePath;
                var fileName = Path.GetFileName(filePath);
                StatusMessage = $"Downloaded: {fileName}";
                CanImport = true;

                // Add message to the list
                ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] Downloaded: {Path.GetFileName(fileName)}");
                _logger.LogInformation($"Detected new file: {fileName}");
            });
        }

        [RelayCommand]
        private async Task OpenSettings()
        {
            try
            {
                _logger.LogInformation("Opening settings dialog");
                
                // Get the service provider from the application instance
                var serviceProvider = (App.Current as App)?._serviceProvider;
                
                // Create the settings window
                SettingsWindow settingsWindow;
                if (serviceProvider != null)
                {
                    // Use DI to get a properly configured SettingsViewModel
                    var viewModel = serviceProvider.GetRequiredService<SettingsViewModel>();
                    var options = serviceProvider.GetRequiredService<IOptionsMonitor<KiCadClientSettings>>();
                    settingsWindow = new SettingsWindow(_configService, _logger, options) { DataContext = viewModel };
                }
                else
                {
                    // Fallback to direct creation when DI is not available
                    settingsWindow = new SettingsWindow();
                }
                
                // Show the dialog and wait for the result
                var result = await settingsWindow.ShowDialog<bool>(App.MainWindow);
                
                if (result)
                {
                    _logger.LogInformation("Settings saved successfully");
                    
                    //// Reload any settings that might have changed
                    //_kicadClientService.UpdateEndpoint(_configService.IpcEndpoint);
                    
                    //// Recreate importer with updated import options
                    //_importer = _kicadClientService.GetImporter(_configService.GetImportOptions());
                    
                    //// Update file watcher if download directory changed
                    //_configService.EnsureDownloadDirectoryExists();
                    //_fileWatcherService.StartWatching(_configService.DownloadDirectory, "*.zip");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening settings dialog");
            }
        }

        [RelayCommand]
        private async Task ImportComponent()
        {
            if (_importer == null || string.IsNullOrEmpty(_downloadedFilePath) || !File.Exists(_downloadedFilePath))
            {
                StatusMessage = "No component available to import";
                return;
            }

            try
            {
                IsProgressVisible = true;
                ProgressValue = 0;
                StatusMessage = "Importing component...";
                CanImport = false;

                // Use a progress timer to show activity
                var progressTimer = new System.Timers.Timer(100);
                progressTimer.Elapsed += (s, e) =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ProgressValue = (ProgressValue + 1) % 100;
                    });
                };
                progressTimer.Start();

                // Import the component
                var result = await _importer.ImportComponentAsync(_downloadedFilePath, SelectedImportType);

                // Stop the timer
                progressTimer.Stop();

                // Update UI with result
                if (result.Success)
                {
                    StatusMessage = "Import completed successfully";
                    ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] Import succeeded");

                    foreach (var detail in result.Details)
                    {
                        ImportMessages.Add($"  - {detail}");
                    }
                }
                else
                {
                    StatusMessage = "Import failed";
                    ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] Import failed");

                    foreach (var detail in result.Details)
                    {
                        ImportMessages.Add($"  - {detail}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing component");
                StatusMessage = $"Error: {ex.Message}";
                ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] Error: {ex.Message}");
            }
            finally
            {
                IsProgressVisible = false;
                ProgressValue = 0;
            }
        }

        [RelayCommand]
        private void OpenDownloadsFolder()
        {
            try
            {
                if (Directory.Exists(_configService.DownloadDirectory))
                {
                    // Open downloads folder in file explorer
                    if (OperatingSystem.IsWindows())
                    {
                        System.Diagnostics.Process.Start("explorer.exe", _configService.DownloadDirectory);
                    }
                    else if (OperatingSystem.IsMacOS())
                    {
                        System.Diagnostics.Process.Start("open", _configService.DownloadDirectory);
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        System.Diagnostics.Process.Start("xdg-open", _configService.DownloadDirectory);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening downloads folder");
            }
        }

        [RelayCommand]
        private void ShowAbout()
        {
            try
            {
                _logger.LogInformation("Showing about dialog");
                
                // Create the about window with the KiCad instance
                AboutWindow aboutWindow = new AboutWindow(_logger, _kiCad);
                
                // Show the dialog
                aboutWindow.ShowDialog(App.MainWindow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing about dialog");
            }
        }

        internal void OnLibraryDowloaded(string resourcePath)
        {
         
        }
    }
}