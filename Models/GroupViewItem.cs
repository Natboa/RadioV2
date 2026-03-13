namespace RadioV2.Models;

public abstract class GroupViewItem { }

/// <summary>Marker rendered as the "Featured" label above featured stations.</summary>
public sealed class FeaturedHeaderItem : GroupViewItem { }

/// <summary>Marker rendered as a separator between the featured and regular station sections.</summary>
public sealed class SectionSeparatorItem : GroupViewItem { }

/// <summary>Wraps a <see cref="Station"/> for display in the combined AllStationItems list.</summary>
public sealed class StationGroupViewItem : GroupViewItem
{
    public Station Station { get; }
    public StationGroupViewItem(Station station) => Station = station;
}
