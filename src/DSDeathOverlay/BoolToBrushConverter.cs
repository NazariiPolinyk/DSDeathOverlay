using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DSDeathOverlay;

/// <summary>
/// Converts a bool to one of two brushes. Used by MainWindow to make the overlay
/// visually distinct when it is in "edit mode" (i.e. drag-to-move enabled).
/// </summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush? FalseBrush { get; set; }
    public Brush? TrueBrush { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueBrush : FalseBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
