using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UltraLibrarianImporter.UI.Services.Interfaces;

/// <summary>
/// Aggregates search results across multiple component providers into a consolidated view.
/// </summary>
public interface IPartAggregatorService
{
    /// <summary>
    /// Queries every enabled direct-API provider at once and yields each provider's results as soon
    /// as that provider has answered, so the order is provider-completion order (#49). A provider
    /// that fails is logged and left out; the others are unaffected. Cancelling
    /// <paramref name="cancellationToken"/> stops the providers still running and ends the
    /// enumeration with an <see cref="System.OperationCanceledException"/>.
    /// </summary>
    IAsyncEnumerable<PartSearchResult> StreamAllProvidersAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Materialises <see cref="StreamAllProvidersAsync"/> for callers that need the whole set at
    /// once, such as the MCP server, ordered by stock (highest first) and then price (lowest first).
    /// </summary>
    Task<IReadOnlyList<PartSearchResult>> SearchAllProvidersAsync(string query, CancellationToken cancellationToken = default);
}
