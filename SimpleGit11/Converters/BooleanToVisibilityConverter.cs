using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace SimpleGit11.Converters;

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    private const string InvertParameter = "Invert";

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not bool boolValue)
        {
            return Visibility.Collapsed;
        }

        bool invert = parameter is string parameterValue
            && string.Equals(parameterValue, InvertParameter, StringComparison.OrdinalIgnoreCase);

        return boolValue != invert
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
