using KiCadSharp;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using NLog.Extensions.Logging;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services;

/// <summary>
/// Configures services for dependency injection
/// </summary>
public static class ServiceConfigurator
{
    /// <summary>
    /// Configures services for the application
    /// </summary>
    /// <param name="services">Service collection to configure</param>
    /// <param name="context">Host builder context supplying the configuration to bind against</param>
    /// <returns>Configured service collection</returns>
    public static IServiceCollection ConfigureServices(IServiceCollection services, HostBuilderContext context) =>
        services
            .AddLogging(logging => logging.ClearProviders().AddNLog())
            // OptionsBuilder<T> breaks the IServiceCollection chain; .Services returns to it.
            .AddOptions<KiCadClientSettings>()
                .Bind(context.Configuration.GetSection("client"))
                .Services
            .AddSingleton<IConfigService, ConfigService>()
            .AddSingleton<KiCadIPCClient>()
            .AddSingleton<KiCad>()
            .AddTransient(provider => provider.GetRequiredService<IConfigService>().GetImportOptions());

    /// <summary>
    /// Configures services for the host
    /// </summary>
    /// <param name="hostBuilder">Host builder to configure</param>
    /// <returns>Configured host builder</returns>
    public static IHostBuilder ConfigureAppServices(this IHostBuilder hostBuilder)
    {
        return hostBuilder.ConfigureServices((context, services) =>
        {
            _ = ConfigureServices(services, context);
        });
    }
}
