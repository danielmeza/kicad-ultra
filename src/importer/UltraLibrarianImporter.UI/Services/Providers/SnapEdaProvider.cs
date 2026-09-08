using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services.Providers
{
    public sealed class SnapEdaProvider : BaseArchiveComponentProvider
    {
        private readonly IConfigService _configService;

        public SnapEdaProvider(IConfigService configService)
        {
            _configService = configService;
        }

        public override string Id => "snapeda";
        public override string DisplayName => "SnapEDA (SnapMagic)";
        public override string SearchUrl => "https://www.snapmagic.com/search/";
        public override string DefaultPrefix => "SE_";
        public override string DefaultLibraryName => "SnapEDA";

        public override bool SupportsDirectApi => true;

        public override Task<IReadOnlyList<PartSearchResult>> SearchPartsAsync(string query, CancellationToken cancellationToken = default)
        {
            var results = new List<PartSearchResult>();

            string apiKey = _configService.SnapEdaApiKey?.Trim() ?? string.Empty;
            string description = string.IsNullOrEmpty(apiKey)
                ? "API key not configured in Settings. Click to search directly on SnapMagic for verified KiCad CAD models."
                : "SnapMagic CAD provider (API key configured).";

            results.Add(new PartSearchResult(
                ProviderId: Id,
                ProviderName: DisplayName,
                PartNumber: query.ToUpperInvariant(),
                Manufacturer: "SnapMagic / SnapEDA",
                Description: description,
                BestPrice: null,
                Currency: "USD",
                Stock: null,
                HasSymbol: true,
                HasFootprint: true,
                Has3DModel: true,
                DatasheetUrl: $"https://www.snapmagic.com/search/?q={Uri.EscapeDataString(query)}",
                PackageDownloadUrl: null
            ));

            return Task.FromResult<IReadOnlyList<PartSearchResult>>(results);
        }
    }
}
