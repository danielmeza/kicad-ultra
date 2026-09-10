using System;
using System.IO;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using KiCadSharp;
using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services
{
    /// <summary>
    /// Service for managing application configuration
    /// </summary>
    public class ConfigService : IConfigService
    {
        private readonly ILogger<ConfigService> _logger;
        private readonly string _configFilePath;

        // Default values
        private const string DEFAULT_DOWNLOAD_DIR = "";

        // Configuration properties
        public string DownloadDirectory { get; set; } = DEFAULT_DOWNLOAD_DIR;
        public bool AddToGlobalLibrary { get; set; } = true;
        public bool CleanupAfterImport { get; set; } = true;
        public string TargetPath { get; set; } = string.Empty;
        public bool UseProjectPath { get; set; } = true;
        public bool AutoImportWhenDownloaded { get; set; } = true;
        public string LibraryName { get; set; } = string.Empty;
        public string OctopartApiToken { get; set; } = string.Empty;
        public string SnapEdaApiKey { get; set; } = string.Empty;
        public string SamacSysApiKey { get; set; } = string.Empty;
        public string DefaultProviderId { get; set; } = "ultralibrarian";
        public System.Collections.Generic.Dictionary<string, bool> EnabledProviders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsProviderEnabled(string providerId)
        {
            if (string.IsNullOrEmpty(providerId)) return false;
            if (EnabledProviders.TryGetValue(providerId, out bool enabled))
            {
                return enabled;
            }
            return true;
        }

        public void SetProviderEnabled(string providerId, bool isEnabled)
        {
            if (string.IsNullOrEmpty(providerId)) return;
            EnabledProviders[providerId] = isEnabled;
        }

        /// <summary>
        /// Creates a new instance of the configuration service
        /// </summary>
        /// <param name="logger">Logger for recording operations</param>
        /// <param name="configFilePath">Path to the configuration file (optional)</param>
        public ConfigService(ILogger<ConfigService> logger)
        {
            _logger = logger;


            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "UltraLibrarianImporter");

            Directory.CreateDirectory(appDataDir);
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

        private class ConfigData
        {
            public string? DownloadDirectory { get; set; }
            public bool AddToGlobalLibrary { get; set; } = true;
            public bool CleanupAfterImport { get; set; } = true;
            public string? TargetPath { get; set; }
            public bool UseProjectPath { get; set; } = true;
            public bool AutoImportWhenDownloaded { get; set; } = true;
            public string? LibraryName { get; set; }
            public string? OctopartApiToken { get; set; }
            public string? SnapEdaApiKey { get; set; }
            public string? SamacSysApiKey { get; set; }
            public string? DefaultProviderId { get; set; }
            public System.Collections.Generic.Dictionary<string, bool>? EnabledProviders { get; set; }
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
                    string json = File.ReadAllText(_configFilePath);
                    var config = JsonSerializer.Deserialize<ConfigData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (config != null)
                    {
                        DownloadDirectory = config.DownloadDirectory ?? DownloadDirectory;
                        AddToGlobalLibrary = config.AddToGlobalLibrary;
                        CleanupAfterImport = config.CleanupAfterImport;
                        TargetPath = config.TargetPath ?? string.Empty;
                        UseProjectPath = config.UseProjectPath;
                        AutoImportWhenDownloaded = config.AutoImportWhenDownloaded;
                        LibraryName = config.LibraryName ?? string.Empty;
                        OctopartApiToken = config.OctopartApiToken ?? string.Empty;
                        SnapEdaApiKey = config.SnapEdaApiKey ?? string.Empty;
                        SamacSysApiKey = config.SamacSysApiKey ?? string.Empty;
                        DefaultProviderId = string.IsNullOrEmpty(config.DefaultProviderId) ? "ultralibrarian" : config.DefaultProviderId;
                        EnabledProviders = config.EnabledProviders != null
                            ? new System.Collections.Generic.Dictionary<string, bool>(config.EnabledProviders, StringComparer.OrdinalIgnoreCase)
                            : new System.Collections.Generic.Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
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
                    OctopartApiToken = OctopartApiToken,
                    SnapEdaApiKey = SnapEdaApiKey,
                    SamacSysApiKey = SamacSysApiKey,
                    DefaultProviderId = DefaultProviderId,
                    EnabledProviders = EnabledProviders
                };

                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
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
                    Directory.CreateDirectory(DownloadDirectory);
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
}