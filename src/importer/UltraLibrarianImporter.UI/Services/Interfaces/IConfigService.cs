namespace UltraLibrarianImporter.UI.Services.Interfaces;

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
    /// Which of KiCad's library tables imported libraries are registered in: the project's, KiCad's
    /// global ones, or the project's when there is a project and the global ones when there is not (#71)
    /// </summary>
    LibraryRegistrationScope RegistrationScope { get; set; }

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
    /// Path to the user-installed easyeda2kicad executable, or to a Python interpreter that has it
    /// installed. Empty to look for it on PATH and then in KiCad's Python interpreter (#76).
    /// </summary>
    string EasyEda2KiCadPath { get; set; }

    /// <summary>
    /// API token for Octopart / Nexar part search. Persisted in the OS credential store, never in
    /// config.json; see <see cref="SecretStorage"/>.
    /// </summary>
    string OctopartApiToken { get; set; }

    /// <summary>
    /// API key for SnapEDA / SnapMagic CAD search. Persisted like <see cref="OctopartApiToken"/>.
    /// </summary>
    string SnapEdaApiKey { get; set; }

    /// <summary>
    /// API key for Component Search Engine (SamacSys). Persisted like <see cref="OctopartApiToken"/>.
    /// </summary>
    string SamacSysApiKey { get; set; }

    /// <summary>
    /// App ID of the user's own application on JLCPCB's API platform, for the official Components
    /// API (#51). Persisted like <see cref="OctopartApiToken"/>. With <see cref="JlcpcbAccessKey"/>
    /// and <see cref="JlcpcbSecretKey"/>; empty, all three, to search the unofficial endpoint.
    /// </summary>
    string JlcpcbAppId { get; set; }

    /// <summary>
    /// Access key of the user's JLCPCB API key, sent with every request. Persisted like
    /// <see cref="OctopartApiToken"/>.
    /// </summary>
    string JlcpcbAccessKey { get; set; }

    /// <summary>
    /// Secret key of the user's JLCPCB API key, which signs requests and is never sent. Persisted
    /// like <see cref="OctopartApiToken"/>.
    /// </summary>
    string JlcpcbSecretKey { get; set; }

    /// <summary>
    /// Whether the API keys and credentials above are being persisted in the OS credential store
    /// or, because that store could not be used, held for this session only. Updated by
    /// <see cref="Load"/> and <see cref="Save"/>.
    /// </summary>
    SecretStorageStatus SecretStorage { get; }

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
