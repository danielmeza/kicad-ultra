using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using KiCadSharp;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UltraLibrarianImporter.UI.Services.EasyEda2KiCad;
using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.ViewModels;

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
    private readonly EasyEda2KiCadLocator? _easyEda2KiCadLocator;
    private readonly KiCadClientSettings _originalSettings;

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

    /// <summary>
    /// easyeda2kicad, or a Python interpreter that has it installed (#76). Empty to look on PATH and
    /// then in KiCad's Python.
    /// </summary>
    [ObservableProperty]
    private string _easyEda2KiCadPath = string.Empty;

    /// <summary>What the last "Check" found, for the path as typed (saved or not).</summary>
    [ObservableProperty]
    private string _easyEda2KiCadStatus = string.Empty;

    /// <summary>The command that installs easyeda2kicad on this system.</summary>
    public string EasyEda2KiCadInstallCommand => EasyEda2KiCadLocator.InstallHelp.Command;

    public string EasyEda2KiCadInstallNote => EasyEda2KiCadLocator.InstallHelp.Note;

    // Provider configuration
    public ObservableCollection<ProviderConfigItemViewModel> ConfiguredProviders { get; } = [];
    public ObservableCollection<string> AvailableProviderIds { get; } = [];

    /// <summary>Where the API keys below are kept - the OS credential store, or this session only.</summary>
    public string SecretStorageMessage => _configService.SecretStorage.Message;

    /// <summary>True when the OS credential store could not be used and API keys will not survive a restart.</summary>
    public bool IsSecretStorageSessionOnly => !_configService.SecretStorage.IsPersistent;

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
            var safePath = McpExecutablePath.Replace('\\', '/');
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
    public event EventHandler<EventArgs>? BrowseForEasyEda2KiCadRequested;
    public event EventHandler<bool>? SettingsSaved;
    public event EventHandler<string>? CopyToClipboardRequested;

    public SettingsViewModel(
        IConfigService configService,
        ILogger<SettingsViewModel> logger,
        IOptionsMonitor<KiCadClientSettings> kicadSettings,
        IComponentProviderRegistry? providerRegistry = null,
        EasyEda2KiCadLocator? easyEda2KiCadLocator = null)
    {
        _configService = configService;
        _logger = logger;
        _kicadSettings = kicadSettings;
        _providerRegistry = providerRegistry;
        _easyEda2KiCadLocator = easyEda2KiCadLocator;

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
        EasyEda2KiCadPath = _configService.EasyEda2KiCadPath;

        SelectedDefaultProviderId = string.IsNullOrEmpty(_configService.DefaultProviderId)
            ? "ultralibrarian"
            : _configService.DefaultProviderId;

        // Initialize Provider list
        ConfiguredProviders.Clear();
        AvailableProviderIds.Clear();

        IReadOnlyList<IComponentProvider>? providers = _providerRegistry?.AllProviders;
        if (providers != null && providers.Count > 0)
        {
            foreach (IComponentProvider p in providers)
            {
                AvailableProviderIds.Add(p.Id);

                var reqKey = p.Id is "octopart" or "snapeda" or "samacsys";
                var key = p.Id switch
                {
                    "octopart" => _configService.OctopartApiToken,
                    "snapeda" => _configService.SnapEdaApiKey,
                    "samacsys" => _configService.SamacSysApiKey,
                    _ => string.Empty
                };

                var caps = p.Id switch
                {
                    "easyeda" => "Direct API: Yes • Stock & Pricing from jlcsearch.tscircuit.com, a third-party index of JLCPCB parts • Symbols, footprints and 3D models through easyeda2kicad, an optional third-party tool (below)",
                    "octopart" => "Direct API: Yes • Multi-Distributor Stock & Pricing • Datasheets",
                    "snapeda" => "Direct API: Yes • SnapMagic Symbols, Footprints, 3D Models",
                    "samacsys" => "Direct API: Yes • SamacSys / Component Search Engine CAD Models",
                    "ultralibrarian" => "Browser-Assisted • Official UltraLibrarian CAD Models & 3D Assets",
                    _ => "Component Library Provider"
                };

                var placeholder = p.Id switch
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
            AddFallbackProvider("easyeda", "EasyEDA / LCSC", true, "Direct API • Stock & Pricing from third-party jlcsearch.tscircuit.com", false, "");
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
            KiCadClientSettings currentSettings = _kicadSettings.CurrentValue;
            currentSettings.PipeName = PipeName;
            currentSettings.Token = Token;

            _configService.DownloadDirectory = DownloadDirectory;
            _configService.AddToGlobalLibrary = AddToGlobalLibrary;
            _configService.CleanupAfterImport = CleanupAfterImport;
            _configService.TargetPath = TargetPath;
            _configService.UseProjectPath = UseProjectPath;
            _configService.AutoImportWhenDownloaded = AutoImportWhenDownloaded;
            _configService.LibraryName = LibraryName;
            _configService.EasyEda2KiCadPath = EasyEda2KiCadPath.Trim();

            _configService.DefaultProviderId = SelectedDefaultProviderId;

            foreach (ProviderConfigItemViewModel p in ConfiguredProviders)
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
    private void BrowseEasyEda2KiCad()
    {
        BrowseForEasyEda2KiCadRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Looks for easyeda2kicad with the path as it is typed now, before it is saved.</summary>
    [RelayCommand]
    private async Task CheckEasyEda2KiCad()
    {
        if (_easyEda2KiCadLocator is null)
        {
            EasyEda2KiCadStatus = "easyeda2kicad cannot be checked from here.";
            return;
        }

        EasyEda2KiCadStatus = "Looking for easyeda2kicad...";
        try
        {
            EasyEda2KiCadDetection detection = await _easyEda2KiCadLocator.LocateAsync(EasyEda2KiCadPath);
            EasyEda2KiCadStatus = detection.Command is { } command
                ? $"Found: {command.Description}."
                : $"Not found. {string.Join(" ", detection.Attempts)}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking for easyeda2kicad");
            EasyEda2KiCadStatus = $"Could not look for easyeda2kicad: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        KiCadClientSettings currentSettings = _kicadSettings.CurrentValue;
        currentSettings.PipeName = _originalSettings.PipeName;
        currentSettings.Token = _originalSettings.Token;

        SettingsSaved?.Invoke(this, false);
    }
}
