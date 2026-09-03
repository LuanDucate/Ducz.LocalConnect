using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Ducz.LocalConnect.App;

public sealed class IntToNullableDoubleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int number ? (double?)number : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double number ? (int)Math.Round(number) : DependencyProperty.UnsetValue;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool flag ? !flag : DependencyProperty.UnsetValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool flag ? !flag : DependencyProperty.UnsetValue;
}

/// <summary>True → Visible, false → Collapsed. Pass <c>Invert</c> as the parameter to flip it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter is string text && string.Equals(text, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasContent = value switch
        {
            null => false,
            string text => !string.IsNullOrEmpty(text),
            int count => count > 0,
            System.Collections.ICollection collection => collection.Count > 0,
            _ => true,
        };

        if (parameter is string text2 && string.Equals(text2, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            hasContent = !hasContent;
        }

        return hasContent ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
