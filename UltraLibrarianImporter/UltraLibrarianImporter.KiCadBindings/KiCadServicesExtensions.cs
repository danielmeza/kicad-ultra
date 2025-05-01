using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using nng;
namespace UltraLibrarianImporter.KiCadBindings
{
    public static class KiCadServicesExtensions
    {
        public static IServiceCollection AddKiCad(this IServiceCollection services, string clientName, Action<KiCadClientSettings>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(clientName);

            services.AddKeyedSingleton(clientName, (provider, key) =>
            {
                var settings = provider.GetRequiredService<IOptionsFactory<KiCadClientSettings>>().Create(clientName);
                var factory = provider.GetRequiredService<IAPIFactory<INngMsg>>();
                var logger = provider.GetRequiredService<ILogger<KiCadIPCClient>>();
                return new KiCadIPCClient(factory, settings, logger);
            });

            var optioinsBuilder = services.AddOptions<KiCadClientSettings>(clientName)
                 .Configure((KiCadClientSettings settings) =>
                 {
                     settings.ClientName = clientName;
                     settings.PipeName = KiCadEnvironment.GetApiSocket();
                     settings.Token = KiCadEnvironment.GetApiToken();
                 });

            if (configure != null)
            {
                optioinsBuilder.PostConfigure(configure);
            }

            services.AddKeyedSingleton(clientName, (provider, key) => new KiCad(provider.GetRequiredKeyedService<KiCadIPCClient>(key)));

            services.AddSingleton((provider) =>
            {
                var path = Path.GetDirectoryName(typeof(KiCadServicesExtensions).Assembly.Location);
                var ctx = new NngLoadContext(path);
                return NngLoadContext.Init(ctx);
            });


            services.AddSingleton<IKiCadFactory, KiCadFactory>();

            return services;
        }

        public static IServiceCollection AddKiCad(this IServiceCollection services, Action<KiCadClientSettings> configure)
        {
            return services.AddKiCad(KiCadClientSettings.DefaultClientName, configure);
        }
    }

    internal class KiCadFactory : IKiCadFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public KiCadFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public KiCad Create(string? clientName = null)
        {
            return _serviceProvider.GetRequiredKeyedService<KiCad>(string.IsNullOrWhiteSpace(clientName) ? KiCadClientSettings.DefaultClientName : clientName);
        }
    }
}
