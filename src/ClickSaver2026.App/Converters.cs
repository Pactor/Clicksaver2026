using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClickSaver2026.App;

public sealed class NullToCollapsed : IValueConverter
{
    public static NullToCollapsed Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
