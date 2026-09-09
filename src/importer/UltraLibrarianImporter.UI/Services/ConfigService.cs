using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services;

/// <summary>
/// Service for managing application configuration
/// </summary>
public class ConfigService : IConfigService
{
    private readonly ILogger<ConfigService> _logger;
    private readonly string _configFilePath;

    private static readonly JsonSerializerOptions s_serializerOptions = new() { WriteIndented = true };

    // Default values
    private const string DEFAULT_DOWNLOAD_DIR = "";

    /// <summary>
    /// The on-disk shape of the configuration.
    /// </summary>
    /// <remarks>
    /// <see cref="ConfigService"/> cannot serialize itself. System.Text.Json binds a type's single
    /// public constructor by parameter name, and this one takes an <see cref="ILogger"/> that
    /// matches no property, so <c>Deserialize&lt;ConfigService&gt;</c> threw
    /// <see cref="InvalidOperationException"/> on every launch that found an existing file. The
    /// catch in <see cref="Load"/> swallowed it, which turned a hard failure into settings that
    /// silently reverted to their defaults on restart. The property names here match what the old
    /// code wrote, so existing config.json files still load.
    /// </remarks>
    private sealed class ConfigData
    {
        public string DownloadDirectory { get; set; } = string.Empty;
        public bool AddToGlobalLibrary { get; set; } = true;
        public bool CleanupAfterImport { get; set; } = true;
        public string TargetPath { get; set; } = string.Empty;
        public bool UseProjectPath { get; set; } = true;
        public bool AutoImportWhenDownloaded { get; set; } = true;
        public string LibraryName { get; set; } = string.Empty;
    }

    // Configuration properties
    public string DownloadDirectory { get; set; } = DEFAULT_DOWNLOAD_DIR;
    public bool AddToGlobalLibrary { get; set; } = true;
    public bool CleanupAfterImport { get; set; } = true;
    public string TargetPath { get; set; } = string.Empty;
    public bool UseProjectPath { get; set; } = true;
    public bool AutoImportWhenDownloaded { get; set; } = true;
    public string LibraryName { get; set; } = string.Empty;

    /// <summary>
    /// Creates a new instance of the configuration service
    /// </summary>
    /// <param name="logger">Logger for recording operations</param>
    public ConfigService(ILogger<ConfigService> logger)
    {
        _logger = logger;


        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UltraLibrarianImporter");

        _ = Directory.CreateDirectory(appDataDir);
        _configFilePath = Path.Combine(appDataDir, "config.json");

        // Set default download directory if not specified
        if (string.IsNullOrEmpty(DownloadDirectory))
        {
            DownloadDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "UltraLibrarianDownloads");
        }

        // Load configuration from file
        Load();
    }

    /// <summary>
    /// Loads the configuration from the config file
    /// </summary>
    public void Load()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                ConfigData? config = JsonSerializer.Deserialize<ConfigData>(json);

                if (config != null)
                {
                    DownloadDirectory = config.DownloadDirectory;
                    AddToGlobalLibrary = config.AddToGlobalLibrary;
                    CleanupAfterImport = config.CleanupAfterImport;
                    TargetPath = config.TargetPath;
                    UseProjectPath = config.UseProjectPath;
                    AutoImportWhenDownloaded = config.AutoImportWhenDownloaded;
                    LibraryName = config.LibraryName;
                }

                _logger.LogInformation("Configuration loaded from file");
            }
            else
            {
                _logger.LogInformation("No configuration file found, using defaults");
                Save(); // Create the default config file
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading configuration");
        }
    }

    /// <summary>
    /// Saves the configuration to the config file
    /// </summary>
    public void Save()
    {
        try
        {
            var data = new ConfigData
            {
                DownloadDirectory = DownloadDirectory,
                AddToGlobalLibrary = AddToGlobalLibrary,
                CleanupAfterImport = CleanupAfterImport,
                TargetPath = TargetPath,
                UseProjectPath = UseProjectPath,
                AutoImportWhenDownloaded = AutoImportWhenDownloaded,
                LibraryName = LibraryName,
            };
            var json = JsonSerializer.Serialize(data, s_serializerOptions);
            File.WriteAllText(_configFilePath, json);
            _logger.LogInformation("Configuration saved to file");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving configuration");
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
            AddToGlobalLibrary = AddToGlobalLibrary,
            CleanupAfterImport = CleanupAfterImport,
            TargetPath = TargetPath,
            UseProjectPath = UseProjectPath,
            AutoImportWhenDownloaded = AutoImportWhenDownloaded,
            LibraryName = LibraryName
        };
    }
}
