using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services
{
    public class PartAggregatorService : IPartAggregatorService
    {
        private readonly IComponentProviderRegistry _registry;
        private readonly ILogger<PartAggregatorService> _logger;

        public PartAggregatorService(IComponentProviderRegistry registry, ILogger<PartAggregatorService> logger)
        {
            _registry = registry;
            _logger = logger;
        }

        public async Task<IReadOnlyList<PartSearchResult>> SearchAllProvidersAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<PartSearchResult>();
            }

            _logger.LogInformation("Querying component providers for query: '{Query}'", query);

            var apiProviders = _registry.Providers.Where(p => p.SupportsDirectApi).ToList();
            if (apiProviders.Count == 0)
            {
                _logger.LogWarning("No providers support direct API search.");
                return Array.Empty<PartSearchResult>();
            }

            var tasks = apiProviders.Select(provider => Task.Run(async () =>
            {
                try
                {
                    return await provider.SearchPartsAsync(query, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Provider {Provider} search failed for query '{Query}': {Message}",
                        provider.DisplayName, query, ex.Message);
                    return (IReadOnlyList<PartSearchResult>)Array.Empty<PartSearchResult>();
                }
            }, cancellationToken));

            var providerResults = await Task.WhenAll(tasks);

            var consolidated = providerResults
                .SelectMany(r => r)
                .OrderByDescending(r => r.Stock ?? 0)
                .ThenBy(r => r.BestPrice ?? decimal.MaxValue)
                .ToList();

            _logger.LogInformation("Consolidated {Count} parts across providers for: '{Query}'", consolidated.Count, query);
            return consolidated;
        }
    }
}
