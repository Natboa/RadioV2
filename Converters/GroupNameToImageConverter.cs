using RadioV2.Helpers;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace RadioV2.Converters;

public class GroupNameToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string name ? GroupImageHelper.GetImage(name) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
