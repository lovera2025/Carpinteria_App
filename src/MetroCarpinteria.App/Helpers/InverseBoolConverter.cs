using System.Globalization;
using System.Windows.Data;

namespace MetroCarpinteria.App.Helpers;

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool boolean ? !boolean : false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool boolean ? !boolean : false;
    }
}
