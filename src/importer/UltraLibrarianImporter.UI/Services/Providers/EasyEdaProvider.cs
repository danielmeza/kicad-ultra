using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services.Providers
{
    public sealed class EasyEdaProvider : BaseArchiveComponentProvider
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private readonly ILogger<EasyEdaProvider> _logger;

        static EasyEdaProvider()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("kicad-ultra/1.0 (KiCad Component Importer; +https://github.com/danielmeza/kicad-ultra)");
        }

        public EasyEdaProvider(ILogger<EasyEdaProvider> logger)
        {
            _logger = logger;
        }

        public override string Id => "easyeda";
        public override string DisplayName => "EasyEDA / LCSC";
        public override string SearchUrl => "https://easyeda.com/";
        public override string DefaultPrefix => "EEDA_";
        public override string DefaultLibraryName => "EasyEDA";
        public override string ProviderColor => "#2E7D32";

        public override bool SupportsDirectApi => true;

        public override async Task<IReadOnlyList<PartSearchResult>> SearchPartsAsync(string query, CancellationToken cancellationToken = default)
        {
            var results = new List<PartSearchResult>();
            if (string.IsNullOrWhiteSpace(query))
            {
                return results;
            }

            try
            {
                // JLCPCB / LCSC in-stock parts API (tscircuit jlcsearch index)
                string url = $"https://jlcsearch.tscircuit.com/components/list.json?search={Uri.EscapeDataString(query)}&limit=25";
                using var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("EasyEDA/JLCPCB search returned HTTP {StatusCode}", response.StatusCode);
                    return results;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                JsonElement componentsArray;
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    componentsArray = doc.RootElement;
                }
                else if (doc.RootElement.TryGetProperty("components", out var comps) && comps.ValueKind == JsonValueKind.Array)
                {
                    componentsArray = comps;
                }
                else
                {
                    return results;
                }

                if (componentsArray.ValueKind != JsonValueKind.Array)
                {
                    return results;
                }

                foreach (var item in componentsArray.EnumerateArray().Take(25))
                {
                    string mpn = item.TryGetProperty("mfr", out var m) ? m.GetString() ?? query : query;
                    string desc = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                    string? pkg = item.TryGetProperty("package", out var p) ? p.GetString() : null;
                    string? subcat = item.TryGetProperty("subcategory", out var sc) ? sc.GetString() : null;
                    string? cat = item.TryGetProperty("category", out var c) ? c.GetString() : null;

                    // Manufacturer field is not present in the third-party jlcsearch index.
                    // Leave blank rather than rendering category as manufacturer.
                    string mfg = string.Empty;

                    // Enrich description with category/subcategory if available
                    string categoryTag = !string.IsNullOrWhiteSpace(subcat) ? subcat : (!string.IsNullOrWhiteSpace(cat) ? cat : string.Empty);
                    if (!string.IsNullOrWhiteSpace(categoryTag) && !desc.Contains(categoryTag, StringComparison.OrdinalIgnoreCase))
                    {
                        desc = string.IsNullOrWhiteSpace(desc) ? categoryTag : $"{desc} ({categoryTag})";
                    }
                    if (!string.IsNullOrEmpty(pkg) && !desc.Contains(pkg, StringComparison.OrdinalIgnoreCase))
                    {
                        desc = $"{desc} [{pkg}]";
                    }

                    // Stock count
                    int? stock = null;
                    if (item.TryGetProperty("stock", out var st) && st.TryGetInt32(out int stockVal))
                    {
                        stock = stockVal;
                    }

                    // Price tiers
                    string? priceStr = item.TryGetProperty("price", out var pr) ? pr.GetString() : null;
                    decimal? bestPrice = ParseBestPrice(priceStr);

                    // LCSC code for direct component page
                    long lcscCode = 0;
                    if (item.TryGetProperty("lcsc", out var lc))
                    {
                        if (lc.ValueKind == JsonValueKind.Number)
                        {
                            lc.TryGetInt64(out lcscCode);
                        }
                        else if (lc.ValueKind == JsonValueKind.String && long.TryParse(lc.GetString(), out long parsedCode))
                        {
                            lcscCode = parsedCode;
                        }
                    }

                    string datasheetUrl = lcscCode > 0
                        ? $"https://jlcpcb.com/parts/componentSearch?searchTxt=C{lcscCode}"
                        : $"https://jlcpcb.com/parts/componentSearch?searchTxt={Uri.EscapeDataString(mpn)}";

                    results.Add(new PartSearchResult(
                        ProviderId: Id,
                        ProviderName: DisplayName,
                        PartNumber: mpn,
                        Manufacturer: mfg,
                        Description: desc,
                        BestPrice: bestPrice,
                        Currency: "USD",
                        Stock: stock,
                        HasSymbol: false,
                        HasFootprint: false,
                        Has3DModel: false,
                        DatasheetUrl: datasheetUrl,
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
                _logger.LogWarning(ex, "HTTP request failed while querying EasyEDA/JLCPCB endpoint");
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse JSON response from EasyEDA/JLCPCB endpoint");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error searching EasyEDA parts");
            }

            return results;
        }

        private static decimal? ParseBestPrice(string? priceStr)
        {
            if (string.IsNullOrWhiteSpace(priceStr))
            {
                return null;
            }

            decimal? best = null;
            var tiers = priceStr.Split(',');
            foreach (var tier in tiers)
            {
                var colonIdx = tier.IndexOf(':');
                string valStr = colonIdx >= 0 ? tier.Substring(colonIdx + 1).Trim() : tier.Trim();
                if (decimal.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price) && price > 0)
                {
                    if (!best.HasValue || price < best.Value)
                    {
                        best = price;
                    }
                }
            }

            return best.HasValue ? Math.Round(best.Value, 3) : null;
        }
    }
}
