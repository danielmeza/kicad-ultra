using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services.Providers
{
    public sealed class ComponentSearchEngineProvider : BaseArchiveComponentProvider
    {
        private readonly IConfigService _configService;

        public ComponentSearchEngineProvider(IConfigService configService)
        {
            _configService = configService;
        }

        public override string Id => "componentsearchengine";
        public override string DisplayName => "Component Search Engine (SamacSys)";
        public override string SearchUrl => "https://componentsearchengine.com/";
        public override string DefaultPrefix => "CSE_";
        public override string DefaultLibraryName => "ComponentSearchEngine";

        public override bool SupportsDirectApi => true;

        public override Task<IReadOnlyList<PartSearchResult>> SearchPartsAsync(string query, CancellationToken cancellationToken = default)
        {
            var results = new List<PartSearchResult>();

            string apiKey = _configService.SamacSysApiKey?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(apiKey))
            {
                return Task.FromResult<IReadOnlyList<PartSearchResult>>(Array.Empty<PartSearchResult>());
            }

            results.Add(new PartSearchResult(
                ProviderId: Id,
                ProviderName: DisplayName,
                PartNumber: query.ToUpperInvariant(),
                Manufacturer: "SamacSys",
                Description: "SamacSys Component Search Engine provider (credentials configured).",
                BestPrice: null,
                Currency: "USD",
                Stock: null,
                HasSymbol: true,
                HasFootprint: true,
                Has3DModel: true,
                DatasheetUrl: $"https://componentsearchengine.com/search?term={Uri.EscapeDataString(query)}",
                PackageDownloadUrl: null
            ));

            return Task.FromResult<IReadOnlyList<PartSearchResult>>(results);
        }
    }
}
