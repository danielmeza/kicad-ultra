namespace KiCadUltra.Services.Providers.Jlcpcb;

/// <summary>
/// Whether EasyEDA / LCSC search may use JLCPCB's official Components API (#51) in this process.
/// Each container chooses one when it calls <c>AddUltraLibrarianKiCadServices</c>, so the choice is
/// made where the process is put together, never inferred from its arguments.
/// </summary>
/// <remarks>
/// JLCPCB's API terms (III.6(9)) forbid sharing "any data obtained through the API with any third
/// party" without its written consent. The GUI shows results to the user who holds the credentials,
/// so it may use the API. The <c>--mcp</c> server hands every result to whichever AI client is
/// connected, so it never does, even when all three credentials are stored.
/// </remarks>
public sealed class JlcpcbSourcePolicy
{
    private JlcpcbSourcePolicy(bool allowsOfficialApi)
    {
        AllowsOfficialApi = allowsOfficialApi;
    }

    /// <summary>
    /// The GUI's: LCSC-number queries go to the official API once all three credentials are
    /// entered, and everything else to the website endpoint.
    /// </summary>
    public static JlcpcbSourcePolicy OfficialApiWhenConfigured { get; } = new(true);

    /// <summary>
    /// The <c>--mcp</c> server's: every query goes to the website endpoint, and stored credentials
    /// are never read.
    /// </summary>
    public static JlcpcbSourcePolicy WebsiteEndpointOnly { get; } = new(false);

    /// <summary>True when the official API may be called with the user's credentials.</summary>
    public bool AllowsOfficialApi { get; }
}
