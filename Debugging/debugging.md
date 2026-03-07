
*keep minimize option so it runs at the background but as a seperate button not when clicking x
FIXED (v2): Native minimize (_) minimizes to taskbar (default). Custom button in TitleBar.TrailingContent hides to tray (Hide + ShowInTaskbar=false). No StateChanged interception — direct and unambiguous. Alignment fixed with Padding=0 + VerticalAlignment=Stretch. (MainWindow.xaml, MainWindow.xaml.cs — see minimize-tray-button-fix.md)

*scrolling up and down is not working when mouse is on the groups in the discovery page, when it is on the sides the scrolling works
FIXED: ui:Card children were consuming mouse wheel events. Added PreviewMouseWheel handler on GroupsScrollViewer that manually scrolls and marks the event handled, so scroll works regardless of where the mouse is. (DiscoverPage.xaml.cs)

*stations with pictures will be at the top
FIXED: Changed ORDER BY in GetStationsByGroupAsync to OrderByDescending(s => s.LogoUrl != null && s.LogoUrl != "").ThenBy(s => s.Name) — stations with a logo URL sort first. (StationService.cs)

*groups are loading everytime you go to the discovery page instead of being saved somewhere, also start loading the first 30 groups when the app is running, so when the user clicks on discovery they will already be loaded
FIXED: Changed DiscoverViewModel from Transient → Singleton so groups data persists across page navigations. Changed StationService from Scoped → Singleton using IDbContextFactory<RadioDbContext> (each operation creates its own short-lived DbContext). Groups preloaded in App.OnStartup before the window shows. (App.xaml.cs, StationService.cs)

*add a button for adding to favourite, it will be a heart, if clicked the heart will turn red and will show up in favourites, what now is showing a j letter to the right of the play station button. change the symbol
FIXED: Three-stage fix —
  1. HeartOff24 → Heart24 (HeartOff24 glyph renders as 'j' in Segoe Fluent Icons; WPF-UI 4.2.0 has no HeartFilled24 either).
  2. Added BoolToHeartColorConverter: returns Brushes.Red when favourite, DependencyProperty.UnsetValue otherwise (returning null made the icon invisible).
  3. Bound ui:SymbolIcon.Foreground to IsFavorite via BoolToHeartColorConverter so heart turns red when favourited.
  (Converters/BoolToHeartIconConverter.cs, Converters/BoolToHeartColorConverter.cs, Controls/StationListItem.xaml, App.xaml)

*add puctures to groups, ask the user how we should implement this so its easy to change pictures and upload them somewhere

*when inside the favorite, add button to remove a song from favourite

*in the audio bar at the button, add another symbol when its muted, to be a speaker with a line over it 

*to the right of the speaker bar, theres a hear button that does nothing. remove it.
FIXED: Removed the non-functional heart/favourite button from MiniPlayer.xaml (ToggleFavouriteCommand was never implemented in MiniPlayerViewModel). (Controls/MiniPlayer.xaml)

*where it says the current artist and song name, its sometimes long,  make it longer before cutting to ... and also when the mouse hovers on the song or the artist it will show the full name, it will be looking nice and according to the theme, eather dark or light mode

*theres a button that minimizes and exmands the left side, when its not minimized,the button will go to the right side of the bar and the symbols will be to arrows poiting outside or inside diagonal

*when clicking on pause, the button at the button will turn to the pause button, a triangle. it will have a smooth transition animation when clicked

*when the user clicks on his keyboard the next or previous button, it will iterate through the stations, if its a station in a group then iterate the group stations, if in favorite then iterate favorites.

*browse page, when clicking the browse it will show prevous searches.

*in the browse page it will say recent sttations and it will show stations you clicked on while searching or while in the discovery. it will show up to the last 15 stations.

*in light mode, some text that is color of grey is too light grey and hard to read, make darker

*future features:
 installer that includes the db aswell.

 *the app will check everyday if a db online has changed, if so it will update the local db

 *crud for developer for stations and groups (seperate from the package and installer, its just for developement)
 the crud will also include merging two groups



