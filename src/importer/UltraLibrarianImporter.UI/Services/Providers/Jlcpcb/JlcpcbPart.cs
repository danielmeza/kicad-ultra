using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

using UltraLibrarianImporter.UI.Services.EasyEda2KiCad;
using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services.Providers.Jlcpcb;

/// <summary>
/// One part as a JLCPCB source described it. Every value is what the source returned, and null (or
/// an empty list) when it returned nothing: nothing is filled in from the query or guessed.
/// </summary>
/// <param name="LcscPartNumber">The LCSC code, only when it is one (<c>C</c> and digits).</param>
/// <param name="ManufacturerPartNumber">The manufacturer's part number.</param>
/// <param name="Manufacturer">The manufacturer's name.</param>
/// <param name="Description">The part's description.</param>
/// <param name="Package">The package, which JLCPCB writes as <c>-</c> for a part without one.</param>
/// <param name="Stock">JLCPCB's stock of the part.</param>
/// <param name="PriceBreaks">Lowest quantity first; only breaks with a positive quantity and price.</param>
/// <param name="DatasheetUrl">The datasheet's URL.</param>
public sealed record JlcpcbPart(
    string? LcscPartNumber,
    string? ManufacturerPartNumber,
    string? Manufacturer,
    string? Description,
    string? Package,
    int? Stock,
    IReadOnlyList<PriceBreak> PriceBreaks,
    string? DatasheetUrl);

/// <summary>
/// Readers for the JSON of both JLCPCB sources. A field that is missing, null, empty or of an
/// unexpected type reads as null, never as a default that could pass for data.
/// </summary>
internal static class JlcpcbJson
{
    public static string? GetString(JsonElement item, string name) =>
        TryGetProperty(item, name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? NullIfBlank(value.GetString())
            : null;

    /// <summary>The first of <paramref name="names"/> that holds a non-empty string.</summary>
    public static string? GetFirstString(JsonElement item, params string[] names) =>
        names.Select(name => GetString(item, name)).FirstOrDefault(value => value is not null);

    /// <summary>An integer sent as a JSON number or as a numeric string.</summary>
    public static int? GetInt32(JsonElement item, string name) =>
        GetNumberText(item, name) is { } text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    /// <summary>A decimal sent as a JSON number or as a numeric string.</summary>
    public static decimal? GetDecimal(JsonElement item, string name) =>
        GetNumberText(item, name) is { } text && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    /// <summary>
    /// The field's value when it is an LCSC part number, which is what the easyeda2kicad import
    /// converts (#76); null for anything else, so a malformed code never reaches that import.
    /// </summary>
    public static string? GetLcscPartNumber(JsonElement item, string name) =>
        GetString(item, name) is { } code && EasyEda2KiCadConverter.IsLcscPartNumber(code) ? code : null;

    /// <summary>
    /// The price ladder in the array <paramref name="arrayName"/>, lowest quantity first. A break
    /// without a positive quantity and a positive unit price is left out rather than guessed at.
    /// </summary>
    public static IReadOnlyList<PriceBreak> GetPriceBreaks(JsonElement item, string arrayName, string quantityName, string priceName)
    {
        if (!TryGetProperty(item, arrayName, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var breaks = new List<PriceBreak>();
        foreach (JsonElement entry in array.EnumerateArray())
        {
            if (GetInt32(entry, quantityName) is int quantity && quantity > 0 &&
                GetDecimal(entry, priceName) is decimal price && price > 0m)
            {
                breaks.Add(new PriceBreak(quantity, price));
            }
        }

        return [.. breaks.OrderBy(b => b.Quantity)];
    }

    /// <summary>The <c>message</c> of an error body, or null when the body is not JSON or has none.</summary>
    public static string? TryGetMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return GetString(doc.RootElement, "message");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A number's JSON text, or a string's value: some numbers arrive as strings.
    private static string? GetNumberText(JsonElement item, string name) =>
        !TryGetProperty(item, name, out JsonElement value) ? null
        : value.ValueKind == JsonValueKind.Number ? value.GetRawText()
        : value.ValueKind == JsonValueKind.String ? value.GetString()
        : null;

    private static bool TryGetProperty(JsonElement item, string name, out JsonElement value)
    {
        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
