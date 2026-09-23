using System;
using KiCadSharp;
using KiCadUltra.Services;
using KiCadUltra.Services.EasyEda2KiCad;
using KiCadUltra.Services.Interfaces;
using KiCadUltra.Services.Mcp;
using KiCadUltra.Services.Providers;
using KiCadUltra.Services.Providers.Jlcpcb;
using Microsoft.Extensions.DependencyInjection;

namespace KiCadUltra;

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
            // What JLCPCB has refused this application's credentials, for as long as the process runs
            // (#126): the provider records it, the Part Explorer's notice reads it.
            .AddSingleton<JlcpcbOfficialApiAccess>()
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
            .AddTransient(provider => new ComponentImporter(
                provider.GetRequiredService<IKiCadImportEngine>(),
                provider.GetRequiredService<IConfigService>().GetImportOptions()));

    public static KiCad GetUltraLibrarianKiCad(this IKiCadFactory factory) => factory.Create(UltraLibrarianKiCadClientName);
}
