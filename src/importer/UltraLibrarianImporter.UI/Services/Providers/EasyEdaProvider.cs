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

namespace UltraLibrarianImporter.UI.Services.Providers;

public sealed class EasyEdaProvider : BaseArchiveComponentProvider
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    // Search goes through tscircuit's independent index of JLCPCB's parts list (#52), not through
    // JLCPCB or LCSC, so every result says so rather than implying either served it (#58).
    private const string DataAttribution =
        "Data from jlcsearch.tscircuit.com, a third-party index of JLCPCB parts, not from JLCPCB or LCSC directly";

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
            var url = $"https://jlcsearch.tscircuit.com/components/list.json?search={Uri.EscapeDataString(query)}&limit=25";
            using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
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
            else if (doc.RootElement.TryGetProperty("components", out JsonElement comps) && comps.ValueKind == JsonValueKind.Array)
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

            foreach (JsonElement item in componentsArray.EnumerateArray().Take(25))
            {
                var mpn = item.TryGetProperty("mfr", out JsonElement m) ? m.GetString() ?? query : query;
                var desc = item.TryGetProperty("description", out JsonElement d) ? d.GetString() ?? "" : "";
                var pkg = item.TryGetProperty("package", out JsonElement p) ? p.GetString() : null;
                var subcat = item.TryGetProperty("subcategory", out JsonElement sc) ? sc.GetString() : null;
                var cat = item.TryGetProperty("category", out JsonElement c) ? c.GetString() : null;

                // Manufacturer field is not present in the third-party jlcsearch index.
                // Leave blank rather than rendering category as manufacturer.
                var mfg = string.Empty;

                // Enrich description with category/subcategory if available
                var categoryTag = !string.IsNullOrWhiteSpace(subcat) ? subcat : (!string.IsNullOrWhiteSpace(cat) ? cat : string.Empty);
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
                if (item.TryGetProperty("stock", out JsonElement st) && st.TryGetInt32(out var stockVal))
                {
                    stock = stockVal;
                }

                // Price tiers
                var priceStr = item.TryGetProperty("price", out JsonElement pr) ? pr.GetString() : null;
                var bestPrice = ParseBestPrice(priceStr);

                // LCSC code for direct component page
                long lcscCode = 0;
                if (item.TryGetProperty("lcsc", out JsonElement lc))
                {
                    if (lc.ValueKind == JsonValueKind.Number)
                    {
                        _ = lc.TryGetInt64(out lcscCode);
                    }
                    else if (lc.ValueKind == JsonValueKind.String && long.TryParse(lc.GetString(), out var parsedCode))
                    {
                        lcscCode = parsedCode;
                    }
                }

                var datasheetUrl = lcscCode > 0
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
                    ProviderColor: ProviderColor,
                    Attribution: DataAttribution
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
            var valStr = colonIdx >= 0 ? tier[(colonIdx + 1)..].Trim() : tier.Trim();
            if (decimal.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) && price > 0)
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
