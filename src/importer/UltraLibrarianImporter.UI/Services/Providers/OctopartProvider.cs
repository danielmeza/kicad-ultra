using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using UltraLibrarianImporter.UI.Services.EasyEda2KiCad;
using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services.Providers;

public sealed class OctopartProvider : BaseArchiveComponentProvider
{
    private readonly IConfigService _configService;
    private readonly ILogger<OctopartProvider> _logger;
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    // LCSC's company slug on Octopart, as in octopart.com/distributors/lcsc.
    private const string LcscCompanySlug = "lcsc";

    public OctopartProvider(IConfigService configService, ILogger<OctopartProvider> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public override string Id => "octopart";
    public override string DisplayName => "Octopart (Nexar)";
    public override string SearchUrl => "https://octopart.com/";
    public override string DefaultPrefix => "OCT_";
    public override string DefaultLibraryName => "Octopart";
    public override string ProviderColor => "#7B1FA2";

    public override bool SupportsDirectApi => true;

    public override async Task<IReadOnlyList<PartSearchResult>> SearchPartsAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = new List<PartSearchResult>();

        var token = _configService.OctopartApiToken?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(token))
        {
            throw new ProviderNotConfiguredException("No Octopart (Nexar) API token is configured.");
        }

        try
        {
            // Nexar GraphQL API query for parts
            var graphQuery = new
            {
                query = @"query SearchParts($q: String!) {
                        supSearch(q: $q, limit: 5) {
                            results {
                                part {
                                    mpn
                                    manufacturer { name }
                                    shortDescription
                                    bestDatasheet { url }
                                    allSellers: sellers(authorizedOnly: false) {
                                        company { slug }
                                        offers { sku }
                                    }
                                    sellers(authorizedOnly: true) {
                                        company { name }
                                        offers {
                                            inventoryLevel
                                            prices { price currency quantity }
                                        }
                                    }
                                    cad {
                                        hasKicad
                                        has3dModel
                                    }
                                }
                            }
                        }
                    }",
                variables = new { q = query }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.nexar.com/graphql");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(JsonSerializer.Serialize(graphQuery), Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            // Anything short of an answer throws, so the aggregator leaves it out and does not cache it.
            _ = response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out JsonElement data) ||
                data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("supSearch", out JsonElement supSearch) ||
                supSearch.ValueKind != JsonValueKind.Object ||
                !supSearch.TryGetProperty("results", out JsonElement resultsArray) ||
                resultsArray.ValueKind != JsonValueKind.Array)
            {
                // A GraphQL server can report a failure such as a rejected token or a spent quota in
                // "errors" with HTTP 200 and "data": null. Either way, this is not an answer.
                var errors = doc.RootElement.TryGetProperty("errors", out JsonElement e) ? e.GetRawText() : "none";
                throw new JsonException($"Nexar response has no supSearch results (errors: {errors}).");
            }

            // Results can come with errors too, for a field the token's plan does not include. Any one
            // of them may be what left a part's "cad" null, so a null then says nothing about CAD.
            var responseHasErrors = doc.RootElement.TryGetProperty("errors", out JsonElement partialErrors) &&
                partialErrors.ValueKind == JsonValueKind.Array &&
                partialErrors.GetArrayLength() > 0;

            foreach (JsonElement item in resultsArray.EnumerateArray())
            {
                if (!item.TryGetProperty("part", out JsonElement part))
                    continue;

                // Empty rather than the search text when Nexar gives none: the query is not a part number.
                var mpn = part.GetProperty("mpn").GetString() ?? string.Empty;
                var mfg = part.TryGetProperty("manufacturer", out JsonElement m) && m.TryGetProperty("name", out JsonElement n)
                    ? n.GetString() ?? "Unknown" : "Unknown";
                var desc = part.TryGetProperty("shortDescription", out JsonElement d) ? d.GetString() ?? "" : "";
                var datasheet = part.TryGetProperty("bestDatasheet", out JsonElement ds) && ds.TryGetProperty("url", out JsonElement u)
                    ? u.GetString() : null;
                var lcscPartNumber = FindLcscPartNumber(part);

                (CadAvailability kicad, CadAvailability model3D) = ReadCad(part, responseHasErrors);

                var totalStock = 0;
                decimal? bestUnitPrice = null;
                var bestTierQuantity = int.MaxValue;
                var detectedCurrency = "USD";

                if (part.TryGetProperty("sellers", out JsonElement sellers) && sellers.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement seller in sellers.EnumerateArray())
                    {
                        if (!seller.TryGetProperty("offers", out JsonElement offers) || offers.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (JsonElement offer in offers.EnumerateArray())
                        {
                            if (offer.TryGetProperty("inventoryLevel", out JsonElement inv) && inv.TryGetInt32(out var invQty))
                            {
                                totalStock += invQty;
                            }

                            if (!offer.TryGetProperty("prices", out JsonElement prices) || prices.ValueKind != JsonValueKind.Array)
                                continue;

                            foreach (JsonElement p in prices.EnumerateArray())
                            {
                                if (!p.TryGetProperty("price", out JsonElement priceVal) || !priceVal.TryGetDecimal(out var dec) || dec <= 0)
                                    continue;

                                var qty = 1;
                                if (p.TryGetProperty("quantity", out JsonElement qVal) && qVal.TryGetInt32(out var parsedQty) && parsedQty > 0)
                                {
                                    qty = parsedQty;
                                }

                                if (p.TryGetProperty("currency", out JsonElement currProp) && !string.IsNullOrWhiteSpace(currProp.GetString()))
                                {
                                    detectedCurrency = currProp.GetString()!;
                                }

                                // Prefer lowest quantity tier (e.g. qty 1) rather than 10,000 unit minimum price
                                if (qty < bestTierQuantity || (qty == bestTierQuantity && (!bestUnitPrice.HasValue || dec < bestUnitPrice.Value)))
                                {
                                    bestTierQuantity = qty;
                                    bestUnitPrice = dec;
                                }
                            }
                        }
                    }
                }

                results.Add(new PartSearchResult(
                    ProviderId: Id,
                    ProviderName: DisplayName,
                    PartNumber: mpn,
                    Manufacturer: mfg,
                    Description: desc,
                    BestPrice: bestUnitPrice,
                    Currency: detectedCurrency,
                    Stock: totalStock > 0 ? totalStock : null,
                    HasSymbol: kicad,
                    HasFootprint: kicad,
                    Has3DModel: model3D,
                    DatasheetUrl: datasheet,
                    ProviderColor: ProviderColor,
                    LcscPartNumber: lcscPartNumber
                ));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Nexar HTTP request failed");
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Nexar returned unparseable JSON");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error searching Octopart/Nexar parts");
            throw;
        }

        return results;
    }

    /// <summary>
    /// The LCSC code of LCSC's own offer for the part, which the easyeda2kicad import converts (#47, #76),
    /// or <c>null</c> when LCSC has no offer that carries one.
    /// </summary>
    /// <remarks>
    /// LCSC's SKU is its part code: on octopart.com, LCSC is the distributor company <c>lcsc</c>
    /// (octopart.com/distributors/lcsc), and its offers carry SKUs such as <c>C41431778</c>. Octopart
    /// usually lists LCSC as a non-authorized seller, so the SKU is read from <c>allSellers</c>, every
    /// seller except brokers, and not from the authorized sellers the stock and price come from. A SKU
    /// that is not an LCSC code is ignored rather than guessed at.
    /// </remarks>
    private static string? FindLcscPartNumber(JsonElement part)
    {
        if (!part.TryGetProperty("allSellers", out JsonElement sellers) || sellers.ValueKind != JsonValueKind.Array)
            return null;

        foreach (JsonElement seller in sellers.EnumerateArray())
        {
            if (!seller.TryGetProperty("company", out JsonElement company) ||
                !company.TryGetProperty("slug", out JsonElement slug) ||
                !string.Equals(slug.GetString(), LcscCompanySlug, StringComparison.OrdinalIgnoreCase) ||
                !seller.TryGetProperty("offers", out JsonElement offers) ||
                offers.ValueKind != JsonValueKind.Array)
                continue;

            foreach (JsonElement offer in offers.EnumerateArray())
            {
                var sku = offer.TryGetProperty("sku", out JsonElement s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                if (EasyEda2KiCadConverter.IsLcscPartNumber(sku))
                {
                    return sku;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a part's CAD availability from Nexar's <c>cad</c> field (#48): whether its CAD model can
    /// be downloaded in KiCad format, and whether the downloads include a 3D (STEP) model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>cad</c> is the schema's public CAD field. The schema also has <c>cadModels</c>, with separate
    /// symbol and footprint flags, but documents it as internal. <c>cad</c> does not split symbol from
    /// footprint: it describes the part's CAD model, which Octopart counts as a symbol plus a
    /// footprint (the schema's <c>SupCadBucket</c>: "parts that have CAD Symbol + Footprint and 3D
    /// model"). So <c>hasKicad</c> answers for both.
    /// </para>
    /// <para>
    /// A null <c>cad</c> means the part has no CAD model: the schema says one can then be requested
    /// through <c>cadRequestUrl</c>. That holds only in a response without errors, see
    /// <paramref name="responseHasErrors"/>.
    /// </para>
    /// </remarks>
    private static (CadAvailability KiCad, CadAvailability Model3D) ReadCad(JsonElement part, bool responseHasErrors)
    {
        if (!part.TryGetProperty("cad", out JsonElement cad))
        {
            return (CadAvailability.Unknown, CadAvailability.Unknown);
        }

        if (cad.ValueKind == JsonValueKind.Null)
        {
            return responseHasErrors
                ? (CadAvailability.Unknown, CadAvailability.Unknown)
                : (CadAvailability.NotAvailable, CadAvailability.NotAvailable);
        }

        return (ReadFlag(cad, "hasKicad"), ReadFlag(cad, "has3dModel"));
    }

    // Anything but a boolean is not an answer.
    private static CadAvailability ReadFlag(JsonElement cad, string name) =>
        !cad.TryGetProperty(name, out JsonElement flag) ? CadAvailability.Unknown
        : flag.ValueKind == JsonValueKind.True ? CadAvailability.Available
        : flag.ValueKind == JsonValueKind.False ? CadAvailability.NotAvailable
        : CadAvailability.Unknown;
}
