using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using KiCadSharp;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.Views;

namespace UltraLibrarianImporter.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ILogger<MainViewModel> _logger;
        private readonly IConfigService _configService;
        private readonly KiCad _kiCad;
        private readonly IKiCadImportEngine _importEngine;
        private readonly IComponentProviderRegistry _providerRegistry;
        private readonly IPartAggregatorService _aggregatorService;

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

        [ObservableProperty]
        private Services.ImportType _selectedImportType = Services.ImportType.All;

        [ObservableProperty]
        private IComponentProvider _selectedProvider;

        [ObservableProperty]
        private string _webviewUrl = string.Empty;

        // Part Explorer Properties
        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private bool _isSearching;

        [ObservableProperty]
        private int _selectedTabIndex;

        [ObservableProperty]
        private PartSearchResult? _selectedSearchResult;

        public ObservableCollection<PartSearchResult> SearchResults { get; } = new ObservableCollection<PartSearchResult>();

        private string? _downloadedFilePath;

        public IReadOnlyList<IComponentProvider> AvailableProviders => _providerRegistry.Providers;

        public string DownloadDirectory => _configService.DownloadDirectory;

        public ObservableCollection<string> ImportMessages { get; } = new ObservableCollection<string>();

        public MainViewModel(
            ILogger<MainViewModel> logger,
            IConfigService configService,
            KiCad kiCad,
            IKiCadImportEngine importEngine,
            IComponentProviderRegistry providerRegistry,
            IPartAggregatorService aggregatorService)
        {
            _logger = logger;
            _configService = configService;
            _kiCad = kiCad;
            _importEngine = importEngine;
            _providerRegistry = providerRegistry;
            _aggregatorService = aggregatorService;

            _selectedProvider = _providerRegistry.SelectedProvider;
            _webviewUrl = SelectedProvider.SearchUrl;

            _configService.EnsureDownloadDirectoryExists();
            _logger.LogInformation("MainViewModel initialized for provider {Provider}. Watching for downloads in {Dir}",
                SelectedProvider.DisplayName, _configService.DownloadDirectory);
        }

        partial void OnSelectedProviderChanged(IComponentProvider value)
        {
            if (value != null)
            {
                _providerRegistry.SelectedProvider = value;
                WebviewUrl = value.SearchUrl;
                StatusMessage = $"Active provider: {value.DisplayName}";
                _logger.LogInformation("Switched component provider to {Provider} ({Url})", value.DisplayName, value.SearchUrl);
            }
        }

        public void SetWebViewLoaded(bool isLoaded)
        {
            WebViewLoaded = isLoaded;
        }

        [RelayCommand]
        private async Task SearchParts()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                StatusMessage = "Please enter a part number or keyword to search.";
                return;
            }

            try
            {
                IsSearching = true;
                StatusMessage = $"Searching all component providers for '{SearchQuery}'...";
                SearchResults.Clear();

                var results = await _aggregatorService.SearchAllProvidersAsync(SearchQuery);
                foreach (var r in results)
                {
                    SearchResults.Add(r);
                }

                StatusMessage = $"Found {SearchResults.Count} results across providers for '{SearchQuery}'.";
                _logger.LogInformation("Aggregated search completed for query: {Query}, found: {Count}", SearchQuery, SearchResults.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching components across providers");
                StatusMessage = $"Search error: {ex.Message}";
            }
            finally
            {
                IsSearching = false;
            }
        }

        [RelayCommand]
        private void OpenInWebBrowser(PartSearchResult? part)
        {
            if (part == null)
            {
                return;
            }

            // Find provider in registry and switch to it
            var provider = _providerRegistry.GetProvider(part.ProviderId);
            if (provider != null)
            {
                SelectedProvider = provider;
            }

            if (!string.IsNullOrEmpty(part.DatasheetUrl))
            {
                WebviewUrl = part.DatasheetUrl;
            }
            else if (provider != null)
            {
                WebviewUrl = provider.SearchUrl;
            }

            // Switch to the Web Browser tab
            SelectedTabIndex = 1;
            StatusMessage = $"Navigating to {part.PartNumber} on {part.ProviderName}...";
        }

        public void LibraryDownloaded(string filePath)
        {
            if (!SelectedProvider.CanHandleDownload(filePath) && !filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            IsProgressVisible = false;
            _downloadedFilePath = filePath;
            var fileName = Path.GetFileName(filePath);
            StatusMessage = $"Downloaded ({SelectedProvider.DisplayName}): {fileName}";
            CanImport = true;

            ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] [{SelectedProvider.DisplayName}] Downloaded: {fileName}");
            _logger.LogInformation("Detected download for {Provider}: {File}", SelectedProvider.DisplayName, fileName);

            if (_configService.AutoImportWhenDownloaded)
            {
                _logger.LogInformation("Auto-import enabled. Initiating import...");
                ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] Starting auto-import...");
                _ = ImportComponent();
            }
            else
            {
                ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] Ready to import. Click 'Import Component' to proceed.");
            }
        }

        [RelayCommand]
        private async Task OpenSettings()
        {
            try
            {
                _logger.LogInformation("Opening settings dialog");

                var serviceProvider = (App.Current as App)?._serviceProvider;
                SettingsWindow settingsWindow;

                if (serviceProvider != null)
                {
                    var viewModel = serviceProvider.GetRequiredService<SettingsViewModel>();
                    var options = serviceProvider.GetRequiredService<IOptionsMonitor<KiCadClientSettings>>();
                    settingsWindow = new SettingsWindow(_configService, _logger, options) { DataContext = viewModel };
                }
                else
                {
                    settingsWindow = new SettingsWindow();
                }

                var owner = App.MainWindow
                    ?? throw new InvalidOperationException("Cannot open settings dialog before main window exists.");

                var result = await settingsWindow.ShowDialog<bool>(owner);
                if (result)
                {
                    _logger.LogInformation("Settings saved successfully.");
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
            if (string.IsNullOrEmpty(_downloadedFilePath) || !File.Exists(_downloadedFilePath))
            {
                StatusMessage = "No component package available to import.";
                return;
            }

            try
            {
                IsProgressVisible = true;
                ProgressValue = 0;
                StatusMessage = $"Importing component from {SelectedProvider.DisplayName}...";
                CanImport = false;

                var progressTimer = new System.Timers.Timer(100);
                progressTimer.Elapsed += (s, e) =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ProgressValue = (ProgressValue + 1) % 100;
                    });
                };
                progressTimer.Start();

                var options = _configService.GetImportOptions();
                var result = await _importEngine.ImportAsync(SelectedProvider, _downloadedFilePath, SelectedImportType, options);

                progressTimer.Stop();

                if (result.Success)
                {
                    StatusMessage = $"Import completed ({SelectedProvider.DisplayName})";
                    ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] Import succeeded ({SelectedProvider.DisplayName})");

                    foreach (var detail in result.Details)
                    {
                        ImportMessages.Add($"  - {detail}");
                    }
                }
                else
                {
                    StatusMessage = "Import failed";
                    ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] Import failed ({SelectedProvider.DisplayName})");

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
                var aboutWindow = new AboutWindow(_logger, _kiCad);

                var aboutOwner = App.MainWindow
                    ?? throw new InvalidOperationException("Cannot open about dialog before main window exists.");
                aboutWindow.ShowDialog(aboutOwner);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing about dialog");
            }
        }

        internal void DownloadCancelled(string fullPath)
        {
            StatusMessage = "Download canceled.";
            IsProgressVisible = false;
        }

        internal void ReportDownloadProgressChanged(string fullPath, long receivedBytes, long totalBytes, int percentComplete)
        {
            StatusMessage = $"Downloading: {Path.GetFileName(fullPath)} ({percentComplete}%)...";
            ProgressValue = percentComplete;
        }

        internal void DownloadStarted(string filePath)
        {
            IsProgressVisible = true;
        }
    }
}