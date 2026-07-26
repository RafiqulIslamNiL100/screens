using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Screens.App.Converters;

/// <summary>True (zoom to fit) -> Uniform. False (100%) -> None, so the image renders at native pixel size.</summary>
public sealed class BoolToStretchConverter : IValueConverter
{
    public static readonly BoolToStretchConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Stretch.Uniform : Stretch.None;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
