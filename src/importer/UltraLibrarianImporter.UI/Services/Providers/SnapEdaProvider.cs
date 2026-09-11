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
        public override string ProviderColor => "#0288D1";

        public override bool SupportsDirectApi => false;

        public override Task<IReadOnlyList<PartSearchResult>> SearchPartsAsync(string query, CancellationToken cancellationToken = default)
        {
            // Direct API search is not yet implemented; user-driven embedded browser workflow is used instead.
            return Task.FromResult<IReadOnlyList<PartSearchResult>>(Array.Empty<PartSearchResult>());
        }
    }
}
