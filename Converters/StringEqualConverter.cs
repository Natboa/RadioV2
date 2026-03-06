using System.Globalization;
using System.Windows.Data;

namespace RadioV2.Converters;

/// <summary>
/// Returns true when the bound string equals ConverterParameter. Used for theme RadioButtons.
/// Setting: converts true → set SelectedTheme to ConverterParameter.
/// </summary>
public class StringEqualConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && parameter is string p && s == p;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter : System.Windows.Data.Binding.DoNothing;
}
