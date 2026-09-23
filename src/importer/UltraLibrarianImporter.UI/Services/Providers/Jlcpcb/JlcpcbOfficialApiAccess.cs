using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services.Providers.Jlcpcb;

/// <summary>
/// Whether JLCPCB has refused this application the official Components API, for the credentials
/// entered now (#126). One per process, shared by <see cref="EasyEdaProvider"/>, which records a
/// refusal, and the Part Explorer's JLCPCB notice, which tells the user about it.
/// </summary>
/// <remarks>
/// <para>
/// JLCPCB grants API access per service type, and the Components API needs the application's
/// <b>Parts</b> permission approved; an account can hold working credentials whose permissions are
/// all rejected. A refusal is therefore not a fault to retry but a standing answer: it is remembered
/// for the rest of the session, and the LCSC-number searches it would have answered go to JLCPCB's
/// website endpoint instead, exactly as they do for a user with no credentials.
/// </para>
/// <para>
/// The memory is keyed to the credentials it was refused to, as Octopart's is keyed to the Nexar
/// token (#109): editing any of the three values in Settings is another application or another key,
/// which JLCPCB may answer differently, so it starts over. The key is a hash and never the values,
/// so no credential is held here and none can reach a log.
/// </para>
/// </remarks>
public sealed class JlcpcbOfficialApiAccess
{
    private readonly Lock _gate = new();
    private string? _refusedCredentials;
    private JlcpcbApiRefusal _refusal;

    /// <summary>
    /// How JLCPCB refused <paramref name="credentials"/> the Components API, or null while it has
    /// not. Anything but null means the API is not to be called with them again.
    /// </summary>
    public JlcpcbApiRefusal? GetRefusal(JlcpcbApiCredentials credentials)
    {
        var key = Identify(credentials);
        lock (_gate)
        {
            return key == _refusedCredentials ? _refusal : null;
        }
    }

    /// <summary>
    /// How JLCPCB refused the credentials in <paramref name="config"/>: what the Part Explorer's
    /// notice says. Null while it has not, and null whenever the three values are not all entered,
    /// since the API is then never called.
    /// </summary>
    public JlcpcbApiRefusal? GetRefusal(IConfigService config) =>
        JlcpcbApiCredentials.FromConfig(config) is { } credentials ? GetRefusal(credentials) : null;

    /// <summary>
    /// Remembers that JLCPCB refused <paramref name="credentials"/>. Returns true the first time,
    /// so that the caller logs it once rather than once per search.
    /// </summary>
    public bool Remember(JlcpcbApiCredentials credentials, JlcpcbApiRefusal refusal)
    {
        var key = Identify(credentials);
        lock (_gate)
        {
            var isNew = key != _refusedCredentials || refusal != _refusal;
            _refusedCredentials = key;
            _refusal = refusal;
            return isNew;
        }
    }

    // The credentials as a value that identifies them without being them: SHA-256 over the three,
    // separated by a character none of them can hold, so that neither this object nor anything that
    // prints it carries a credential.
    private static string Identify(JlcpcbApiCredentials credentials) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{credentials.AppId}\n{credentials.AccessKey}\n{credentials.SecretKey}")));
}
