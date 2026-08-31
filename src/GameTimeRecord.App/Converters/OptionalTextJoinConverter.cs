using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GameTimeRecord.App.Converters;

public sealed class OptionalTextJoinConverter : IMultiValueConverter
{
    public object Convert(
        object[] values,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var separator = parameter as string ?? " · ";
        return string.Join(
            separator,
            values
                .Where(value => value != DependencyProperty.UnsetValue)
                .Select(value => value?.ToString()?.Trim())
                .Where(value => !string.IsNullOrEmpty(value)));
    }

    public object[] ConvertBack(
        object value,
        Type[] targetTypes,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
