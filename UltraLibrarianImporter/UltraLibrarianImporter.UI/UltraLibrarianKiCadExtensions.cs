using Microsoft.Extensions.DependencyInjection;

using UltraLibrarianImporter.KiCadBindings;

namespace UltraLibrarianImporter.UI
{
    internal static class UltraLibrarianKiCadExtensions
    {
        public const string UltraLibrarianKiCadClientName = "com.ultralibrarian.kicad.importer";
        public static IServiceCollection AddUltraLibrarianKiCadServices(this IServiceCollection services)
        {
            return services.AddKiCad(UltraLibrarianKiCadClientName).AddSingleton((provider) => provider.GetRequiredKeyedService<KiCad>(UltraLibrarianKiCadClientName));
        }

        public static KiCad GetUltraLibrarianKiCad(this IKiCadFactory factory) => factory.Create(UltraLibrarianKiCadClientName);
    }
}
