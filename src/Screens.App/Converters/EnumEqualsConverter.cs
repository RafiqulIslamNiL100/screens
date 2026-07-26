using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Screens.App.Converters;

public sealed class EnumEqualsConverter : IValueConverter
{
    public static readonly EnumEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
            return false;
        return value.ToString() == parameter.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
