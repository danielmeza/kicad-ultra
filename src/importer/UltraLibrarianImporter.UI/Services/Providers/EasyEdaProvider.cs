using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using UltraLibrarianImporter.UI.Services.EasyEda2KiCad;
using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.Services.Providers.Jlcpcb;

namespace UltraLibrarianImporter.UI.Services.Providers;

/// <summary>
/// EasyEDA / LCSC parts: found in JLCPCB's parts library, imported with the user-installed
/// easyeda2kicad (#76).
/// </summary>
/// <remarks>
/// Each search picks one of two JLCPCB sources (#51, #52):
/// <list type="bullet">
/// <item>JLCPCB's official Components API (<see cref="JlcpcbOpenApiClient"/>), when the user has
/// entered their own API credentials and the query is an LCSC part number. The API cannot search
/// by keyword.</item>
/// <item>Otherwise the unofficial endpoint behind JLCPCB's parts search page
/// (<see cref="JlcpcbWebsiteSearchClient"/>). It can break without notice; the Part Explorer says
/// so while it is in use, and every result it produced says so in its Attribution.</item>
/// </list>
/// A failure of either source throws. A failed official lookup never falls back to the unofficial
/// endpoint, so a problem with the user's credentials cannot hide behind other data.
/// </remarks>
public sealed class EasyEdaProvider : BaseArchiveComponentProvider
{
    public const string ProviderId = "easyeda";

    /// <summary>The Attribution of results from the official Components API.</summary>
    public const string OfficialApiAttribution = "Data from JLCPCB's official Components API";

    /// <summary>The Attribution of results from the unofficial endpoint: what it is, and that it can break.</summary>
    public const string UnofficialEndpointAttribution =
        "Unofficial source: an internal JLCPCB website endpoint, not a published API. It can change or stop working at any time without notice.";

    // Neither source names a currency. JLCPCB's international site charges in US dollars.
    private const string PriceCurrency = "USD";

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly IConfigService _configService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EasyEdaProvider> _logger;

    static EasyEdaProvider()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("kicad-ultra/1.0 (KiCad Component Importer; +https://github.com/danielmeza/kicad-ultra)");
    }

    public EasyEdaProvider(IConfigService configService, TimeProvider timeProvider, ILogger<EasyEdaProvider> logger)
    {
        _configService = configService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public override string Id => ProviderId;
    public override string DisplayName => "EasyEDA / LCSC";
    public override string SearchUrl => "https://easyeda.com/";
    public override string DefaultPrefix => "EEDA_";
    public override string DefaultLibraryName => "EasyEDA";
    public override string ProviderColor => "#2E7D32";

    public override bool SupportsDirectApi => true;

    public override async Task<IReadOnlyList<PartSearchResult>> SearchPartsAsync(string query, CancellationToken cancellationToken = default)
    {
        var keyword = query.Trim();
        if (keyword.Length == 0)
        {
            return [];
        }

        var credentials = JlcpcbApiCredentials.FromConfig(_configService);
        var lcscPartNumber = keyword.ToUpperInvariant();
        if (credentials is not null && EasyEda2KiCadConverter.IsLcscPartNumber(lcscPartNumber))
        {
            return await LookUpWithOfficialApiAsync(credentials, lcscPartNumber, cancellationToken);
        }

        if (credentials is null && JlcpcbApiCredentials.GetState(_configService) == JlcpcbApiCredentialState.Incomplete)
        {
            _logger.LogWarning("JLCPCB API credentials are incomplete (missing {Missing}); searching the unofficial JLCPCB endpoint instead",
                string.Join(", ", JlcpcbApiCredentials.GetMissing(_configService)));
        }

        return await SearchWebsiteAsync(keyword, cancellationToken);
    }

    private async Task<IReadOnlyList<PartSearchResult>> LookUpWithOfficialApiAsync(
        JlcpcbApiCredentials credentials, string lcscPartNumber, CancellationToken cancellationToken)
    {
        IReadOnlyList<JlcpcbPart> parts;
        try
        {
            parts = await JlcpcbOpenApiClient.GetComponentDetailAsync(_httpClient, credentials, lcscPartNumber, _timeProvider, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "JLCPCB Components API request for {LcscPartNumber} failed", lcscPartNumber);
            throw;
        }
        catch (JlcpcbApiException ex)
        {
            _logger.LogWarning("JLCPCB Components API refused the lookup of {LcscPartNumber}: {Error}", lcscPartNumber, ex.Message);
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JLCPCB Components API returned an unexpected response for {LcscPartNumber}", lcscPartNumber);
            throw;
        }

        _logger.LogDebug("JLCPCB Components API: {Count} part(s) for {LcscPartNumber}", parts.Count, lcscPartNumber);
        return ToResults(parts, OfficialApiAttribution);
    }

    private async Task<IReadOnlyList<PartSearchResult>> SearchWebsiteAsync(string keyword, CancellationToken cancellationToken)
    {
        IReadOnlyList<JlcpcbPart> parts;
        try
        {
            parts = await JlcpcbWebsiteSearchClient.SearchAsync(_httpClient, keyword, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Unofficial JLCPCB search request for '{Keyword}' failed", keyword);
            throw;
        }
        catch (JlcpcbApiException ex)
        {
            _logger.LogWarning("Unofficial JLCPCB search for '{Keyword}' returned an error: {Error}", keyword, ex.Message);
            throw;
        }
        catch (JsonException ex)
        {
            // The endpoint is undocumented, so a change of shape is the expected way for it to break.
            _logger.LogWarning(ex, "Unofficial JLCPCB search for '{Keyword}' returned an unexpected response", keyword);
            throw;
        }

        return ToResults(parts, UnofficialEndpointAttribution);
    }

    private List<PartSearchResult> ToResults(IReadOnlyList<JlcpcbPart> parts, string attribution) =>
        parts.Select(part => new PartSearchResult(
            ProviderId: Id,
            ProviderName: DisplayName,
            PartNumber: part.ManufacturerPartNumber ?? string.Empty,
            Manufacturer: part.Manufacturer ?? string.Empty,
            Description: Describe(part),
            // The price for the smallest order, as Octopart's results show it, rather than the
            // cheapest break, which only applies to thousands of pieces.
            BestPrice: part.PriceBreaks.Count > 0 ? part.PriceBreaks[0].UnitPrice : null,
            Currency: part.PriceBreaks.Count > 0 ? PriceCurrency : null,
            Stock: part.Stock,
            // Neither source says whether EasyEDA has a symbol, footprint or 3D model for the part.
            HasSymbol: false,
            HasFootprint: false,
            Has3DModel: false,
            DatasheetUrl: part.DatasheetUrl,
            ProviderColor: ProviderColor,
            Attribution: attribution,
            // As the source reported it, and nothing when it reported none: this is what the
            // easyeda2kicad import converts (#76), so it is never derived from the MPN.
            LcscPartNumber: part.LcscPartNumber,
            PriceBreaks: part.PriceBreaks.Count > 0 ? part.PriceBreaks : null
        )).ToList();

    // The description as the source wrote it, with the package appended when it does not already
    // mention it. JLCPCB writes "-" for a part without one.
    private static string Describe(JlcpcbPart part)
    {
        var description = part.Description ?? string.Empty;
        if (part.Package is { } package && package != "-" && !description.Contains(package, StringComparison.OrdinalIgnoreCase))
        {
            description = description.Length == 0 ? $"[{package}]" : $"{description} [{package}]";
        }

        return description;
    }
}
