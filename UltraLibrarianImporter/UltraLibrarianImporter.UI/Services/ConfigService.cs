using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using UltraLibrarianImporter.KiCadBindings;
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
        private const string DEFAULT_IPC_ENDPOINT = "http://localhost:22000";
        private const string DEFAULT_DOWNLOAD_DIR = "";
        private const string DEFAULT_IPC_TOKEN = "";

        // Configuration properties
        public string IpcEndpoint { get; set; } = DEFAULT_IPC_ENDPOINT;
        public string IpcToken { get; set; } = DEFAULT_IPC_TOKEN;
        public string DownloadDirectory { get; set; } = DEFAULT_DOWNLOAD_DIR;
        public bool AddToGlobalLibrary { get; set; } = true;
        public bool CleanupAfterImport { get; set; } = true;

        /// <summary>
        /// Creates a new instance of the configuration service
        /// </summary>
        /// <param name="logger">Logger for recording operations</param>
        /// <param name="configFilePath">Path to the configuration file (optional)</param>
        public ConfigService(ILogger<ConfigService> logger, string? configFilePath = null)
        {
            _logger = logger;

            // If config file path is not specified, create one in the app data directory
            if (string.IsNullOrEmpty(configFilePath))
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "UltraLibrarianImporter");
                
                Directory.CreateDirectory(appDataDir);
                _configFilePath = Path.Combine(appDataDir, "config.json");
            }
            else
            {
                _configFilePath = configFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(_configFilePath) ?? ".");
            }

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
                    string json = File.ReadAllText(_configFilePath);
                    var config = JsonSerializer.Deserialize<ConfigService>(json);

                    if (config != null)
                    {
                        IpcEndpoint = config.IpcEndpoint;
                        IpcToken = config.IpcToken;
                        DownloadDirectory = config.DownloadDirectory;
                        AddToGlobalLibrary = config.AddToGlobalLibrary;
                        CleanupAfterImport = config.CleanupAfterImport;
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
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
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
                CleanupAfterImport = CleanupAfterImport
            };
        }
    }
}