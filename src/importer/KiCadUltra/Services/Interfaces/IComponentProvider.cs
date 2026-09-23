using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace KiCadUltra.Services.Interfaces;

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
/// Whether the provider says a part has one CAD asset (#48). Not a <c>bool</c>, because "the provider
/// did not say" is not "no": most providers cannot tell at search time.
/// </summary>
/// <remarks>Serialised by name, so the MCP tools' JSON says <c>"Unknown"</c> rather than a number.</remarks>
[JsonConverter(typeof(JsonStringEnumConverter<CadAvailability>))]
public enum CadAvailability
{
    /// <summary>The provider does not report it, or did not for this part. The default.</summary>
    Unknown = 0,

    /// <summary>The provider reports that the part has it.</summary>
    Available,

    /// <summary>The provider reports that the part does not have it.</summary>
    NotAvailable,
}

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
/// <param name="HasSymbol">Whether the provider reports a KiCad schematic symbol for the part.
/// <see cref="CadAvailability.Unknown"/> unless the provider's response says so, either way.</param>
/// <param name="HasFootprint">Whether the provider reports a KiCad PCB footprint for the part, on the same terms.</param>
/// <param name="Has3DModel">Whether the provider reports a 3D model for the part, on the same terms.</param>
/// <param name="DatasheetUrl">URL to component datasheet or provider product page.</param>
/// <param name="ProviderColor">Hex color code for provider visual badge (e.g. "#E65100").</param>
/// <param name="Attribution">Where this result's data actually comes from, shown beside the result, when that is
/// not the provider itself (e.g. a third-party index). <c>null</c> when the provider is the source.</param>
/// <param name="LcscPartNumber">The part's LCSC code (<c>C</c> followed by digits, e.g. <c>C2040</c>) when the provider
/// reported one, which is what the EasyEDA / LCSC import converts (#76): JLCPCB's own field for EasyEDA / LCSC
/// (#51, #52), LCSC's offer SKU for Octopart (#47). <c>null</c> when the provider did not report one; never derived
/// from the MPN or guessed.</param>
/// <param name="PriceBreaks">The provider's quantity price breaks, lowest quantity first, in <paramref name="Currency"/>.
/// <c>null</c> when the provider does not report them.</param>
public record PartSearchResult(
    string ProviderId,
    string ProviderName,
    string PartNumber,
    string Manufacturer,
    string Description,
    decimal? BestPrice,
    string? Currency,
    int? Stock,
    CadAvailability HasSymbol,
    CadAvailability HasFootprint,
    CadAvailability Has3DModel,
    string? DatasheetUrl,
    string ProviderColor = "#666666",
    string? Attribution = null,
    string? LcscPartNumber = null,
    IReadOnlyList<PriceBreak>? PriceBreaks = null
);

/// <summary>
/// One step of a quantity price ladder: the unit price that applies from <paramref name="Quantity"/>
/// pieces up to the next break.
/// </summary>
/// <param name="Quantity">The smallest order quantity this unit price applies to.</param>
/// <param name="UnitPrice">The price per piece at that quantity.</param>
public record PriceBreak(int Quantity, decimal UnitPrice);

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
    /// <remarks>
    /// Return only what the provider answered, and throw when it did not answer: a non-success HTTP
    /// status, a timeout, a body that is not the expected shape, or
    /// <see cref="ProviderNotConfiguredException"/> when a required credential is missing. A narrow
    /// catch may log the detail, but must rethrow rather than return an empty list:
    /// <c>PartAggregatorService</c> caches every list returned here, so an empty list is remembered
    /// as "no matches", while a failure is left out of the results and not cached.
    /// </remarks>
    /// <param name="query">Search term, keyword, or manufacturer part number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The provider's matches for the query; empty only when it found none.</returns>
    Task<IReadOnlyList<PartSearchResult>> SearchPartsAsync(string query, CancellationToken cancellationToken = default);
}
