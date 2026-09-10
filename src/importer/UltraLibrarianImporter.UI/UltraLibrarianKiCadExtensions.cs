using KiCadSharp;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UltraLibrarianImporter.UI.Services;
using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.Services.Mcp;
using UltraLibrarianImporter.UI.Services.Providers;

namespace UltraLibrarianImporter.UI
{
    internal static class UltraLibrarianKiCadExtensions
    {
        public const string UltraLibrarianKiCadClientName = "com.ultralibrarian.kicad.importer";

        public static IServiceCollection AddUltraLibrarianKiCadServices(this IServiceCollection services)
        {
            services.AddKiCad(UltraLibrarianKiCadClientName)
                   .AddSingleton((provider) => provider.GetRequiredKeyedService<KiCad>(UltraLibrarianKiCadClientName));

            // Core import engine & provider registry
            services.AddSingleton<IKiCadImportEngine, KiCadImportEngine>();
            // Component providers
            services.AddSingleton<IComponentProvider, UltraLibrarianProvider>();
            services.AddSingleton<IComponentProvider, SnapEdaProvider>();
            services.AddSingleton<IComponentProvider, ComponentSearchEngineProvider>();
            services.AddSingleton<IComponentProvider, EasyEdaProvider>();
            services.AddSingleton<IComponentProvider, OctopartProvider>();
            services.AddSingleton<IComponentProviderRegistry, ComponentProviderRegistry>();
            services.AddSingleton<IPartAggregatorService, PartAggregatorService>();
            services.AddSingleton<McpServer>();

            // Legacy importer facade for backward compatibility
            services.AddTransient<Services.UltraLibrarianImporter>((provider) =>
            {
                var engine = provider.GetRequiredService<IKiCadImportEngine>();
                var configService = provider.GetRequiredService<IConfigService>();
                var options = configService.GetImportOptions();
                return new Services.UltraLibrarianImporter(engine, options);
            });

            return services;
        }

        public static KiCad GetUltraLibrarianKiCad(this IKiCadFactory factory) => factory.Create(UltraLibrarianKiCadClientName);
    }
}
