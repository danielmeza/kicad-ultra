using Microsoft.Extensions.Logging;
using UltraLibrarianImporter.KiCadBindings;

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