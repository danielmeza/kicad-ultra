using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services.Providers
{
    public sealed class OctopartProvider : BaseArchiveComponentProvider
    {
        private readonly IConfigService _configService;
        private readonly ILogger<OctopartProvider> _logger;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

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

            string token = _configService.OctopartApiToken?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(token))
            {
                return results;
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
                                    sellers(authorizedOnly: true) {
                                        company { name }
                                        offers {
                                            inventoryLevel
                                            prices { price currency quantity }
                                        }
                                    }
                                    cadModels {
                                        hasSymbol
                                        hasFootprint
                                        has3DModel
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

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Nexar request failed with status code {StatusCode}", response.StatusCode);
                    return results;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("supSearch", out var supSearch) ||
                    !supSearch.TryGetProperty("results", out var resultsArray) ||
                    resultsArray.ValueKind != JsonValueKind.Array)
                {
                    return results;
                }

                foreach (var item in resultsArray.EnumerateArray())
                {
                    if (!item.TryGetProperty("part", out var part))
                        continue;

                    string mpn = part.GetProperty("mpn").GetString() ?? query;
                    string mfg = part.TryGetProperty("manufacturer", out var m) && m.TryGetProperty("name", out var n)
                        ? n.GetString() ?? "Unknown" : "Unknown";
                    string desc = part.TryGetProperty("shortDescription", out var d) ? d.GetString() ?? "" : "";
                    string? datasheet = part.TryGetProperty("bestDatasheet", out var ds) && ds.TryGetProperty("url", out var u)
                        ? u.GetString() : null;

                    bool hasSym = false, hasFp = false, has3D = false;
                    if (part.TryGetProperty("cadModels", out var cad))
                    {
                        hasSym = cad.TryGetProperty("hasSymbol", out var hs) && hs.GetBoolean();
                        hasFp = cad.TryGetProperty("hasFootprint", out var hf) && hf.GetBoolean();
                        has3D = cad.TryGetProperty("has3DModel", out var hm) && hm.GetBoolean();
                    }

                    int totalStock = 0;
                    decimal? bestUnitPrice = null;
                    int bestTierQuantity = int.MaxValue;
                    string detectedCurrency = "USD";

                    if (part.TryGetProperty("sellers", out var sellers) && sellers.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var seller in sellers.EnumerateArray())
                        {
                            if (!seller.TryGetProperty("offers", out var offers) || offers.ValueKind != JsonValueKind.Array)
                                continue;

                            foreach (var offer in offers.EnumerateArray())
                            {
                                if (offer.TryGetProperty("inventoryLevel", out var inv) && inv.TryGetInt32(out int invQty))
                                {
                                    totalStock += invQty;
                                }

                                if (!offer.TryGetProperty("prices", out var prices) || prices.ValueKind != JsonValueKind.Array)
                                    continue;

                                foreach (var p in prices.EnumerateArray())
                                {
                                    if (!p.TryGetProperty("price", out var priceVal) || !priceVal.TryGetDecimal(out decimal dec) || dec <= 0)
                                        continue;

                                    int qty = 1;
                                    if (p.TryGetProperty("quantity", out var qVal) && qVal.TryGetInt32(out int parsedQty) && parsedQty > 0)
                                    {
                                        qty = parsedQty;
                                    }

                                    if (p.TryGetProperty("currency", out var currProp) && !string.IsNullOrWhiteSpace(currProp.GetString()))
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
                        HasSymbol: hasSym,
                        HasFootprint: hasFp,
                        Has3DModel: has3D,
                        DatasheetUrl: datasheet,
                        PackageDownloadUrl: null,
                        ProviderColor: ProviderColor
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
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Nexar returned unparseable JSON");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error searching Octopart/Nexar parts");
            }

            return results;
        }
    }
}
