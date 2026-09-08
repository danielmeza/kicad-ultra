using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UltraLibrarianImporter.UI.Services.Interfaces
{
    /// <summary>
    /// Aggregates search results across multiple component providers into a consolidated view.
    /// </summary>
    public interface IPartAggregatorService
    {
        Task<IReadOnlyList<PartSearchResult>> SearchAllProvidersAsync(string query, CancellationToken cancellationToken = default);
    }
}
