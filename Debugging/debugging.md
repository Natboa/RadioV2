
*keep minimize option so it runs at the background but as a seperate button not when clicking x
FIXED: The native window minimize button (_) now hides the app to tray instead of minimizing to taskbar. X still quits. Double-click tray icon or tray → "Show Window" to restore. (MainWindow.xaml.cs — StateChanged hook)

*scrolling up and down is not working when mouse is on the groups in the discovery page, when it is on the sides the scrolling works
FIXED: ui:Card children were consuming mouse wheel events. Added PreviewMouseWheel handler on GroupsScrollViewer that manually scrolls and marks the event handled, so scroll works regardless of where the mouse is. (DiscoverPage.xaml.cs)

*stations with pictures will be at the top
FIXED: Changed ORDER BY in GetStationsByGroupAsync to OrderByDescending(s => s.LogoUrl != null && s.LogoUrl != "").ThenBy(s => s.Name) — stations with a logo URL sort first. (StationService.cs)

*groups are loading everytime you go to the discovery page instead of being saved somewhere, also start loading the first 30 groups when the app is running, so when the user clicks on discovery they will already be loaded
FIXED: Changed DiscoverViewModel from Transient → Singleton so groups data persists across page navigations. Changed StationService from Scoped → Singleton using IDbContextFactory<RadioDbContext> (each operation creates its own short-lived DbContext). Groups preloaded in App.OnStartup before the window shows. (App.xaml.cs, StationService.cs)

*

