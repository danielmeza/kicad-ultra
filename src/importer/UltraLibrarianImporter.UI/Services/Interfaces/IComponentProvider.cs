using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UltraLibrarianImporter.UI.Services.Interfaces
{
    /// <summary>
    /// Represents the extracted files produced by unpacking a component package archive.
    /// </summary>
    /// <param name="SymbolFiles">Paths to symbol library files (.kicad_sym, .lib).</param>
    /// <param name="PrettyDirectories">Paths to footprint library directories (.pretty).</param>
    /// <param name="FootprintFiles">Paths to loose footprint module files (.kicad_mod).</param>
    /// <param name="Model3DFiles">Paths to 3D model files (.step, .stp, .wrl, .vrml).</param>
    public record ProviderExtractionResult(
        IReadOnlyList<string> SymbolFiles,
        IReadOnlyList<string> PrettyDirectories,
        IReadOnlyList<string> FootprintFiles,
        IReadOnlyList<string> Model3DFiles
    );

    /// <summary>
    /// Represents a consolidated part result returned from a component search query.
    /// </summary>
    /// <param name="ProviderId">Unique identifier of the provider that returned this result.</param>
    /// <param name="ProviderName">Display name of the component provider.</param>
    /// <param name="PartNumber">Manufacturer part number (MPN).</param>
    /// <param name="Manufacturer">Manufacturer name.</param>
    /// <param name="Description">Short description or package specification.</param>
    /// <param name="BestPrice">Lowest unit price found across available offers.</param>
    /// <param name="Currency">Currency code for the price (e.g. "USD").</param>
    /// <param name="Stock">Available stock count across authorized distributors.</param>
    /// <param name="HasSymbol">Indicates whether a verified KiCad schematic symbol is available.</param>
    /// <param name="HasFootprint">Indicates whether a verified KiCad PCB footprint is available.</param>
    /// <param name="Has3DModel">Indicates whether a verified 3D CAD model is available.</param>
    /// <param name="DatasheetUrl">URL to component datasheet or provider product page.</param>
    /// <param name="PackageDownloadUrl">Direct URL to download CAD model package archive if supported.</param>
    /// <param name="ProviderColor">Hex color code for provider visual badge (e.g. "#E65100").</param>
    public record PartSearchResult(
        string ProviderId,
        string ProviderName,
        string PartNumber,
        string Manufacturer,
        string Description,
        decimal? BestPrice,
        string? Currency,
        int? Stock,
        bool HasSymbol,
        bool HasFootprint,
        bool Has3DModel,
        string? DatasheetUrl,
        string? PackageDownloadUrl,
        string ProviderColor = "#666666"
    );

    /// <summary>
    /// Represents an external component library provider (e.g. UltraLibrarian, SnapEDA, Octopart, EasyEDA).
    /// </summary>
    public interface IComponentProvider
    {
        /// <summary>
        /// Gets the unique string identifier for this provider (e.g. "ultralibrarian", "snapeda").
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Gets the human-readable display name for this provider.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Gets the primary search or home URL for this provider.
        /// </summary>
        string SearchUrl { get; }

        /// <summary>
        /// Gets the default library prefix used to avoid symbol/footprint collisions (e.g. "UL_", "SE_").
        /// </summary>
        string DefaultPrefix { get; }

        /// <summary>
        /// Gets the default base name of the KiCad library generated for this provider.
        /// </summary>
        string DefaultLibraryName { get; }

        /// <summary>
        /// Gets the primary brand accent color (hex format) used to render the provider pill in the UI.
        /// </summary>
        string ProviderColor { get; }

        /// <summary>
        /// Determines whether the downloaded file can be handled by this provider.
        /// </summary>
        /// <param name="filePath">Full file path to the downloaded archive or component.</param>
        /// <returns><c>true</c> if this provider can unpack and process the file; otherwise, <c>false</c>.</returns>
        bool CanHandleDownload(string filePath);

        /// <summary>
        /// Unpacks the downloaded package file and categorizes symbol, footprint, and 3D model assets.
        /// </summary>
        /// <param name="packageFilePath">Path to the downloaded package archive.</param>
        /// <param name="destinationDir">Temporary directory to unpack files into.</param>
        /// <returns>Extracted asset paths grouped by asset type.</returns>
        Task<ProviderExtractionResult> ExtractPackageAsync(string packageFilePath, string destinationDir);

        /// <summary>
        /// Gets whether this provider supports direct REST/GraphQL API part queries without browser automation.
        /// </summary>
        bool SupportsDirectApi { get; }

        /// <summary>
        /// Performs an API search for components matching the given query.
        /// </summary>
        /// <param name="query">Search term, keyword, or manufacturer part number.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A collection of matching part search results.</returns>
        Task<IReadOnlyList<PartSearchResult>> SearchPartsAsync(string query, CancellationToken cancellationToken = default);
    }
}
