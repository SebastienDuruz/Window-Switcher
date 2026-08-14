using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace WindowSwitcher.Windows.Converters;

internal sealed class ColorStringValueConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text && Color.TryParse(text, out Color color))
            return color;

        return Colors.Magenta;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    )
    {
        if (value is not Color color)
            return "#E3008C";

        return color.A == 0xFF
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
