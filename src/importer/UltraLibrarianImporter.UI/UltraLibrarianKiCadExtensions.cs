using KiCadSharp;

using Microsoft.Extensions.DependencyInjection;

using UltraLibrarianImporter.UI.Services;
using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.Services.Mcp;
using UltraLibrarianImporter.UI.Services.Providers;

namespace UltraLibrarianImporter.UI;

internal static class UltraLibrarianKiCadExtensions
{
    public const string UltraLibrarianKiCadClientName = "com.ultralibrarian.kicad.importer";

    public static IServiceCollection AddUltraLibrarianKiCadServices(this IServiceCollection services) =>
        services
            .AddKiCad(UltraLibrarianKiCadClientName)
            .AddSingleton(provider => provider.GetRequiredKeyedService<KiCad>(UltraLibrarianKiCadClientName))
            // Core import engine & provider registry
            .AddSingleton<IKiCadImportEngine, KiCadImportEngine>()
            // Component providers
            .AddSingleton<IComponentProvider, UltraLibrarianProvider>()
            .AddSingleton<IComponentProvider, SnapEdaProvider>()
            .AddSingleton<IComponentProvider, ComponentSearchEngineProvider>()
            .AddSingleton<IComponentProvider, EasyEdaProvider>()
            .AddSingleton<IComponentProvider, OctopartProvider>()
            .AddSingleton<IComponentProviderRegistry, ComponentProviderRegistry>()
            .AddSingleton<IPartAggregatorService, PartAggregatorService>()
            // Resolved only in `--mcp` mode (Program.RunMcpHostAsync), which builds its own container
            .AddSingleton<McpServer>()
            // Legacy importer facade for backward compatibility
            .AddTransient(provider => new Services.UltraLibrarianImporter(
                provider.GetRequiredService<IKiCadImportEngine>(),
                provider.GetRequiredService<IConfigService>().GetImportOptions()));

    public static KiCad GetUltraLibrarianKiCad(this IKiCadFactory factory) => factory.Create(UltraLibrarianKiCadClientName);
}
