using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace UltraLibrarianImporter.UI.ViewModels
{
    /// <summary>
    /// Converts a boolean value to a color (green for true, red for false)
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isConnected)
            {
                return isConnected ? new SolidColorBrush(Color.Parse("#4CAF50")) : new SolidColorBrush(Color.Parse("#F44336"));
            }
            
            return new SolidColorBrush(Color.Parse("#F44336"));
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
            if (value is bool isConnected)
            {
                return isConnected ? "Connected" : "Disconnected";
            }
            
            return "Disconnected";
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
    /// Converts an ImportType enum value to a human-readable display label.
    /// </summary>
    public class ImportTypeDisplayConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Services.ImportType importType)
            {
                return importType switch
                {
                    Services.ImportType.Symbol => "Import Symbols Only",
                    Services.ImportType.Footprint => "Import Footprints Only",
                    Services.ImportType.Model3D => "Import 3D Models Only",
                    Services.ImportType.All => "Import All Assets",
                    _ => importType.ToString()
                };
            }

            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}