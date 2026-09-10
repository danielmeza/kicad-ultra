using Microsoft.Extensions.Logging;
using KiCadSharp;

namespace UltraLibrarianImporter.UI.Services.Interfaces
{
    /// <summary>
    /// Interface for application configuration service
    /// </summary>
    public interface IConfigService
    {
        /// <summary>
        /// Gets the configured download directory
        /// </summary>
        string DownloadDirectory { get; set; }
        
        /// <summary>
        /// Whether to add components to the global library
        /// </summary>
        bool AddToGlobalLibrary { get; set; }
        
        /// <summary>
        /// Whether to clean up temporary files after import
        /// </summary>
        bool CleanupAfterImport { get; set; }
        
        /// <summary>
        /// Target path for importing libraries (if empty, uses project path)
        /// </summary>
        string TargetPath { get; set; }
        
        /// <summary>
        /// Whether to use the project path as the target path
        /// </summary>
        bool UseProjectPath { get; set; }

        /// <summary>
        /// Whether to automatically import libraries when downloaded
        /// </summary>
        bool AutoImportWhenDownloaded { get; set; }
        
        /// <summary>
        /// Custom name to use for the library (if empty, default naming will be used)
        /// </summary>
        string LibraryName { get; set; }

        /// <summary>
        /// API token for Octopart / Nexar part search
        /// </summary>
        string OctopartApiToken { get; set; }

        /// <summary>
        /// API key for SnapEDA / SnapMagic CAD search
        /// </summary>
        string SnapEdaApiKey { get; set; }

        /// <summary>
        /// API key for Component Search Engine (SamacSys)
        /// </summary>
        string SamacSysApiKey { get; set; }

        /// <summary>
        /// Default component provider ID to select on startup (e.g. "ultralibrarian", "easyeda")
        /// </summary>
        string DefaultProviderId { get; set; }

        /// <summary>
        /// Enabled state for each provider ID. If not present in map, defaults to true.
        /// </summary>
        System.Collections.Generic.Dictionary<string, bool> EnabledProviders { get; set; }

        /// <summary>
        /// Checks whether a specific provider is enabled by user configuration
        /// </summary>
        bool IsProviderEnabled(string providerId);

        /// <summary>
        /// Sets enabled state for a specific provider
        /// </summary>
        void SetProviderEnabled(string providerId, bool isEnabled);

        /// <summary>
        /// Ensures the download directory exists
        /// </summary>
        void EnsureDownloadDirectoryExists();
        
        /// <summary>
        /// Loads configuration from storage
        /// </summary>
        void Load();
        
        /// <summary>
        /// Saves configuration to storage
        /// </summary>
        void Save();
        
        /// <summary>
        /// Gets the import options for the KiCad importer
        /// </summary>
        /// <returns>Import options for the KiCad importer</returns>
        ImportOptions GetImportOptions();
    }
}