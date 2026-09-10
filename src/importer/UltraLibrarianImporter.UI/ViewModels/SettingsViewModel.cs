using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using KiCadSharp;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.ViewModels
{
    public partial class ProviderConfigItemViewModel : ObservableObject
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string SearchUrl { get; init; } = string.Empty;
        public bool SupportsDirectApi { get; init; }
        public string CapabilitiesDescription { get; init; } = string.Empty;
        public bool RequiresApiKey { get; init; }
        public string ApiKeyPlaceholder { get; init; } = string.Empty;

        [ObservableProperty]
        private bool _isEnabled = true;

        [ObservableProperty]
        private string _apiKey = string.Empty;
    }

    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IConfigService _configService;
        private readonly ILogger<SettingsViewModel> _logger;
        private readonly IOptionsMonitor<KiCadClientSettings> _kicadSettings;
        private readonly IComponentProviderRegistry? _providerRegistry;
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

        // Provider configuration
        public ObservableCollection<ProviderConfigItemViewModel> ConfiguredProviders { get; } = new();
        public ObservableCollection<string> AvailableProviderIds { get; } = new();

        [ObservableProperty]
        private string _selectedDefaultProviderId = "ultralibrarian";

        // AI & MCP Properties
        [ObservableProperty]
        private string _mcpCopyStatusMessage = string.Empty;

        public string McpExecutablePath
        {
            get
            {
                try
                {
                    return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                        ?? Environment.ProcessPath
                        ?? "UltraLibrarianImporter.UI.exe";
                }
                catch
                {
                    return Environment.ProcessPath ?? "UltraLibrarianImporter.UI.exe";
                }
            }
        }

        public string McpConfigSnippet
        {
            get
            {
                string safePath = McpExecutablePath.Replace('\\', '/');
                return "{\n" +
                       "  \"mcpServers\": {\n" +
                       "    \"kicad-component-explorer\": {\n" +
                       $"      \"command\": \"{safePath}\",\n" +
                       "      \"args\": [\"--mcp\"]\n" +
                       "    }\n" +
                       "  }\n" +
                       "}";
            }
        }

        // Events
        public event EventHandler<EventArgs>? BrowseForFolderRequested;
        public event EventHandler<EventArgs>? BrowseForTargetPathRequested;
        public event EventHandler<bool>? SettingsSaved;
        public event EventHandler<string>? CopyToClipboardRequested;

        public SettingsViewModel(
            IConfigService configService, 
            ILogger<SettingsViewModel> logger,
            IOptionsMonitor<KiCadClientSettings> kicadSettings,
            IComponentProviderRegistry? providerRegistry = null)
        {
            _configService = configService;
            _logger = logger;
            _kicadSettings = kicadSettings;
            _providerRegistry = providerRegistry;
            
            _originalSettings = new KiCadClientSettings
            {
                PipeName = _kicadSettings.CurrentValue.PipeName,
                Token = _kicadSettings.CurrentValue.Token,
            };

            InitializeSettings();
            _logger.LogInformation("SettingsViewModel initialized");
        }
        
        private void InitializeSettings()
        {
            PipeName = _kicadSettings.CurrentValue.PipeName ?? 
                      KiCadEnvironment.GetDefaultSocketPath();
            Token = _kicadSettings.CurrentValue.Token ?? 
                   KiCadEnvironment.GetApiToken() ?? string.Empty;
            ClientName = _kicadSettings.CurrentValue.ClientName ?? 
                         KiCadEnvironment.GenerateRandomClientName();
            
            DownloadDirectory = _configService.DownloadDirectory;
            AddToGlobalLibrary = _configService.AddToGlobalLibrary;
            CleanupAfterImport = _configService.CleanupAfterImport;
            TargetPath = _configService.TargetPath;
            UseProjectPath = _configService.UseProjectPath;
            AutoImportWhenDownloaded = _configService.AutoImportWhenDownloaded;
            LibraryName = _configService.LibraryName;

            SelectedDefaultProviderId = string.IsNullOrEmpty(_configService.DefaultProviderId)
                ? "ultralibrarian"
                : _configService.DefaultProviderId;

            // Initialize Provider list
            ConfiguredProviders.Clear();
            AvailableProviderIds.Clear();

            var providers = _providerRegistry?.AllProviders;
            if (providers != null && providers.Count > 0)
            {
                foreach (var p in providers)
                {
                    AvailableProviderIds.Add(p.Id);

                    bool reqKey = p.Id is "octopart" or "snapeda" or "samacsys";
                    string key = p.Id switch
                    {
                        "octopart" => _configService.OctopartApiToken,
                        "snapeda" => _configService.SnapEdaApiKey,
                        "samacsys" => _configService.SamacSysApiKey,
                        _ => string.Empty
                    };

                    string caps = p.Id switch
                    {
                        "easyeda" => "Direct API: Yes • JLCPCB / LCSC Stock & Pricing • Symbols, Footprints, 3D Models",
                        "octopart" => "Direct API: Yes • Multi-Distributor Stock & Pricing • Datasheets",
                        "snapeda" => "Direct API: Yes • SnapMagic Symbols, Footprints, 3D Models",
                        "samacsys" => "Direct API: Yes • SamacSys / Component Search Engine CAD Models",
                        "ultralibrarian" => "Browser-Assisted • Official UltraLibrarian CAD Models & 3D Assets",
                        _ => "Component Library Provider"
                    };

                    string placeholder = p.Id switch
                    {
                        "octopart" => "Nexar API Bearer token...",
                        "snapeda" => "SnapEDA / SnapMagic API key...",
                        "samacsys" => "SamacSys / Component Search Engine key...",
                        _ => "API Key / Token..."
                    };

                    ConfiguredProviders.Add(new ProviderConfigItemViewModel
                    {
                        Id = p.Id,
                        DisplayName = p.DisplayName,
                        SearchUrl = p.SearchUrl,
                        SupportsDirectApi = p.SupportsDirectApi,
                        CapabilitiesDescription = caps,
                        RequiresApiKey = reqKey,
                        ApiKeyPlaceholder = placeholder,
                        IsEnabled = _configService.IsProviderEnabled(p.Id),
                        ApiKey = key
                    });
                }
            }
            else
            {
                // Fallback default list if no registry provided
                AddFallbackProvider("ultralibrarian", "UltraLibrarian", false, "Browser-Assisted • CAD Models & 3D Assets", false, "");
                AddFallbackProvider("easyeda", "EasyEDA / LCSC", true, "Direct API • JLCPCB / LCSC Stock & Pricing", false, "");
                AddFallbackProvider("octopart", "Octopart (Nexar)", true, "Direct API • Multi-Distributor Stock & Pricing", true, _configService.OctopartApiToken);
                AddFallbackProvider("snapeda", "SnapEDA / SnapMagic", true, "Direct API • CAD Models & Footprints", true, _configService.SnapEdaApiKey);
                AddFallbackProvider("samacsys", "Component Search Engine", true, "Direct API • SamacSys CAD Models", true, _configService.SamacSysApiKey);
            }
        }

        private void AddFallbackProvider(string id, string name, bool directApi, string caps, bool reqKey, string key)
        {
            AvailableProviderIds.Add(id);
            ConfiguredProviders.Add(new ProviderConfigItemViewModel
            {
                Id = id,
                DisplayName = name,
                SupportsDirectApi = directApi,
                CapabilitiesDescription = caps,
                RequiresApiKey = reqKey,
                ApiKeyPlaceholder = $"{name} API key...",
                IsEnabled = _configService.IsProviderEnabled(id),
                ApiKey = key
            });
        }

        [RelayCommand]
        private void CopyMcpConfig()
        {
            CopyToClipboardRequested?.Invoke(this, McpConfigSnippet);
            McpCopyStatusMessage = "✓ MCP configuration copied to clipboard!";
        }

        [RelayCommand]
        private void Save()
        {
            try
            {
                var currentSettings = _kicadSettings.CurrentValue;
                currentSettings.PipeName = PipeName;
                currentSettings.Token = Token;
                
                _configService.DownloadDirectory = DownloadDirectory;
                _configService.AddToGlobalLibrary = AddToGlobalLibrary;
                _configService.CleanupAfterImport = CleanupAfterImport;
                _configService.TargetPath = TargetPath;
                _configService.UseProjectPath = UseProjectPath;
                _configService.AutoImportWhenDownloaded = AutoImportWhenDownloaded;
                _configService.LibraryName = LibraryName;

                _configService.DefaultProviderId = SelectedDefaultProviderId;

                foreach (var p in ConfiguredProviders)
                {
                    _configService.SetProviderEnabled(p.Id, p.IsEnabled);
                    if (p.Id == "octopart") _configService.OctopartApiToken = p.ApiKey ?? string.Empty;
                    if (p.Id == "snapeda") _configService.SnapEdaApiKey = p.ApiKey ?? string.Empty;
                    if (p.Id == "samacsys") _configService.SamacSysApiKey = p.ApiKey ?? string.Empty;
                }

                _configService.Save();
                _configService.EnsureDownloadDirectoryExists();

                _providerRegistry?.RefreshProviders();

                _logger.LogInformation("Settings saved successfully.");
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
            BrowseForFolderRequested?.Invoke(this, EventArgs.Empty);
        }
        
        [RelayCommand]
        private void BrowseTargetPath()
        {
            BrowseForTargetPathRequested?.Invoke(this, EventArgs.Empty);
        }
        
        [RelayCommand]
        private void Cancel()
        {
            var currentSettings = _kicadSettings.CurrentValue;
            currentSettings.PipeName = _originalSettings.PipeName;
            currentSettings.Token = _originalSettings.Token;
            
            SettingsSaved?.Invoke(this, false);
        }
    }
}