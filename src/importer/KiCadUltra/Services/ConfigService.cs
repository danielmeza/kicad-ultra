using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using KiCadUltra.Services.Interfaces;
using KiCadUltra.Services.Secrets;
using Microsoft.Extensions.Logging;

namespace KiCadUltra.Services;

/// <summary>
/// Service for managing application configuration
/// </summary>
/// <remarks>
/// Two stores, split by sensitivity. Everything that is not a secret goes to <c>config.json</c>
/// through the <see cref="ConfigData"/> DTO. The provider API keys and the JLCPCB API credentials
/// (#51) go to the OS credential store through <see cref="ISecretStore"/> and are never added to the
/// file (#54): <c>config.json</c>
/// gets shared for troubleshooting, and the Settings dialog masking the keys implied a protection
/// the file never had. <see cref="LoadSecrets"/> moves keys an earlier version left in the file;
/// <see cref="SaveSecrets"/> describes what happens when the credential store cannot be used.
/// </remarks>
public class ConfigService : IConfigService
{
    /// <summary>The name the Octopart / Nexar token is filed under in the credential store.</summary>
    public const string OctopartApiTokenKey = "octopart-api-token";

    /// <summary>The name the SnapEDA key is filed under in the credential store.</summary>
    public const string SnapEdaApiKeyKey = "snapeda-api-key";

    /// <summary>The name the SamacSys key is filed under in the credential store.</summary>
    public const string SamacSysApiKeyKey = "samacsys-api-key";

    /// <summary>The name the JLCPCB API App ID is filed under in the credential store.</summary>
    public const string JlcpcbAppIdKey = "jlcpcb-app-id";

    /// <summary>The name the JLCPCB API access key is filed under in the credential store.</summary>
    public const string JlcpcbAccessKeyKey = "jlcpcb-access-key";

    /// <summary>The name the JLCPCB API secret key is filed under in the credential store.</summary>
    public const string JlcpcbSecretKeyKey = "jlcpcb-secret-key";

    /// <summary>
    /// Every key this application files a secret under, in one place: the credential store is a
    /// key-value store with no way to list what is in it, so anything that has to visit all of them
    /// - <see cref="SecretStoreMigration"/> after the rename in #132 - needs the list.
    /// </summary>
    public static IReadOnlyList<string> SecretKeys { get; } =
        [OctopartApiTokenKey, SnapEdaApiKeyKey, SamacSysApiKeyKey, JlcpcbAppIdKey, JlcpcbAccessKeyKey, JlcpcbSecretKeyKey];

    private readonly ILogger<ConfigService> _logger;
    private readonly ISecretStore _secretStore;
    private readonly string _configFilePath;

    // What the credential store is known to hold, by key, as of the last read or write that
    // succeeded. A key is absent while the store could not be read, which is what stops Save from
    // deleting a secret this instance never saw.
    private readonly Dictionary<string, string> _storedSecrets = new(StringComparer.Ordinal);

    // Cleartext keys found in a config.json written before #54 that could not be moved into the
    // credential store yet. Save writes these - and only these - back to the file unchanged, so an
    // unavailable store does not cost the user a key; they move on the first Load or Save that
    // reaches the store, and one the user changes or clears in Settings is dropped from the file.
    private readonly Dictionary<string, string> _unmigratedSecrets = new(StringComparer.Ordinal);

    // Default values
    private const string DEFAULT_DOWNLOAD_DIR = "";

    // Configuration properties
    public string DownloadDirectory { get; set; } = DEFAULT_DOWNLOAD_DIR;
    public LibraryRegistrationScope RegistrationScope { get; set; } = LibraryRegistrationScope.Automatic;
    public bool CleanupAfterImport { get; set; } = true;
    public string TargetPath { get; set; } = string.Empty;
    public bool UseProjectPath { get; set; } = true;
    public bool AutoImportWhenDownloaded { get; set; } = true;
    public string LibraryName { get; set; } = string.Empty;
    public string EasyEda2KiCadPath { get; set; } = string.Empty;
    public string OctopartApiToken { get; set; } = string.Empty;
    public string SnapEdaApiKey { get; set; } = string.Empty;
    public string SamacSysApiKey { get; set; } = string.Empty;
    public string JlcpcbAppId { get; set; } = string.Empty;
    public string JlcpcbAccessKey { get; set; } = string.Empty;
    public string JlcpcbSecretKey { get; set; } = string.Empty;
    public SecretStorageStatus SecretStorage { get; private set; } = new(false, "API keys have not been loaded yet.");
    public string DefaultProviderId { get; set; } = "ultralibrarian";
    public Dictionary<string, bool> EnabledProviders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsProviderEnabled(string providerId)
    {
        // Unknown providers default to enabled; an empty id never is.
        return !string.IsNullOrEmpty(providerId) && (!EnabledProviders.TryGetValue(providerId, out var enabled) || enabled);
    }

    public void SetProviderEnabled(string providerId, bool isEnabled)
    {
        if (string.IsNullOrEmpty(providerId)) return;
        EnabledProviders[providerId] = isEnabled;
    }

    /// <summary>
    /// Creates a new instance of the configuration service, keeping API keys in the credential
    /// store for this operating system (<see cref="PlatformSecretStore.Create"/>).
    /// </summary>
    /// <param name="logger">Logger for recording operations</param>
    public ConfigService(ILogger<ConfigService> logger)
        : this(logger, PlatformSecretStore.Create())
    {
    }

    /// <summary>
    /// Creates a new instance of the configuration service
    /// </summary>
    /// <param name="logger">Logger for recording operations</param>
    /// <param name="secretStore">Where the provider API keys are persisted</param>
    public ConfigService(ILogger<ConfigService> logger, ISecretStore secretStore)
    {
        _logger = logger;
        _secretStore = secretStore;

        // AppDataFolder, not a folder name spelled out here: it is the one place that knows where
        // this application keeps things, and the one that moved what the pre-#132 names left behind.
        var appDataDir = AppDataFolder.Current;

        _ = Directory.CreateDirectory(appDataDir);
        _configFilePath = Path.Combine(appDataDir, "config.json");
        _logger.LogInformation("Configuration file: {ConfigFile}", _configFilePath);

        // Set default download directory if not specified. ~/Documents is not created here; it is
        // created with the download directory, by EnsureDownloadDirectoryExists or a download.
        if (string.IsNullOrEmpty(DownloadDirectory))
        {
            DownloadDirectory = Path.Combine(
                SpecialFolders.GetPath(Environment.SpecialFolder.MyDocuments),
                "UltraLibrarianDownloads");
        }

        // Load configuration from file
        Load();
    }

    private class ConfigData
    {
        public string? DownloadDirectory { get; set; }

        // A LibraryRegistrationScope by name (#71). Kept as a string so that a value this version does
        // not know costs only this setting, where an enum would fail the whole file.
        public string? RegistrationScope { get; set; }

        public bool CleanupAfterImport { get; set; } = true;
        public string? TargetPath { get; set; }
        public bool UseProjectPath { get; set; } = true;
        public bool AutoImportWhenDownloaded { get; set; } = true;
        public string? LibraryName { get; set; }
        public string? EasyEda2KiCadPath { get; set; }
        public string? DefaultProviderId { get; set; }
        public Dictionary<string, bool>? EnabledProviders { get; set; }

        // Releases before #54 kept the provider API keys here in cleartext. Load reads them only
        // to move them into the credential store. Save leaves them null, and null is omitted, so
        // the file carries no secret at all - except a key that could not be moved yet, which is
        // written back as it was rather than lost (see _unmigratedSecrets).
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OctopartApiToken { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SnapEdaApiKey { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SamacSysApiKey { get; set; }

        // Releases before #71 had this boolean where RegistrationScope is now. Load reads it only to
        // migrate it (ReadRegistrationScope); Save leaves it null, so it is dropped from the file.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? AddToGlobalLibrary { get; set; }
    }

    /// <summary>
    /// Loads the configuration from the config file, and the API keys from the credential store
    /// </summary>
    public void Load()
    {
        ConfigData? config = null;
        var fileExists = File.Exists(_configFilePath);
        var droppedRelativeDownloadDirectory = false;
        var droppedRelativeTargetPath = false;
        var scopeMigrated = false;
        try
        {
            if (fileExists)
            {
                var json = File.ReadAllText(_configFilePath);
                config = JsonSerializer.Deserialize<ConfigData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (config != null)
                {
                    if (config.DownloadDirectory is { Length: > 0 } savedDownloadDirectory
                        && !Path.IsPathFullyQualified(savedDownloadDirectory))
                    {
                        // Until #70 the default became the relative path "UltraLibrarianDownloads"
                        // when ~/Documents did not exist yet, and Save wrote it here. A relative
                        // directory resolves against whatever the working directory is, so keep the
                        // default, and rewrite the file with it below.
                        _logger.LogWarning("Ignoring the relative download directory {SavedDirectory} in config.json; using {DownloadDirectory}", savedDownloadDirectory, DownloadDirectory);
                        droppedRelativeDownloadDirectory = true;
                    }
                    else
                    {
                        DownloadDirectory = config.DownloadDirectory ?? DownloadDirectory;
                    }

                    RegistrationScope = ReadRegistrationScope(config, out scopeMigrated);
                    CleanupAfterImport = config.CleanupAfterImport;
                    if (!string.IsNullOrWhiteSpace(config.TargetPath) && !Path.IsPathFullyQualified(config.TargetPath))
                    {
                        // Settings accepted a relative target path until #112, and the import engine
                        // writes libraries straight into it. Drop it like a relative download
                        // directory: empty means not set, so imports go to the default location.
                        _logger.LogWarning("Ignoring the relative target path {SavedTargetPath} in config.json", config.TargetPath);
                        TargetPath = string.Empty;
                        droppedRelativeTargetPath = true;
                    }
                    else
                    {
                        TargetPath = config.TargetPath ?? string.Empty;
                    }

                    UseProjectPath = config.UseProjectPath;
                    AutoImportWhenDownloaded = config.AutoImportWhenDownloaded;
                    LibraryName = config.LibraryName ?? string.Empty;
                    EasyEda2KiCadPath = config.EasyEda2KiCadPath ?? string.Empty;
                    DefaultProviderId = string.IsNullOrEmpty(config.DefaultProviderId) ? "ultralibrarian" : config.DefaultProviderId;
                    EnabledProviders = config.EnabledProviders != null
                        ? new Dictionary<string, bool>(config.EnabledProviders, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                }

                _logger.LogInformation("Configuration loaded from file");
            }
            else
            {
                _logger.LogInformation("No configuration file found, using defaults");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading configuration");
        }

        var migrated = LoadSecrets(config);

        // Create the default config file, or rewrite one whose cleartext keys were just moved into
        // the credential store or whose relative download directory or target path was dropped. A
        // file that exists but failed to parse is left alone.
        if (!fileExists || migrated || droppedRelativeDownloadDirectory || droppedRelativeTargetPath)
        {
            Save();
        }
        else if (scopeMigrated)
        {
            // Only the file needs rewriting: the credential store was just read, so it is not asked
            // again, which could prompt to unlock a keyring a second time.
            WriteFile();
        }
    }

    /// <summary>
    /// The library registration scope <paramref name="config"/> holds (#71).
    /// </summary>
    /// <remarks>
    /// A file written before #71 has the <c>AddToGlobalLibrary</c> boolean instead, and
    /// <paramref name="migrated"/> then asks for the file to be rewritten without it. Its <c>true</c>
    /// was the default, not a choice anyone made, and meant the global table even with a project open,
    /// the behaviour #71 replaces, so it becomes <see cref="LibraryRegistrationScope.Automatic"/>.
    /// <c>false</c> did ask for the project's table, so it becomes
    /// <see cref="LibraryRegistrationScope.Project"/>. No value at all is the default,
    /// <see cref="LibraryRegistrationScope.Automatic"/>, and so is a name this version does not know.
    /// </remarks>
    private LibraryRegistrationScope ReadRegistrationScope(ConfigData config, out bool migrated)
    {
        migrated = config.AddToGlobalLibrary is not null;

        if (config.RegistrationScope is { } name)
        {
            // By name only: Enum.TryParse would also take "1" or "Project, Global".
            foreach (LibraryRegistrationScope scope in Enum.GetValues<LibraryRegistrationScope>())
            {
                if (string.Equals(scope.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return scope;
                }
            }

            _logger.LogWarning("Unknown library registration scope '{Scope}' in the configuration file; using Automatic", name);
            return LibraryRegistrationScope.Automatic;
        }

        LibraryRegistrationScope migratedScope = config.AddToGlobalLibrary == false ? LibraryRegistrationScope.Project : LibraryRegistrationScope.Automatic;
        if (migrated)
        {
            _logger.LogInformation("Replacing AddToGlobalLibrary = {Old} in the configuration file with RegistrationScope = {New}", config.AddToGlobalLibrary, migratedScope);
        }

        return migratedScope;
    }

    /// <summary>
    /// Reads the API keys from the credential store, first moving any that
    /// <paramref name="config"/> still carries in cleartext into it.
    /// </summary>
    /// <returns>True when at least one key was moved, so the file needs rewriting without it.</returns>
    private bool LoadSecrets(ConfigData? config)
    {
        _storedSecrets.Clear();
        _unmigratedSecrets.Clear();

        var migrated = false;
        string? failure = null;
        foreach (var key in SecretKeys)
        {
            var legacyValue = config is null ? null : GetLegacySecret(config, key);
            if (string.IsNullOrEmpty(legacyValue))
            {
                // Builds that kept keys in the file wrote "" for an unset one; that is not a key.
                legacyValue = null;
            }

            // After the first failure the store is not asked again during this load: a keyring
            // whose unlock prompt was just dismissed would otherwise prompt once per key.
            if (failure is null)
            {
                try
                {
                    if (legacyValue is null)
                    {
                        var stored = _secretStore.Get(key) ?? string.Empty;
                        SetSecret(key, stored);
                        _storedSecrets[key] = stored;
                    }
                    else
                    {
                        // The file's value wins over anything already in the store: only a build
                        // that predates the store writes a key to the file, so it is the newer one.
                        _secretStore.Set(key, legacyValue);
                        SetSecret(key, legacyValue);
                        _storedSecrets[key] = legacyValue;
                        migrated = true;
                        _logger.LogInformation("Moved {SecretKey} from config.json into {SecretStore}", key, _secretStore.DisplayName);
                    }

                    continue;
                }
                catch (SecretStoreException ex)
                {
                    failure = ex.Message;
                    _logger.LogWarning("The credential store cannot be used ({Reason}); API keys are kept for this session only", ex.Message);
                }
            }

            // No store: keep the file's key in memory, and in the file until it can be moved. A
            // key with nothing in the file keeps whatever value this instance already had.
            if (legacyValue is not null)
            {
                SetSecret(key, legacyValue);
                _unmigratedSecrets[key] = legacyValue;
            }
        }

        SecretStorage = CreateStatus(failure);
        return migrated;
    }

    /// <summary>
    /// Saves the API keys to the credential store and everything else to the config file
    /// </summary>
    public void Save()
    {
        SaveSecrets();
        WriteFile();
    }

    /// <summary>
    /// Writes everything but the API keys to the config file.
    /// </summary>
    private void WriteFile()
    {
        try
        {
            var data = new ConfigData
            {
                DownloadDirectory = DownloadDirectory,
                RegistrationScope = RegistrationScope.ToString(),
                CleanupAfterImport = CleanupAfterImport,
                TargetPath = TargetPath,
                UseProjectPath = UseProjectPath,
                AutoImportWhenDownloaded = AutoImportWhenDownloaded,
                LibraryName = LibraryName,
                EasyEda2KiCadPath = EasyEda2KiCadPath,
                DefaultProviderId = DefaultProviderId,
                EnabledProviders = EnabledProviders
            };

            // Never a key typed into Settings: only ones an earlier version already wrote here and
            // that could not be moved into the credential store yet.
            foreach (KeyValuePair<string, string> unmigrated in _unmigratedSecrets)
            {
                SetLegacySecret(data, unmigrated.Key, unmigrated.Value);
            }

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
            _logger.LogInformation("Configuration saved to file");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving configuration");
        }
    }

    /// <summary>
    /// Writes every API key that changed since it was last loaded or saved to the credential store;
    /// a cleared key is deleted from it.
    /// </summary>
    /// <remarks>
    /// When the store cannot be used, a key is <b>not</b> written to <c>config.json</c> instead:
    /// it stays in memory, so it keeps working until the app closes, and
    /// <see cref="SecretStorage"/> says so for the Settings dialog to show. The one exception is a
    /// key an earlier version already left in the file, which stays there - unchanged - until it
    /// can be moved (see <c>_unmigratedSecrets</c>).
    /// </remarks>
    private void SaveSecrets()
    {
        var attempted = false;
        string? failure = null;
        foreach (var key in SecretKeys)
        {
            var value = GetSecret(key);
            var hasLegacy = _unmigratedSecrets.TryGetValue(key, out var legacyValue);
            var known = _storedSecrets.TryGetValue(key, out var storedValue);

            // Nothing to do when the store already holds this value. An empty key whose stored
            // value is unknown is also left alone: the store could not be read, so the user never
            // saw what it holds, and deleting it would destroy a key this instance never loaded.
            if (!hasLegacy && (known ? storedValue == value : value.Length == 0))
            {
                continue;
            }

            if (failure is null)
            {
                attempted = true;
                try
                {
                    if (value.Length == 0)
                    {
                        _secretStore.Delete(key);
                    }
                    else
                    {
                        _secretStore.Set(key, value);
                    }

                    _storedSecrets[key] = value;
                    _ = _unmigratedSecrets.Remove(key);
                    continue;
                }
                catch (SecretStoreException ex)
                {
                    failure = ex.Message;
                    _logger.LogWarning("The credential store cannot be used ({Reason}); API keys are kept for this session only", ex.Message);
                }
            }

            // Not persisted. A key from the file that the user has since changed or cleared is
            // stale, so stop carrying it; the new value lives in memory for this session.
            if (hasLegacy && legacyValue != value)
            {
                _ = _unmigratedSecrets.Remove(key);
            }
        }

        if (attempted)
        {
            SecretStorage = CreateStatus(failure);
        }
    }

    private SecretStorageStatus CreateStatus(string? failure)
    {
        if (failure is null)
        {
            return new SecretStorageStatus(true, $"API keys are saved in {_secretStore.DisplayName}, not in config.json.");
        }

        var message = $"API keys cannot be saved securely ({failure}), so keys entered here are kept only until the app closes.";
        if (_unmigratedSecrets.Count > 0)
        {
            message += " Keys saved by an earlier version are still in config.json in cleartext; they will be moved to the credential store as soon as it works, and clearing a key here removes it from the file.";
        }

        return new SecretStorageStatus(false, message);
    }

    private string GetSecret(string key) => key switch
    {
        OctopartApiTokenKey => OctopartApiToken,
        SnapEdaApiKeyKey => SnapEdaApiKey,
        SamacSysApiKeyKey => SamacSysApiKey,
        JlcpcbAppIdKey => JlcpcbAppId,
        JlcpcbAccessKeyKey => JlcpcbAccessKey,
        JlcpcbSecretKeyKey => JlcpcbSecretKey,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown secret key"),
    };

    private void SetSecret(string key, string value)
    {
        switch (key)
        {
            case OctopartApiTokenKey:
                OctopartApiToken = value;
                break;
            case SnapEdaApiKeyKey:
                SnapEdaApiKey = value;
                break;
            case SamacSysApiKeyKey:
                SamacSysApiKey = value;
                break;
            case JlcpcbAppIdKey:
                JlcpcbAppId = value;
                break;
            case JlcpcbAccessKeyKey:
                JlcpcbAccessKey = value;
                break;
            case JlcpcbSecretKeyKey:
                JlcpcbSecretKey = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown secret key");
        }
    }

    private static string? GetLegacySecret(ConfigData data, string key) => key switch
    {
        OctopartApiTokenKey => data.OctopartApiToken,
        SnapEdaApiKeyKey => data.SnapEdaApiKey,
        SamacSysApiKeyKey => data.SamacSysApiKey,
        _ => null,
    };

    private static void SetLegacySecret(ConfigData data, string key, string value)
    {
        switch (key)
        {
            case OctopartApiTokenKey:
                data.OctopartApiToken = value;
                break;
            case SnapEdaApiKeyKey:
                data.SnapEdaApiKey = value;
                break;
            case SamacSysApiKeyKey:
                data.SamacSysApiKey = value;
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Ensures the download directory exists
    /// </summary>
    public void EnsureDownloadDirectoryExists()
    {
        try
        {
            if (!string.IsNullOrEmpty(DownloadDirectory) && !Directory.Exists(DownloadDirectory))
            {
                _ = Directory.CreateDirectory(DownloadDirectory);
                _logger.LogInformation($"Created download directory: {DownloadDirectory}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to create download directory: {DownloadDirectory}");
        }
    }

    /// <summary>
    /// Gets the import options for the KiCad importer
    /// </summary>
    /// <returns>Import options for the KiCad importer</returns>
    public ImportOptions GetImportOptions()
    {
        return new ImportOptions
        {
            RegistrationScope = RegistrationScope,
            CleanupAfterImport = CleanupAfterImport,
            TargetPath = TargetPath,
            UseProjectPath = UseProjectPath,
            AutoImportWhenDownloaded = AutoImportWhenDownloaded,
            LibraryName = LibraryName,
            EasyEda2KiCadPath = EasyEda2KiCadPath
        };
    }
}
