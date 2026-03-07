using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RadioV2.Converters;

public class BoolToHeartColorConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? System.Windows.Media.Brushes.Red : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
