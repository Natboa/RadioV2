using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace RadioV2.Converters;

public class BoolToStarIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? SymbolRegular.Star24 : SymbolRegular.StarAdd24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
