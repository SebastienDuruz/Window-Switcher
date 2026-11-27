using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace WindowSwitcher.Windows.Converters;

/// <summary>
/// Converts a color string such as "#E3008C" to a solid brush, for previews of the
/// highlight color.
/// </summary>
internal sealed class ColorStringToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text && Color.TryParse(text, out Color color))
            return new SolidColorBrush(color);

        return Brushes.Magenta;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    )
    {
        return BindingOperations.DoNothing;
    }
}
