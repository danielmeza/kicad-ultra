using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

namespace UltraLibrarianImporter.KiCadBindings
{
    public static class KiCadServicesExtensions
    {
        public static IServiceCollection AddKiCadServices(this IServiceCollection services, Action<KiCadClientSettings> configure)
        {
            services.AddSingleton<KiCadIPCClient>();
            services.AddSingleton<KiCadClientSettings>()
                .Configure((KiCadClientSettings settings) =>
                {
                    settings.ClientName = "kicad.net";
                    settings.PipeName = KiCadEnvironment.GetApiSocket();
                    settings.Token = KiCadEnvironment.GetApiToken();
                })
                .PostConfigure(configure);

            services.AddSingleton<KiCad>();


            return services;
        }
    }
}
