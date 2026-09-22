using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.ViewModels;

/// <summary>
/// Converts a boolean value to a color (green for true, red for false)
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool isConnected
            ? isConnected ? new SolidColorBrush(Color.Parse("#4CAF50")) : new SolidColorBrush(Color.Parse("#F44336"))
            : new SolidColorBrush(Color.Parse("#F44336"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a hex color string (e.g. "#E65100") to an Avalonia IBrush.
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return new SolidColorBrush(Color.Parse(hex));
            }
            catch
            {
                // Fall back to default gray
            }
        }

        return new SolidColorBrush(Color.Parse("#555555"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Picks the style classes of a Part Explorer CAD badge (#48). A badge with neither class shows
/// <see cref="CadAvailability.NotAvailable"/>.
/// </summary>
public static class CadAvailabilityConverters
{
    public static readonly IValueConverter IsAvailable =
        new FuncValueConverter<CadAvailability, bool>(availability => availability == CadAvailability.Available);

    public static readonly IValueConverter IsUnknown =
        new FuncValueConverter<CadAvailability, bool>(availability => availability == CadAvailability.Unknown);
}

/// <summary>
/// The words for the About window's KiCad lines (#121): what came of asking KiCad, where the socket
/// address the client dials came from, and whether a token is set. The view model carries the state
/// alone -- <see cref="KiCadQueryState"/>, <see cref="ApiSocketSource"/>, <see cref="ApiTokenState"/> --
/// so it has no sentence to word differently and no token value to put on screen.
/// </summary>
public static class KiCadApiConverters
{
    /// <summary>What the three KiCad lines say while the query is still out.</summary>
    private const string Loading = "Loading ...";

    public static readonly IValueConverter ConnectionStatus =
        new FuncValueConverter<KiCadQueryState, string>(state => state switch
        {
            KiCadQueryState.Answered => "Connected",
            KiCadQueryState.NotConnected => "Not connected to KiCad",
            KiCadQueryState.TimedOut => $"KiCad did not answer within {AboutViewModel.KiCadQueryTimeout.TotalSeconds:0} s",
            KiCadQueryState.AnsweredWithError => "KiCad answered with an error",
            KiCadQueryState.Asking => Loading,
            _ => Loading,
        });

    public static readonly IValueConverter SocketSource =
        new FuncValueConverter<ApiSocketSource, string>(source =>
            source == ApiSocketSource.KiCadEnvironment
                ? "Set by KiCad for this plugin"
                : "KiCad's default path; no socket was named");

    public static readonly IValueConverter TokenState =
        new FuncValueConverter<ApiTokenState, string>(state =>
            state == ApiTokenState.Set
                ? "Set by KiCad for this plugin"
                : "Not set; connecting with an empty token");
}

/// <summary>
/// Converts an ImportType enum value to a human-readable display label.
/// </summary>
public class ImportTypeDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Services.ImportType importType
            ? importType switch
            {
                Services.ImportType.Symbol => "Import Symbols Only",
                Services.ImportType.Footprint => "Import Footprints Only",
                Services.ImportType.Model3D => "Import 3D Models Only",
                Services.ImportType.All => "Import All Assets",
                _ => importType.ToString()
            }
            : value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
