using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services.Providers
{
    public sealed class EasyEdaProvider : BaseArchiveComponentProvider
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

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
                // EasyEDA / LCSC public component search endpoint
                string url = $"https://easyeda.com/api/components/search?q={Uri.EscapeDataString(query)}&doctype=1";
                using var response = await _httpClient.GetAsync(url, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("result", out var resultObj) &&
                        resultObj.TryGetProperty("lists", out var lists))
                    {
                        foreach (var item in lists.EnumerateArray())
                        {
                            string title = item.TryGetProperty("title", out var t) ? t.GetString() ?? query : query;
                            string desc = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                            string mfg = item.TryGetProperty("manufacturer", out var m) ? m.GetString() ?? "LCSC" : "LCSC";
                            string? pkg = item.TryGetProperty("package", out var p) ? p.GetString() : null;

                            // EasyEDA components typically provide schematic symbols and footprints
                            bool hasSymbol = true;
                            bool hasFootprint = !string.IsNullOrEmpty(pkg);
                            bool has3D = item.TryGetProperty("has_model3d", out var h3d) && h3d.GetInt32() == 1;

                            decimal? price = null;
                            if (item.TryGetProperty("price", out var pr) && pr.TryGetDecimal(out decimal priceVal))
                            {
                                price = priceVal;
                            }

                            int? stock = null;
                            if (item.TryGetProperty("stock", out var st) && st.TryGetInt32(out int stockVal))
                            {
                                stock = stockVal;
                            }

                            string? uuid = item.TryGetProperty("uuid", out var u) ? u.GetString() : null;
                            string? packageUrl = uuid != null ? $"https://easyeda.com/component/{uuid}" : null;

                            results.Add(new PartSearchResult(
                                ProviderId: Id,
                                ProviderName: DisplayName,
                                PartNumber: title,
                                Manufacturer: mfg,
                                Description: desc,
                                BestPrice: price,
                                Currency: "USD",
                                Stock: stock,
                                HasSymbol: hasSymbol,
                                HasFootprint: hasFootprint,
                                Has3DModel: has3D,
                                DatasheetUrl: packageUrl,
                                PackageDownloadUrl: null
                            ));
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Fallback direct link
                results.Add(new PartSearchResult(
                    ProviderId: Id,
                    ProviderName: DisplayName,
                    PartNumber: query.ToUpperInvariant(),
                    Manufacturer: "LCSC / EasyEDA",
                    Description: "Search directly on EasyEDA / LCSC for symbols, footprints, and JLCPCB assembly parts.",
                    BestPrice: null,
                    Currency: "USD",
                    Stock: null,
                    HasSymbol: true,
                    HasFootprint: true,
                    Has3DModel: true,
                    DatasheetUrl: $"https://jlcpcb.com/parts/componentSearch?searchTxt={Uri.EscapeDataString(query)}",
                    PackageDownloadUrl: null
                ));
            }

            if (results.Count == 0)
            {
                results.Add(new PartSearchResult(
                    ProviderId: Id,
                    ProviderName: DisplayName,
                    PartNumber: query.ToUpperInvariant(),
                    Manufacturer: "LCSC / JLCPCB",
                    Description: "Direct part lookup on EasyEDA / JLCPCB catalog.",
                    BestPrice: null,
                    Currency: "USD",
                    Stock: null,
                    HasSymbol: true,
                    HasFootprint: true,
                    Has3DModel: true,
                    DatasheetUrl: $"https://jlcpcb.com/parts/componentSearch?searchTxt={Uri.EscapeDataString(query)}",
                    PackageDownloadUrl: null
                ));
            }

            return results;
        }
    }
}
