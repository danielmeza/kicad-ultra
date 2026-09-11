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
        public override string ProviderColor => "#C2185B";

        public override bool SupportsDirectApi => false;

        public override Task<IReadOnlyList<PartSearchResult>> SearchPartsAsync(string query, CancellationToken cancellationToken = default)
        {
            // Direct API search is not yet implemented; user-driven embedded browser workflow is used instead.
            return Task.FromResult<IReadOnlyList<PartSearchResult>>(Array.Empty<PartSearchResult>());
        }
    }
}
