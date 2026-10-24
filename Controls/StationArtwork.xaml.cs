using System.Windows;
using RadioV2.Helpers;

namespace RadioV2.Controls;

public partial class StationArtwork : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(string), typeof(StationArtwork));
    public static readonly DependencyProperty StationNameProperty = DependencyProperty.Register(
        nameof(StationName), typeof(string), typeof(StationArtwork),
        new PropertyMetadata("", (d, _) => ((StationArtwork)d).SetValue(InitialsPropertyKey, StationInitials.For(((StationArtwork)d).StationName))));
    private static readonly DependencyPropertyKey InitialsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Initials), typeof(string), typeof(StationArtwork), new PropertyMetadata("?"));
    public static readonly DependencyProperty InitialsProperty = InitialsPropertyKey.DependencyProperty;

    public string? Source { get => (string?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public string StationName { get => (string)GetValue(StationNameProperty); set => SetValue(StationNameProperty, value); }
    public string Initials => (string)GetValue(InitialsProperty);
    public StationArtwork() => InitializeComponent();
}
