using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DSDeathOverlay;

/// <summary>
/// Returns <see cref="FontWeights.Bold"/> when the bound boolean is true,
/// otherwise <see cref="FontWeights.Normal"/>. Used to highlight the active
/// boss in the expanded list.
/// </summary>
public sealed class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? FontWeights.Bold : FontWeights.Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
