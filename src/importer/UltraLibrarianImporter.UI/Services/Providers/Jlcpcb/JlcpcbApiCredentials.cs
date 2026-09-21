using System.Collections.Generic;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services.Providers.Jlcpcb;

/// <summary>How much of JLCPCB's official API access (#51) the user has entered in Settings.</summary>
public enum JlcpcbApiCredentialState
{
    /// <summary>Nothing entered: EasyEDA / LCSC search uses the unofficial website endpoint (#52).</summary>
    None,

    /// <summary>Some of the three values, not all: the official API cannot be called.</summary>
    Incomplete,

    /// <summary>All three values: LCSC part numbers are looked up through the official API.</summary>
    Complete,
}

/// <summary>
/// The three values JLCPCB's API console issues for an application, which together sign every
/// request to the official API (<see cref="JlcpcbOpenApiClient"/>). They come from
/// <see cref="IConfigService"/>, which keeps them in the OS credential store and never in
/// config.json (#54).
/// </summary>
/// <remarks>
/// A class rather than a record on purpose: a record's generated <c>ToString</c> prints every
/// property, and the secret key must never reach a log.
/// </remarks>
public sealed class JlcpcbApiCredentials
{
    /// <summary>JLCPCB's guide to applying for API access, which links on to the API console.</summary>
    public const string ApiGuideUrl = "https://jlcpcb.com/help/article/jlcpcb-online-api-available-now";

    private JlcpcbApiCredentials(string appId, string accessKey, string secretKey)
    {
        AppId = appId;
        AccessKey = accessKey;
        SecretKey = secretKey;
    }

    /// <summary>The application's ID, sent as <c>appid</c>.</summary>
    public string AppId { get; }

    /// <summary>The API key's identifier, sent as <c>accesskey</c>.</summary>
    public string AccessKey { get; }

    /// <summary>The API key's secret, which signs requests and is never sent.</summary>
    public string SecretKey { get; }

    /// <summary>The credentials in <paramref name="config"/>, or null unless all three are entered.</summary>
    public static JlcpcbApiCredentials? FromConfig(IConfigService config) =>
        GetState(config) == JlcpcbApiCredentialState.Complete
            ? new JlcpcbApiCredentials(config.JlcpcbAppId.Trim(), config.JlcpcbAccessKey.Trim(), config.JlcpcbSecretKey.Trim())
            : null;

    public static JlcpcbApiCredentialState GetState(IConfigService config) => GetMissing(config).Count switch
    {
        0 => JlcpcbApiCredentialState.Complete,
        3 => JlcpcbApiCredentialState.None,
        _ => JlcpcbApiCredentialState.Incomplete,
    };

    /// <summary>The names, as Settings labels them, of the values that are still empty.</summary>
    public static IReadOnlyList<string> GetMissing(IConfigService config)
    {
        var missing = new List<string>(3);
        if (string.IsNullOrWhiteSpace(config.JlcpcbAppId))
        {
            missing.Add("App ID");
        }

        if (string.IsNullOrWhiteSpace(config.JlcpcbAccessKey))
        {
            missing.Add("Access Key");
        }

        if (string.IsNullOrWhiteSpace(config.JlcpcbSecretKey))
        {
            missing.Add("Secret Key");
        }

        return missing;
    }
}
