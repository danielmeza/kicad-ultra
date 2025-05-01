using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

using UltraLibrarianImporter.KiCadBindings;


var host = Host
    .CreateDefaultBuilder(args)
    .ConfigureLogging((hostContext, builder) =>
    {
        builder.ClearProviders();
        builder.SetMinimumLevel(LogLevel.Information);
        builder.AddSimpleConsole(options =>
        {
            options.SingleLine = false;
            options.TimestampFormat = "HH:mm:ss.fff";
            options.ColorBehavior = LoggerColorBehavior.Enabled;
        });
    })
    .ConfigureServices(services =>
    {
        services.AddKiCad(ServiceTest.ClientName, settings =>
        {
            settings.Token = "72eb9eb5-d0b6-49d4-b5b2-49e665c5f478";
            settings.PipeName = "ipc://C:\\Users\\danie\\AppData\\Local\\Temp\\kicad\\api.sock"; 
        });
        services.AddHostedService<ServiceTest>();
    })
    .Build();

host.Run();

