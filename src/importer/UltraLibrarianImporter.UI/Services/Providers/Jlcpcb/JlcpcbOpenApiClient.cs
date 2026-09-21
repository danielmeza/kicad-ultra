using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UltraLibrarianImporter.UI.Services.Providers.Jlcpcb;

/// <summary>
/// JLCPCB's official Components API (#51), on the Open Platform documented at
/// https://api.jlcpcb.com/docs/start. Every call is a JSON POST signed with the user's own
/// <see cref="JlcpcbApiCredentials"/>.
/// </summary>
/// <remarks>
/// <para>
/// The public documentation gives the host, the rule that every call is a signed JSON POST, and the
/// signature scheme with a worked example, which <see cref="Sign"/> and
/// <see cref="FormatAuthorizationParameter"/> reproduce exactly. Its API list names three Components
/// interfaces: a paged feed of the whole public library, the account's private library, and a
/// detail lookup by C-number. None of them searches by keyword, so this client only looks parts up
/// by LCSC number. The feed exists to mirror the whole catalogue, which a search box must never do.
/// </para>
/// <para>
/// Each interface's path and fields are documented only inside the API console, behind a login.
/// The path, request body and response fields below are the detail lookup as independent
/// open-source clients call it (yaqwsx/jlcparts among them); they have not been checked against the
/// console documentation or with real credentials here.
/// </para>
/// </remarks>
public static class JlcpcbOpenApiClient
{
    /// <summary>The Open Platform endpoint, as JLCPCB's SDK guide configures it.</summary>
    public const string Host = "https://open.jlcpcb.com";

    /// <summary>The "Query Component Detail Data" interface: component details by C-number.</summary>
    public const string ComponentDetailPath = "/overseas/openapi/component/getComponentDetailByCode";

    // "32-character random string (letters A-Z, a-z, digits 0-9)"
    private const string NonceAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const int NonceLength = 32;

    /// <summary>
    /// Looks <paramref name="lcscPartNumber"/> up. Returns the parts the API described, which is
    /// empty only when it answered with none; throws for anything short of an answer.
    /// </summary>
    /// <exception cref="HttpRequestException">A non-success HTTP status, or no response.</exception>
    /// <exception cref="JlcpcbApiException">The API reported an error in the body.</exception>
    /// <exception cref="JsonException">The body is not the expected shape.</exception>
    public static async Task<IReadOnlyList<JlcpcbPart>> GetComponentDetailAsync(
        HttpClient httpClient,
        JlcpcbApiCredentials credentials,
        string lcscPartNumber,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // Serialised once, so the bytes that are signed are the bytes that are sent.
        var body = JsonSerializer.Serialize(new { componentCodes = new[] { lcscPartNumber } });
        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds();

        using var request = new HttpRequestMessage(HttpMethod.Post, Host + ComponentDetailPath);
        request.Headers.Authorization = CreateAuthorization(credentials, HttpMethod.Post.Method, ComponentDetailPath, body, timestamp, CreateNonce());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // The documented "Content-Type: application/json", without the charset StringContent would add.
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var traceId = response.Headers.TryGetValues("J-Trace-ID", out IEnumerable<string>? ids) ? string.Join(",", ids) : "none";

        if (!response.IsSuccessStatusCode)
        {
            // Documented: 400 invalid parameters, 401 signature rejected, 403 forbidden (for
            // example an IP whitelist), 500 platform error.
            throw new HttpRequestException(
                $"JLCPCB Components API returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): " +
                $"{JlcpcbJson.TryGetMessage(json) ?? "no message"}. J-Trace-ID: {traceId}",
                null,
                response.StatusCode);
        }

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // HTTP 200 only means the platform answered; the body's code says whether the call worked.
        var code = JlcpcbJson.GetInt32(root, "code");
        if (code != 200 || IsFalse(root, "success") || IsFalse(root, "successful"))
        {
            throw new JlcpcbApiException(
                $"JLCPCB Components API error {code?.ToString(CultureInfo.InvariantCulture) ?? "(no code)"}: " +
                $"{JlcpcbJson.GetString(root, "message") ?? "no message"}. J-Trace-ID: {traceId}");
        }

        JsonElement details = GetDetailList(root)
            ?? throw new JsonException($"JLCPCB Components API response has no component detail list. J-Trace-ID: {traceId}");

        return [.. details.EnumerateArray().Select(ReadPart)];
    }

    /// <summary>
    /// The request's <c>Authorization</c> header:
    /// <c>JOP appid="…",accesskey="…",nonce="…",timestamp="…",signature="…"</c>, on one line.
    /// </summary>
    public static AuthenticationHeaderValue CreateAuthorization(
        JlcpcbApiCredentials credentials, string method, string path, string body, long timestamp, string nonce) =>
        new("JOP", FormatAuthorizationParameter(
            credentials.AppId,
            credentials.AccessKey,
            nonce,
            timestamp,
            Sign(BuildStringToSign(method, path, timestamp, nonce, body), credentials.SecretKey)));

    /// <summary>
    /// The documented string to sign: method, path (with its query, if any), Unix timestamp in
    /// seconds, nonce and the raw JSON body, each followed by <c>\n</c>, the last one included.
    /// </summary>
    public static string BuildStringToSign(string method, string path, long timestamp, string nonce, string body) =>
        string.Create(CultureInfo.InvariantCulture, $"{method}\n{path}\n{timestamp}\n{nonce}\n{body}\n");

    /// <summary>HMAC-SHA256 of <paramref name="stringToSign"/> with the secret key, Base64-encoded.</summary>
    public static string Sign(string stringToSign, string secretKey) =>
        Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretKey), Encoding.UTF8.GetBytes(stringToSign)));

    /// <summary>What follows <c>JOP </c> in the <c>Authorization</c> header.</summary>
    public static string FormatAuthorizationParameter(string appId, string accessKey, string nonce, long timestamp, string signature) =>
        string.Create(CultureInfo.InvariantCulture,
            $"appid=\"{appId}\",accesskey=\"{accessKey}\",nonce=\"{nonce}\",timestamp=\"{timestamp}\",signature=\"{signature}\"");

    /// <summary>A fresh 32-character nonce from A-Z, a-z and 0-9, from a cryptographic RNG.</summary>
    public static string CreateNonce() => RandomNumberGenerator.GetString(NonceAlphabet, NonceLength);

    // "data" is the list itself, or an object holding it.
    private static JsonElement? GetDetailList(JsonElement root) =>
        !root.TryGetProperty("data", out JsonElement data) ? null
        : data.ValueKind == JsonValueKind.Array ? data
        : data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("componentDetailResponseVOList", out JsonElement list)
            && list.ValueKind == JsonValueKind.Array ? list
        : null;

    private static bool IsFalse(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.False;

    private static JlcpcbPart ReadPart(JsonElement item) => new(
        LcscPartNumber: JlcpcbJson.GetLcscPartNumber(item, "componentCode"),
        ManufacturerPartNumber: JlcpcbJson.GetString(item, "componentModel"),
        Manufacturer: JlcpcbJson.GetString(item, "manufacturer"),
        Description: JlcpcbJson.GetString(item, "description"),
        Package: JlcpcbJson.GetString(item, "componentSpecification"),
        Stock: JlcpcbJson.GetInt32(item, "stockCount"),
        PriceBreaks: JlcpcbJson.GetPriceBreaks(item, "priceRanges", "startQuantity", "unitPrice"),
        DatasheetUrl: JlcpcbJson.GetFirstString(item, "datasheetUrl", "dataManualUrl", "dataManualOfficialLink"));
}
