using System.Globalization;
using System.Windows.Data;

namespace RadioV2.DevTool.Converters;

public class BoolToModeConverter : IValueConverter
{
    // Pass ConverterParameter="Group" to get "Edit Group"/"New Group". Default: "Station".
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string entity = parameter as string ?? "Station";
        return value is true ? $"Edit {entity}" : $"New {entity}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
