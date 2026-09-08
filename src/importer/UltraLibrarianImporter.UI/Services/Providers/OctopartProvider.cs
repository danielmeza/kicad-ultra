using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services.Providers
{
    public sealed class OctopartProvider : BaseArchiveComponentProvider
    {
        private readonly IConfigService _configService;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public OctopartProvider(IConfigService configService)
        {
            _configService = configService;
        }

        public override string Id => "octopart";
        public override string DisplayName => "Octopart (Nexar)";
        public override string SearchUrl => "https://octopart.com/";
        public override string DefaultPrefix => "OCT_";
        public override string DefaultLibraryName => "Octopart";

        public override bool SupportsDirectApi => true;

        public override async Task<IReadOnlyList<PartSearchResult>> SearchPartsAsync(string query, CancellationToken cancellationToken = default)
        {
            var results = new List<PartSearchResult>();

            string token = _configService.OctopartApiToken?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(token))
            {
                // Return a friendly prompt item directing user to configure token
                results.Add(new PartSearchResult(
                    ProviderId: Id,
                    ProviderName: DisplayName,
                    PartNumber: query.ToUpperInvariant(),
                    Manufacturer: "Nexar / Octopart",
                    Description: "API token not set. Configure your Nexar API token in Settings to fetch real-time distributor pricing & stock.",
                    BestPrice: null,
                    Currency: "USD",
                    Stock: null,
                    HasSymbol: false,
                    HasFootprint: false,
                    Has3DModel: false,
                    DatasheetUrl: $"https://octopart.com/search?q={Uri.EscapeDataString(query)}",
                    PackageDownloadUrl: null
                ));
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
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("data", out var data) &&
                        data.TryGetProperty("supSearch", out var supSearch) &&
                        supSearch.TryGetProperty("results", out var resultsArray))
                    {
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
                            decimal? lowestPrice = null;

                            if (part.TryGetProperty("sellers", out var sellers))
                            {
                                foreach (var seller in sellers.EnumerateArray())
                                {
                                    if (seller.TryGetProperty("offers", out var offers))
                                    {
                                        foreach (var offer in offers.EnumerateArray())
                                        {
                                            if (offer.TryGetProperty("inventoryLevel", out var inv) && inv.TryGetInt32(out int invQty))
                                            {
                                                totalStock += invQty;
                                            }

                                            if (offer.TryGetProperty("prices", out var prices))
                                            {
                                                foreach (var p in prices.EnumerateArray())
                                                {
                                                    if (p.TryGetProperty("price", out var priceVal) && priceVal.TryGetDecimal(out decimal dec))
                                                    {
                                                        if (!lowestPrice.HasValue || dec < lowestPrice.Value)
                                                        {
                                                            lowestPrice = dec;
                                                        }
                                                    }
                                                }
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
                                BestPrice: lowestPrice,
                                Currency: "USD",
                                Stock: totalStock > 0 ? totalStock : null,
                                HasSymbol: hasSym,
                                HasFootprint: hasFp,
                                Has3DModel: has3D,
                                DatasheetUrl: datasheet,
                                PackageDownloadUrl: null
                            ));
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Fallback to web search item on network or authentication error
                results.Add(new PartSearchResult(
                    ProviderId: Id,
                    ProviderName: DisplayName,
                    PartNumber: query.ToUpperInvariant(),
                    Manufacturer: "Octopart / Nexar",
                    Description: "Direct search link (verify Nexar token if API was expected).",
                    BestPrice: null,
                    Currency: "USD",
                    Stock: null,
                    HasSymbol: false,
                    HasFootprint: false,
                    Has3DModel: false,
                    DatasheetUrl: $"https://octopart.com/search?q={Uri.EscapeDataString(query)}",
                    PackageDownloadUrl: null
                ));
            }

            return results;
        }
    }
}
