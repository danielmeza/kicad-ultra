using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UltraLibrarianImporter.UI.Services.Interfaces
{
    public record ProviderExtractionResult(
        IReadOnlyList<string> SymbolFiles,
        IReadOnlyList<string> PrettyDirectories,
        IReadOnlyList<string> FootprintFiles,
        IReadOnlyList<string> Model3DFiles
    );

    /// <summary>
    /// Represents a consolidated part result returned from a component search query.
    /// </summary>
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
        string? PackageDownloadUrl
    );

    /// <summary>
    /// Represents an external component library provider (e.g. UltraLibrarian, SnapEDA, Octopart).
    /// </summary>
    public interface IComponentProvider
    {
        string Id { get; }
        string DisplayName { get; }
        string SearchUrl { get; }
        string DefaultPrefix { get; }
        string DefaultLibraryName { get; }

        bool CanHandleDownload(string filePath);
        Task<ProviderExtractionResult> ExtractPackageAsync(string packageFilePath, string destinationDir);

        bool SupportsDirectApi { get; }
        Task<IReadOnlyList<PartSearchResult>> SearchPartsAsync(string query, CancellationToken cancellationToken = default);
    }
}
