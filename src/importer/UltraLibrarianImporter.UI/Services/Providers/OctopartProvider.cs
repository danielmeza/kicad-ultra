using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using UltraLibrarianImporter.UI.Services.EasyEda2KiCad;
using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services.Providers;

public sealed partial class OctopartProvider : BaseArchiveComponentProvider
{
    private readonly IConfigService _configService;
    private readonly ILogger<OctopartProvider> _logger;
    private readonly HttpClient _httpClient;
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    // The optional fields Nexar refused to the token's plan (#109), and the token they were refused
    // to. Searches with that token leave them out for the rest of the session.
    private readonly Lock _refusalsGate = new();
    private string? _refusalsToken;
    private OptionalFields _refusedFields;

    // LCSC's company slug on Octopart, as in octopart.com/distributors/lcsc.
    private const string LcscCompanySlug = "lcsc";

    public OctopartProvider(IConfigService configService, ILogger<OctopartProvider> logger)
        : this(configService, logger, SharedHttpClient)
    {
    }

    // Takes the HttpClient so that a harness can answer Nexar's requests itself.
    internal OctopartProvider(IConfigService configService, ILogger<OctopartProvider> logger, HttpClient httpClient)
    {
        _configService = configService;
        _logger = logger;
        _httpClient = httpClient;
    }

    /// <summary>
    /// The fields of the search that a result can do without. A field Nexar refuses to the token's
    /// plan is left out rather than costing the search (#109).
    /// </summary>
    /// <remarks>
    /// Nexar's schema (<c>https://api.nexar.com/graphql?sdl</c>) sells two of them by plan:
    /// <c>bestDatasheet</c> carries <c>@HasPlanTier(tier: PRO) @NexarRequire(feature: DATASHEETS)</c>,
    /// and <c>cad</c> carries <c>@RequireAny(roles: [ CADMODELS ])</c>. <c>allSellers</c> carries no
    /// plan directive, only the <c>@BlockAll(roles: [ DISTRIBUTOR ])</c> that the authorized
    /// <c>sellers</c> carry too, but it only supplies the LCSC code, so a refusal of it is treated
    /// the same way. Everything else the search asks for carries no such directive.
    /// </remarks>
    [Flags]
    private enum OptionalFields
    {
        None = 0,
        BestDatasheet = 1,
        Cad = 2,
        AllSellers = 4,
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
        var token = _configService.OctopartApiToken?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(token))
        {
            throw new ProviderNotConfiguredException("No Octopart (Nexar) API token is configured.");
        }

        try
        {
            return await SearchNexarAsync(query, token, GetRefusedFields(token), mayRetry: true, cancellationToken);
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
    }

    /// <summary>
    /// Sends the search without the fields in <paramref name="omitted"/>, and reads the parts from
    /// Nexar's answer. Anything short of an answer throws, so the aggregator leaves it out and does
    /// not cache it, except a refusal of optional fields (#109): that search is sent once more
    /// without them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nexar reports a field outside the token's plan as "Unauthorized to access '…'". Its support
    /// article "Quick Fixes to Try" quotes "Unauthorized to access 'best_datasheet'. Please upgrade
    /// your Nexar subscription to include 'DATASHEETS'". Nexar does not document whether that error
    /// comes beside the rest of the data or instead of it, nor with which HTTP status, so both are
    /// handled:
    /// </para>
    /// <list type="bullet">
    /// <item>With results, the answer is used as it came. A refused field is null or missing, so CAD
    /// reads as unknown (see <see cref="ReadCad"/>), and the row has no datasheet or LCSC code.</item>
    /// <item>Without results, if every error is a refusal of an optional field, the search is sent
    /// once more without those fields (<paramref name="mayRetry"/>). A second failure throws.</item>
    /// </list>
    /// <para>
    /// Either way the refused fields are remembered for the token, so later searches leave them out
    /// from the start. Any other error is no refusal and still throws: a rejected token
    /// (<c>AuthInvalidToken</c>, <c>AuthExpiredToken</c>), a spent quota, or a refusal of a field the
    /// search cannot do without.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<PartSearchResult>> SearchNexarAsync(
        string query, string token, OptionalFields omitted, bool mayRetry, CancellationToken cancellationToken)
    {
        var graphQuery = new { query = BuildSearchQuery(omitted), variables = new { q = query } };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.nexar.com/graphql");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(graphQuery), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument doc = ParseResponse(response, json);

        OptionalFields refused = ReadRefusals(doc.RootElement, out var onlyRefusals);
        RememberRefusedFields(token, refused);

        if (response.IsSuccessStatusCode && TryGetResults(doc.RootElement, out JsonElement resultsArray))
        {
            return ReadParts(resultsArray, TryGetErrors(doc.RootElement, out _));
        }

        if (mayRetry && onlyRefusals && (refused & ~omitted) != OptionalFields.None)
        {
            return await SearchNexarAsync(query, token, omitted | refused, mayRetry: false, cancellationToken);
        }

        _ = response.EnsureSuccessStatusCode();

        // A GraphQL server can report a failure such as a rejected token or a spent quota in
        // "errors" with HTTP 200 and "data": null. Either way, this is not an answer.
        var errors = TryGetErrors(doc.RootElement, out JsonElement e) ? e.GetRawText() : "none";
        throw new JsonException($"Nexar response has no supSearch results (errors: {errors}).");
    }

    private static string BuildSearchQuery(OptionalFields omitted)
    {
        string Unless(OptionalFields field, string selection) => (omitted & field) != OptionalFields.None ? string.Empty : selection;

        return $$"""
            query SearchParts($q: String!) {
                supSearch(q: $q, limit: 5) {
                    results {
                        part {
                            mpn
                            manufacturer { name }
                            shortDescription
                            {{Unless(OptionalFields.BestDatasheet, "bestDatasheet { url }")}}
                            {{Unless(OptionalFields.AllSellers, "allSellers: sellers(authorizedOnly: false) { company { slug } offers { sku } }")}}
                            sellers(authorizedOnly: true) {
                                company { name }
                                offers {
                                    inventoryLevel
                                    prices { price currency quantity }
                                }
                            }
                            {{Unless(OptionalFields.Cad, "cad { hasKicad has3dModel }")}}
                        }
                    }
                }
            }
            """;
    }

    /// <param name="resultsArray">The <c>supSearch.results</c> of Nexar's answer.</param>
    /// <param name="responseHasErrors">Whether errors came with the results, for a field the token's
    /// plan does not include. Any one of them may be what left a part's <c>cad</c> null, so a null
    /// then says nothing about CAD.</param>
    private List<PartSearchResult> ReadParts(JsonElement resultsArray, bool responseHasErrors)
    {
        var results = new List<PartSearchResult>();
        foreach (JsonElement item in resultsArray.EnumerateArray())
        {
            if (!item.TryGetProperty("part", out JsonElement part))
                continue;

            // Empty rather than the search text when Nexar gives none: the query is not a part number.
            var mpn = part.GetProperty("mpn").GetString() ?? string.Empty;
            var mfg = part.TryGetProperty("manufacturer", out JsonElement m) && m.TryGetProperty("name", out JsonElement n)
                ? n.GetString() ?? "Unknown" : "Unknown";
            var desc = part.TryGetProperty("shortDescription", out JsonElement d) ? d.GetString() ?? "" : "";
            // Null when the part has none, and when the token's plan does not include datasheets (#109).
            var datasheet = part.TryGetProperty("bestDatasheet", out JsonElement ds) && ds.ValueKind == JsonValueKind.Object &&
                ds.TryGetProperty("url", out JsonElement u) ? u.GetString() : null;
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

        return results;
    }

    // A body that is not JSON is no GraphQL response at all, so its HTTP status is the answer.
    private static JsonDocument ParseResponse(HttpResponseMessage response, string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException) when (!response.IsSuccessStatusCode)
        {
            _ = response.EnsureSuccessStatusCode();
            throw;
        }
    }

    private static bool TryGetResults(JsonElement root, out JsonElement results)
    {
        results = default;
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("data", out JsonElement data) &&
            data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("supSearch", out JsonElement supSearch) &&
            supSearch.ValueKind == JsonValueKind.Object &&
            supSearch.TryGetProperty("results", out results) &&
            results.ValueKind == JsonValueKind.Array;
    }

    private static bool TryGetErrors(JsonElement root, out JsonElement errors)
    {
        errors = default;
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("errors", out errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0;
    }

    /// <summary>
    /// The optional fields that the response's errors refuse to the token's plan.
    /// </summary>
    /// <param name="root">Nexar's response.</param>
    /// <param name="onlyRefusals">Whether there are errors, and every one of them is such a refusal.</param>
    private static OptionalFields ReadRefusals(JsonElement root, out bool onlyRefusals)
    {
        onlyRefusals = false;
        if (!TryGetErrors(root, out JsonElement errors))
        {
            return OptionalFields.None;
        }

        OptionalFields refused = OptionalFields.None;
        onlyRefusals = true;
        foreach (JsonElement error in errors.EnumerateArray())
        {
            OptionalFields field = ReadRefusedField(error);
            refused |= field;
            onlyRefusals &= field != OptionalFields.None;
        }

        return refused;
    }

    /// <summary>
    /// The optional field that <paramref name="error"/> refuses to the token's plan, or
    /// <see cref="OptionalFields.None"/> for any other error, including a refusal of a field the
    /// search cannot do without.
    /// </summary>
    /// <remarks>
    /// An error about a field carries that field's <c>path</c> in the response (GraphQL spec, section
    /// 7.1.2 "Errors"), and then the path decides. Without a path, the field the message names
    /// decides. Nexar names it as its own schema does, so <c>best_datasheet</c>, not
    /// <c>bestDatasheet</c>.
    /// </remarks>
    private static OptionalFields ReadRefusedField(JsonElement error)
    {
        if (error.ValueKind != JsonValueKind.Object ||
            !error.TryGetProperty("message", out JsonElement message) ||
            message.ValueKind != JsonValueKind.String)
        {
            return OptionalFields.None;
        }

        Match refusal = PlanRefusalPattern().Match(message.GetString()!);
        if (!refusal.Success)
        {
            return OptionalFields.None;
        }

        if (!error.TryGetProperty("path", out JsonElement path) || path.ValueKind != JsonValueKind.Array)
        {
            return OptionalFieldNamed(refusal.Groups["field"].Value);
        }

        // The first optional field on the path. A refusal of something inside it, such as the
        // offers of allSellers, leaves out the whole field.
        foreach (JsonElement segment in path.EnumerateArray())
        {
            OptionalFields field = segment.ValueKind == JsonValueKind.String
                ? OptionalFieldNamed(segment.GetString())
                : OptionalFields.None;
            if (field != OptionalFields.None)
            {
                return field;
            }
        }

        return OptionalFields.None;
    }

    // A field by its name in the search, or in Nexar's own schema (the SDL's @source name).
    private static OptionalFields OptionalFieldNamed(string? name) => name switch
    {
        "bestDatasheet" or "best_datasheet" => OptionalFields.BestDatasheet,
        "cad" => OptionalFields.Cad,
        // Only by its alias: in Nexar's schema it is "sellers", the same as the authorized sellers
        // that stock and price come from, which the search cannot do without.
        "allSellers" => OptionalFields.AllSellers,
        _ => OptionalFields.None,
    };

    // Nexar's words for a field outside the token's plan, as its support article quotes them:
    // "Unauthorized to access 'best_datasheet'. Please upgrade your Nexar subscription to include 'DATASHEETS'".
    [GeneratedRegex(@"Unauthorized to access '(?<field>[^']+)'", RegexOptions.CultureInvariant)]
    private static partial Regex PlanRefusalPattern();

    private OptionalFields GetRefusedFields(string token)
    {
        lock (_refusalsGate)
        {
            return token == _refusalsToken ? _refusedFields : OptionalFields.None;
        }
    }

    private void RememberRefusedFields(string token, OptionalFields refused)
    {
        if (refused == OptionalFields.None)
        {
            return;
        }

        OptionalFields added;
        lock (_refusalsGate)
        {
            if (token != _refusalsToken)
            {
                // Another token can come with another plan.
                _refusalsToken = token;
                _refusedFields = OptionalFields.None;
            }

            added = refused & ~_refusedFields;
            _refusedFields |= refused;
        }

        if (added != OptionalFields.None)
        {
            _logger.LogWarning(
                "Nexar refused {Fields} to this token's plan, so Octopart searches leave them out until the token changes or the app restarts",
                added);
        }
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
    /// <para>
    /// No <c>cad</c> at all means the search left it out, because the token's plan does not include
    /// it (#109). Then nothing is known.
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
