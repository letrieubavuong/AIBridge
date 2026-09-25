using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AIBridge.Infrastructure;

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue = value is bool b && b;
        bool inverted = !boolValue;

        if (targetType == typeof(Visibility) || targetType == typeof(Visibility?))
        {
            return inverted ? Visibility.Visible : Visibility.Collapsed;
        }

        return inverted;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility != Visibility.Visible;
        }
        if (value is bool b)
        {
            return !b;
        }
        return false;
    }
}
