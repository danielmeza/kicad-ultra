using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services.Providers
{
    public sealed class EasyEdaProvider : BaseArchiveComponentProvider
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        static EasyEdaProvider()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        public override string Id => "easyeda";
        public override string DisplayName => "EasyEDA / LCSC";
        public override string SearchUrl => "https://easyeda.com/";
        public override string DefaultPrefix => "EEDA_";
        public override string DefaultLibraryName => "EasyEDA";

        public override bool SupportsDirectApi => true;

        public override async Task<IReadOnlyList<PartSearchResult>> SearchPartsAsync(string query, CancellationToken cancellationToken = default)
        {
            var results = new List<PartSearchResult>();

            try
            {
                // JLCPCB / LCSC in-stock parts API (tscircuit jlcsearch index)
                string url = $"https://jlcsearch.tscircuit.com/components/list.json?search={Uri.EscapeDataString(query)}&limit=25";
                using var response = await _httpClient.GetAsync(url, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
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
                        componentsArray = default;
                    }

                    if (componentsArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in componentsArray.EnumerateArray())
                        {
                            string mpn = item.TryGetProperty("mfr", out var m) ? m.GetString() ?? query : query;
                            string desc = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                            string? pkg = item.TryGetProperty("package", out var p) ? p.GetString() : null;
                            string? subcat = item.TryGetProperty("subcategory", out var sc) ? sc.GetString() : null;
                            string? cat = item.TryGetProperty("category", out var c) ? c.GetString() : null;

                            // Determine manufacturer or category label
                            string mfg = !string.IsNullOrWhiteSpace(subcat) 
                                ? subcat 
                                : (!string.IsNullOrWhiteSpace(cat) ? cat : "LCSC / JLCPCB");

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
                                Description: !string.IsNullOrEmpty(pkg) && !desc.Contains(pkg) ? $"{desc} [{pkg}]" : desc,
                                BestPrice: bestPrice,
                                Currency: "USD",
                                Stock: stock,
                                HasSymbol: true,
                                HasFootprint: !string.IsNullOrEmpty(pkg) && pkg != "-",
                                Has3DModel: true,
                                DatasheetUrl: datasheetUrl,
                                PackageDownloadUrl: null
                            ));
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Network or API failure fallback
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
