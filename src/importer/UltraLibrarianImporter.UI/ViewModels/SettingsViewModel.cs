using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using KiCadSharp;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UltraLibrarianImporter.UI.Services;
using UltraLibrarianImporter.UI.Services.EasyEda2KiCad;
using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.Services.Providers.Jlcpcb;

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
    private readonly ProviderResponseCache? _responseCache;
    private readonly KiCadClientSettings _originalSettings;

    [ObservableProperty]
    private string _pipeName = string.Empty;

    [ObservableProperty]
    private string _token = string.Empty;

    [ObservableProperty]
    private string _clientName = string.Empty;

    [ObservableProperty]
    private string _downloadDirectory = string.Empty;

    /// <summary>Why Save refused the download folder, shown under it. Empty when it did not (#112).</summary>
    [ObservableProperty]
    private string _downloadDirectoryError = string.Empty;

    /// <summary>The selected tab, so that a refused Save can show the error on the General tab.</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>Which of KiCad's library tables imported libraries are registered in (#71).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAutomaticRegistration), nameof(IsProjectRegistration), nameof(IsGlobalRegistration))]
    private LibraryRegistrationScope _registrationScope;

    // One per radio button. The button a new choice unchecks writes false, which changes nothing.
    public bool IsAutomaticRegistration
    {
        get => RegistrationScope == LibraryRegistrationScope.Automatic;
        set => SelectRegistrationScope(LibraryRegistrationScope.Automatic, value);
    }

    public bool IsProjectRegistration
    {
        get => RegistrationScope == LibraryRegistrationScope.Project;
        set => SelectRegistrationScope(LibraryRegistrationScope.Project, value);
    }

    public bool IsGlobalRegistration
    {
        get => RegistrationScope == LibraryRegistrationScope.Global;
        set => SelectRegistrationScope(LibraryRegistrationScope.Global, value);
    }

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

    // The user's own JLCPCB API credentials (#51). Kept in the credential store like the API keys
    // below; all three empty means EasyEDA / LCSC search uses the unofficial endpoint (#52).
    [ObservableProperty]
    private string _jlcpcbAppId = string.Empty;

    [ObservableProperty]
    private string _jlcpcbAccessKey = string.Empty;

    [ObservableProperty]
    private string _jlcpcbSecretKey = string.Empty;

    /// <summary>JLCPCB's guide to applying for API access.</summary>
    public Uri JlcpcbApiGuideUri { get; } = new(JlcpcbApiCredentials.ApiGuideUrl);

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
        EasyEda2KiCadLocator? easyEda2KiCadLocator = null,
        ProviderResponseCache? responseCache = null)
    {
        _configService = configService;
        _logger = logger;
        _kicadSettings = kicadSettings;
        _providerRegistry = providerRegistry;
        _easyEda2KiCadLocator = easyEda2KiCadLocator;
        _responseCache = responseCache;

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
        RegistrationScope = _configService.RegistrationScope;
        CleanupAfterImport = _configService.CleanupAfterImport;
        TargetPath = _configService.TargetPath;
        UseProjectPath = _configService.UseProjectPath;
        AutoImportWhenDownloaded = _configService.AutoImportWhenDownloaded;
        LibraryName = _configService.LibraryName;
        EasyEda2KiCadPath = _configService.EasyEda2KiCadPath;
        JlcpcbAppId = _configService.JlcpcbAppId;
        JlcpcbAccessKey = _configService.JlcpcbAccessKey;
        JlcpcbSecretKey = _configService.JlcpcbSecretKey;

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

                var reqKey = ReadsApiKey(p.Id);

                ConfiguredProviders.Add(new ProviderConfigItemViewModel
                {
                    Id = p.Id,
                    DisplayName = p.DisplayName,
                    SearchUrl = p.SearchUrl,
                    SupportsDirectApi = p.SupportsDirectApi,
                    CapabilitiesDescription = DescribeCapabilities(p.Id),
                    RequiresApiKey = reqKey,
                    ApiKeyPlaceholder = reqKey ? "Nexar API Bearer token..." : string.Empty,
                    IsEnabled = _configService.IsProviderEnabled(p.Id),
                    ApiKey = reqKey ? _configService.OctopartApiToken : string.Empty
                });
            }
        }
        else
        {
            // Fallback default list if no registry provided
            AddFallbackProvider("ultralibrarian", "UltraLibrarian", false);
            AddFallbackProvider("easyeda", "EasyEDA / LCSC", true);
            AddFallbackProvider("octopart", "Octopart (Nexar)", true);
            AddFallbackProvider("snapeda", "SnapEDA (SnapMagic)", false);
            AddFallbackProvider("componentsearchengine", "Component Search Engine (SamacSys)", false);
        }
    }

    private void SelectRegistrationScope(LibraryRegistrationScope scope, bool selected)
    {
        if (selected)
        {
            RegistrationScope = scope;
        }
    }

    // The error was about the folder as it was; the next Save checks the new one.
    partial void OnDownloadDirectoryChanged(string value) => DownloadDirectoryError = string.Empty;

    // Since #111 the browser saves into this folder as it is given, so a relative one would resolve
    // against whatever directory the app happens to run in (#112). Returns why it is refused, or null.
    private static string? CheckDownloadDirectory(string directory) =>
        string.IsNullOrWhiteSpace(directory)
            ? "Not saved: enter the download folder as a full path, or choose one with Browse."
        : !Path.IsPathFullyQualified(directory)
            ? $"Not saved: \"{directory}\" is not a full path. A relative folder would depend on the directory the app was started in. Enter a full path, or choose a folder with Browse."
        : null;

    private void AddFallbackProvider(string id, string name, bool directApi)
    {
        var reqKey = ReadsApiKey(id);
        AvailableProviderIds.Add(id);
        ConfiguredProviders.Add(new ProviderConfigItemViewModel
        {
            Id = id,
            DisplayName = name,
            SupportsDirectApi = directApi,
            CapabilitiesDescription = DescribeCapabilities(id),
            RequiresApiKey = reqKey,
            ApiKeyPlaceholder = reqKey ? "Nexar API Bearer token..." : string.Empty,
            IsEnabled = _configService.IsProviderEnabled(id),
            ApiKey = reqKey ? _configService.OctopartApiToken : string.Empty
        });
    }

    // Only Octopart reads a key. SnapEDA's and SamacSys's stay in the credential store as they are,
    // but nothing reads them (#56, #57), so Settings neither asks for them nor rewrites them.
    private static bool ReadsApiKey(string providerId) => providerId == "octopart";

    // What each provider does today, keyed by its real Id. Claim nothing it does not do (#48): a
    // provider that makes no call is not "Direct API", and one whose search cannot tell whether a part
    // has a symbol, footprint or 3D model does not list them.
    private static string DescribeCapabilities(string providerId) => providerId switch
    {
        "easyeda" => "Direct API: Yes • Stock, pricing and datasheets from JLCPCB's parts library: LCSC numbers through JLCPCB's official API with your own credentials (below), everything else through an unofficial JLCPCB website endpoint that can stop working without notice • Search cannot tell whether a part has a symbol, footprint or 3D model: importing it with easyeda2kicad, an optional third-party tool (below), converts whichever of them EasyEDA has",
        "octopart" => "Direct API: Yes • Multi-Distributor Stock & Pricing • Datasheets and CAD availability where your Nexar plan includes them",
        "snapeda" => "Browser-Assisted • No direct search: SnapMagic has no public API, and grants API keys per application after review (github.com/danielmeza/kicad-ultra/issues/56)",
        "componentsearchengine" => "Browser-Assisted • No direct search integration yet (github.com/danielmeza/kicad-ultra/issues/57)",
        "ultralibrarian" => "Browser-Assisted • Official UltraLibrarian CAD Models & 3D Assets",
        _ => "Component Library Provider"
    };

    [RelayCommand]
    private void CopyMcpConfig()
    {
        CopyToClipboardRequested?.Invoke(this, McpConfigSnippet);
        McpCopyStatusMessage = "✓ MCP configuration copied to clipboard!";
    }

    [RelayCommand]
    private void Save()
    {
        // Checked before anything is written: a refused folder leaves every setting as it was, and
        // the dialog open on the General tab with the reason under the folder.
        if (CheckDownloadDirectory(DownloadDirectory) is { } downloadDirectoryError)
        {
            DownloadDirectoryError = downloadDirectoryError;
            SelectedTabIndex = 0;
            _logger.LogWarning("Settings not saved: the download folder \"{Directory}\" is not a full path", DownloadDirectory);
            return;
        }

        try
        {
            KiCadClientSettings currentSettings = _kicadSettings.CurrentValue;
            currentSettings.PipeName = PipeName;
            currentSettings.Token = Token;

            _configService.DownloadDirectory = DownloadDirectory;
            _configService.RegistrationScope = RegistrationScope;
            _configService.CleanupAfterImport = CleanupAfterImport;
            _configService.TargetPath = TargetPath;
            _configService.UseProjectPath = UseProjectPath;
            _configService.AutoImportWhenDownloaded = AutoImportWhenDownloaded;
            _configService.LibraryName = LibraryName;
            _configService.EasyEda2KiCadPath = EasyEda2KiCadPath.Trim();
            _configService.JlcpcbAppId = JlcpcbAppId.Trim();
            _configService.JlcpcbAccessKey = JlcpcbAccessKey.Trim();
            _configService.JlcpcbSecretKey = JlcpcbSecretKey.Trim();

            _configService.DefaultProviderId = SelectedDefaultProviderId;

            foreach (ProviderConfigItemViewModel p in ConfiguredProviders)
            {
                _configService.SetProviderEnabled(p.Id, p.IsEnabled);
                // Only keys Settings shows are written back; see ReadsApiKey.
                if (p.Id == "octopart") _configService.OctopartApiToken = p.ApiKey ?? string.Empty;
            }

            _configService.Save();
            _configService.EnsureDownloadDirectoryExists();

            // A cached answer may come from a source these settings no longer select.
            _responseCache?.Clear();
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
