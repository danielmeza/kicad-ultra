using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UltraLibrarianImporter.UI.Services.Providers.Jlcpcb;

/// <summary>
/// The fallback source (#52): the endpoint behind JLCPCB's own parts search page. The data is
/// first-party, JLCPCB's parts library served by JLCPCB, but the endpoint is not a published API.
/// Nothing documents or promises it, so it can change or disappear at any time without notice.
/// </summary>
/// <remarks>
/// Used when no official API credentials are configured, and for keyword searches, which the
/// official API cannot answer (<see cref="JlcpcbOpenApiClient"/>). Each search asks for the first
/// page of <see cref="PageSize"/> results and never pages further, and it goes through the provider
/// cache and rate limiter like every other search. The User-Agent stays the honest
/// <c>kicad-ultra/1.0</c> one; it never impersonates a browser.
/// </remarks>
public static class JlcpcbWebsiteSearchClient
{
    public const string SearchUrl = "https://jlcpcb.com/api/overseas-pcb-order/v1/shoppingCart/smtGood/selectSmtComponentList";

    /// <summary>Results asked for per search: the first page only.</summary>
    public const int PageSize = 25;

    /// <summary>
    /// Searches for <paramref name="keyword"/>. Returns the parts the endpoint listed, which is
    /// empty only when it listed none; throws for anything short of an answer.
    /// </summary>
    /// <exception cref="HttpRequestException">A non-success HTTP status, or no response.</exception>
    /// <exception cref="JlcpcbApiException">The endpoint reported an error in the body.</exception>
    /// <exception cref="JsonException">The body is not the expected shape.</exception>
    public static async Task<IReadOnlyList<JlcpcbPart>> SearchAsync(HttpClient httpClient, string keyword, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new { keyword, currentPage = 1, pageSize = PageSize });

        using var request = new HttpRequestMessage(HttpMethod.Post, SearchUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        // Anything short of an answer throws, so the aggregator leaves it out and does not cache it.
        _ = response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        var code = JlcpcbJson.GetInt32(root, "code");
        if (code != 200)
        {
            throw new JlcpcbApiException(
                $"JLCPCB website search error {code?.ToString(CultureInfo.InvariantCulture) ?? "(no code)"}: " +
                $"{JlcpcbJson.GetString(root, "message") ?? "no message"}");
        }

        if (!root.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("componentPageInfo", out JsonElement page) ||
            page.ValueKind != JsonValueKind.Object ||
            !page.TryGetProperty("list", out JsonElement list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("JLCPCB website search response has no data.componentPageInfo.list array.");
        }

        // A row that names neither an LCSC code nor a part number identifies nothing, so it is left
        // out rather than shown with the query standing in for its part number.
        return [.. list.EnumerateArray()
            .Take(PageSize)
            .Select(ReadPart)
            .Where(part => part.LcscPartNumber is not null || part.ManufacturerPartNumber is not null)];
    }

    private static JlcpcbPart ReadPart(JsonElement item) => new(
        LcscPartNumber: JlcpcbJson.GetLcscPartNumber(item, "componentCode"),
        ManufacturerPartNumber: JlcpcbJson.GetString(item, "componentModelEn"),
        Manufacturer: JlcpcbJson.GetString(item, "componentBrandEn"),
        Description: JlcpcbJson.GetString(item, "describe"),
        Package: JlcpcbJson.GetString(item, "componentSpecificationEn"),
        Stock: JlcpcbJson.GetInt32(item, "stockCount"),
        PriceBreaks: JlcpcbJson.GetPriceBreaks(item, "componentPrices", "startNumber", "productPrice"),
        DatasheetUrl: JlcpcbJson.GetFirstString(item, "dataManualUrl", "dataManualOfficialLink"));
}
