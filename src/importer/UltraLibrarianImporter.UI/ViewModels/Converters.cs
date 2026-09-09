using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

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
/// Converts a boolean value to a status string (Connected/Disconnected)
/// </summary>
public class BoolToStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool isConnected ? isConnected ? "Connected" : "Disconnected" : "Disconnected";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
