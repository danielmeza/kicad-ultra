using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace UltraLibrarianImporter.UI.Converters;

/// <summary>
/// Converts a boolean value to an opacity value (1.0 for true, 0.4 for false)
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    /// <summary>
    /// Converts a boolean to an opacity value
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool boolValue ? boolValue ? 1.0 : 0.4 : 1.0;
    }

    /// <summary>
    /// Not implemented
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
