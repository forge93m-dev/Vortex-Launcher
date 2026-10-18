using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace VortexLauncher.Converters;

public sealed class IndexEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int selected && int.TryParse(parameter?.ToString(), out int index) && selected == index;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
