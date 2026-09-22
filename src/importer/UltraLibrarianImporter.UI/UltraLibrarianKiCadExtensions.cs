using System;

using KiCadSharp;

using Microsoft.Extensions.DependencyInjection;

using UltraLibrarianImporter.UI.Services;
using UltraLibrarianImporter.UI.Services.EasyEda2KiCad;
using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.Services.Mcp;
using UltraLibrarianImporter.UI.Services.Providers;
using UltraLibrarianImporter.UI.Services.Providers.Jlcpcb;

namespace UltraLibrarianImporter.UI;

internal static class UltraLibrarianKiCadExtensions
{
    public const string UltraLibrarianKiCadClientName = "com.ultralibrarian.kicad.importer";

    /// <param name="services">The container to add to.</param>
    /// <param name="jlcpcbSources">Whether EasyEDA / LCSC search may use JLCPCB's official API in this
    /// container (#51). Required, so that neither the GUI nor the <c>--mcp</c> container can leave it
    /// out: the GUI passes <see cref="JlcpcbSourcePolicy.OfficialApiWhenConfigured"/>, and the MCP
    /// server <see cref="JlcpcbSourcePolicy.WebsiteEndpointOnly"/>, which <see cref="McpServer"/>
    /// insists on.</param>
    public static IServiceCollection AddUltraLibrarianKiCadServices(this IServiceCollection services, JlcpcbSourcePolicy jlcpcbSources) =>
        services
            .AddSingleton(jlcpcbSources)
            .AddKiCad(UltraLibrarianKiCadClientName)
            .AddSingleton(provider => provider.GetRequiredKeyedService<KiCad>(UltraLibrarianKiCadClientName))
            // Core import engine & provider registry
            .AddSingleton<IKiCadImportEngine, KiCadImportEngine>()
            // The user-installed easyeda2kicad, run only as a separate process (#76)
            .AddSingleton<EasyEda2KiCadLocator>()
            .AddSingleton<EasyEda2KiCadConverter>()
            // Component providers
            .AddSingleton<IComponentProvider, UltraLibrarianProvider>()
            .AddSingleton<IComponentProvider, SnapEdaProvider>()
            .AddSingleton<IComponentProvider, ComponentSearchEngineProvider>()
            .AddSingleton<IComponentProvider, EasyEdaProvider>()
            .AddSingleton<IComponentProvider, OctopartProvider>()
            .AddSingleton<IComponentProviderRegistry, ComponentProviderRegistry>()
            // Search pipeline (#49, #55): one response cache and one set of per-provider rate limits per
            // process. `--mcp` runs as its own process, so it has its own.
            .AddSingleton(TimeProvider.System)
            .AddSingleton(new ProviderSearchOptions())
            .AddSingleton<ProviderResponseCache>()
            .AddSingleton<ProviderRateLimiter>()
            .AddSingleton<IPartAggregatorService, PartAggregatorService>()
            // Resolved only in `--mcp` mode (Program.RunMcpHostAsync), which builds its own container
            .AddSingleton<McpServer>()
            // Legacy importer facade for backward compatibility
            .AddTransient(provider => new Services.UltraLibrarianImporter(
                provider.GetRequiredService<IKiCadImportEngine>(),
                provider.GetRequiredService<IConfigService>().GetImportOptions()));

    public static KiCad GetUltraLibrarianKiCad(this IKiCadFactory factory) => factory.Create(UltraLibrarianKiCadClientName);
}
