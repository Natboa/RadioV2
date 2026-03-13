using RadioV2.Models;
using System.Windows;
using System.Windows.Controls;

namespace RadioV2.Helpers;

/// <summary>
/// Returns the normal station style for <see cref="StationGroupViewItem"/> entries and a
/// chrome-free style (no hover highlight, not focusable) for header/separator markers.
/// </summary>
public class GroupViewItemStyleSelector : StyleSelector
{
    public Style? StationStyle { get; set; }
    public Style? MarkerStyle { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container) =>
        item is StationGroupViewItem ? StationStyle : MarkerStyle;
}
