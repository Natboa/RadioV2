using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace RadioV2.Converters;

public class BoolToHeartIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => SymbolRegular.Heart24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
