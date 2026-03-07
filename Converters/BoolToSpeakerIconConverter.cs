using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace RadioV2.Converters;

public class BoolToSpeakerIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? SymbolRegular.SpeakerMute24 : SymbolRegular.Speaker224;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
