using KiCadSharp;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UltraLibrarianImporter.UI.Services;
using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI
{
    internal static class UltraLibrarianKiCadExtensions
    {
        public const string UltraLibrarianKiCadClientName = "com.ultralibrarian.kicad.importer";
        
        public static IServiceCollection AddUltraLibrarianKiCadServices(this IServiceCollection services)
        {
            services.AddKiCad(UltraLibrarianKiCadClientName)
                   .AddSingleton((provider) => provider.GetRequiredKeyedService<KiCad>(UltraLibrarianKiCadClientName));
            
            // Register the UltraLibrarian importer
            services.AddTransient<Services.UltraLibrarianImporter>((provider) => 
            {
                var kicad = provider.GetRequiredService<KiCad>();
                var logger = provider.GetRequiredService<ILogger<Services.UltraLibrarianImporter>>();
                var configService = provider.GetRequiredService<IConfigService>();
                
                var options = configService.GetImportOptions();
                
                return new Services.UltraLibrarianImporter(kicad, logger, options);
            });
            
            return services;
        }

        public static KiCad GetUltraLibrarianKiCad(this IKiCadFactory factory) => factory.Create(UltraLibrarianKiCadClientName);
    }
}
