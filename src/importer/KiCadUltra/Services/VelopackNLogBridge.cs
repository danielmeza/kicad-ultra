using System;

using NLog;

using Velopack.Logging;

namespace KiCadUltra.Services;

/// <summary>
/// Sends Velopack's own diagnostics to NLog, so they land in the application's log files (#128).
/// </summary>
/// <remarks>
/// <para>
/// Without it, everything Velopack says goes only to a file of its own in the temporary directory -
/// including the line that explains why a start restarted itself, <c>"Auto apply is true, so
/// restarting to apply update..."</c>, and any failure of the update it then applies. That is the
/// one part of a start the user cannot otherwise account for, and #121 and #123 are clear that the
/// application has one log.
/// </para>
/// <para>
/// It writes through <see cref="LogManager"/> rather than taking an
/// <c>Microsoft.Extensions.Logging.ILogger</c> because it is installed before the host exists:
/// <c>Program.Main</c> hands it to <c>VelopackApp.Build().SetLogger(...)</c>, which passes it to the
/// locator, and the locator's logger is also the one <see cref="Velopack.UpdateManager"/> uses
/// later. One bridge therefore covers both the startup hooks and the background checks.
/// </para>
/// </remarks>
internal sealed class VelopackNLogBridge : IVelopackLogger
{
    private static readonly Logger Target = LogManager.GetLogger("Velopack");

    public void Log(VelopackLogLevel logLevel, string? message, Exception? exception) =>
        Target.Log(Map(logLevel), exception, "{Message}", message);

    // Written out rather than cast: the two enumerations happen to agree today, and a cast would
    // turn a future member of Velopack's into a silently wrong severity.
    private static LogLevel Map(VelopackLogLevel level) => level switch
    {
        VelopackLogLevel.Trace => LogLevel.Trace,
        VelopackLogLevel.Debug => LogLevel.Debug,
        VelopackLogLevel.Information => LogLevel.Info,
        VelopackLogLevel.Warning => LogLevel.Warn,
        VelopackLogLevel.Error => LogLevel.Error,
        VelopackLogLevel.Critical => LogLevel.Fatal,
        _ => LogLevel.Info,
    };
}
