using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace WindowSwitcher.Windows.Converters;

/// <summary>
/// Returns <see langword="true"/> when the first two bound values are equal, non-empty
/// strings (ordinal comparison).
/// </summary>
internal sealed class StringEqualsMultiConverter : IMultiValueConverter
{
    public static readonly StringEqualsMultiConverter Instance = new();

    public object Convert(
        IList<object?> values,
        Type targetType,
        object? parameter,
        CultureInfo culture
    )
    {
        return values.Count >= 2
            && values[0] is string first
            && values[1] is string second
            && first.Length > 0
            && string.Equals(first, second, StringComparison.Ordinal);
    }
}
