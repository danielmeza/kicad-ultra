using KiCadSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using SampleConsole;

// Check for command line arguments. The guard below had been commented out, which made the parser
// test run unconditionally and left the entire host bootstrap underneath it unreachable (CS0162).
// Restoring it puts the test back behind its opt-in flag and makes the default path run the host.
if (args.Length > 0 && args[0] == "--test-parser")
{
    Console.WriteLine("Running KiCad Document Parser tests...");
    KiCadTest.TestKiCadParser();
    return;
}

IHost host = Host
    .CreateDefaultBuilder(args)
    .ConfigureLogging(logging => logging
        .ClearProviders()
        .SetMinimumLevel(LogLevel.Information)
        .AddSimpleConsole(options =>
        {
            options.SingleLine = false;
            options.TimestampFormat = "HH:mm:ss.fff";
            options.ColorBehavior = LoggerColorBehavior.Enabled;
        }))
    .ConfigureServices(services => services
        .AddKiCad(ServiceTest.ClientName, settings =>
        {
            settings.Token = "72eb9eb5-d0b6-49d4-b5b2-49e665c5f478";
            settings.PipeName = "ipc://C:\\Users\\danie\\AppData\\Local\\Temp\\kicad\\api.sock";
        })
        .AddHostedService<ServiceTest>())
    .Build();

host.Run();

