using System.Globalization;
using System.Windows.Data;

namespace RadioV2.Converters;

public class BoolToFeaturedTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "Remove from Featured" : "Add to Featured";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
