
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
  4. Station implements INotifyPropertyChanged on IsFavorite so heart colour updates instantly on click.
  5. Heart hidden by default (Visibility=Hidden on wrapper Grid, not Collapsed — preserves layout space), visible on hover OR when favourited.
  6. Replaced ToggleButton with ui:Button inside a Grid wrapper — ToggleButton had WPF-UI accent blue background when IsChecked=True; ui:Button Appearance=Transparent has no such issue.
  7. Heart disappeared near row edges — root Grid had no Background so empty areas (column gaps, margins) were not hit-test visible. Added Background=Transparent to root Grid so entire row registers IsMouseOver.
  8. Heart still disappeared at edge — UserControl itself had no Background, so the 8px/4px margin gap between UserControl boundary and the inner Grid was not hit-testable. Added Background=Transparent to the UserControl element.
  (Converters/BoolToHeartIconConverter.cs, Converters/BoolToHeartColorConverter.cs, Controls/StationListItem.xaml, App.xaml, Models/Station.cs)

*add puctures to groups, ask the user how we should implement this so its easy to change pictures and upload them somewhere

*in the audio bar at the button, the symbol when its muted will be changed to be a speaker with a line over it 
FIXED

*to the right of the speaker bar, theres a hear button that does nothing. remove it.
FIXED: Removed the non-functional heart/favourite button from MiniPlayer.xaml (ToggleFavouriteCommand was never implemented in MiniPlayerViewModel). (Controls/MiniPlayer.xaml)

*where it says the current artist and song name, its sometimes long,  make it longer before cutting to ... and also when the mouse hovers on the song or the artist it will show the full name, it will be looking nice and according to the theme, eather dark or light mode
FIXED: Raised MaxWidth from 200→320 on both station name and now-playing TextBlocks. Added ToolTip="{Binding ...}" to each — WPF-UI auto-styles tooltips to match the active Fluent light/dark theme, so hovering shows the full text. (Controls/MiniPlayer.xaml)

*theres a button that minimizes and exmands the left side, when its not minimized,the button will go to the right side of the bar and the symbols will be to arrows poiting outside or inside diagonal
FIXED

*when clicking on pause, the button at the button will turn to the pause button, a triangle. it will have a smooth transition animation when clicked
FIXED: Two issues — (1) LibVLC's Paused event was not subscribed, so IsPlaying never updated to false when pausing; added _mediaPlayer.Paused handler that invokes PlaybackStopped, which sets IsPlaying=false and swaps the icon to Play24 (triangle). (2) Added ScaleTransform + EventTrigger animation on Button.Click that scales 1→0.85→1 over 160ms for a smooth press feel. (Services/RadioPlayerService.cs, Controls/MiniPlayer.xaml)

*when the user clicks on his keyboard the next or previous button, it will iterate through the stations, if its a station in a group then iterate the group stations, if in favorite then iterate favorites. same for the pause and continue button on the keyboard
FIXED: MiniPlayerViewModel now tracks a playlist context. Each page ViewModel passes its current list when calling SetStation (BrowseViewModel → Stations, DiscoverViewModel → GroupStations, FavouritesViewModel → Favourites). NextStation/PreviousStation navigate within that context list; fallback to favourites from DB if no context is set. Play/Pause via media key was already wired through MediaKeyHook → PlayPauseCommand. (ViewModels/MiniPlayerViewModel.cs, ViewModels/BrowseViewModel.cs, ViewModels/DiscoverViewModel.cs, ViewModels/FavouritesViewModel.cs)

*browse page, when clicking the browse it will show prevous searches.
FIXED: Added search history dropdown using an in-layout Border+ItemsControl (Grid.Row="2", no Popup/overlay). History saved to %LocalAppData%/RadioV2/search_history.json, up to 7 entries, deduped. Shows on TextBox GotFocus (if history exists), hides on LostFocus (Dispatcher.Background delay so clicks register). Selecting a history item sets SearchQuery and triggers search. Styled with ApplicationBackgroundBrush + CardStrokeColorDefaultBrush border, themed automatically. (ViewModels/BrowseViewModel.cs, Views/BrowsePage.xaml, Views/BrowsePage.xaml.cs)

*in the browse page it will say "recent sttations" and it will show stations were played while searching or while in the discovery. it will show up to the last 15 stations. each time the user listens to a new station it will be the top one in the browse page. if a station thats already there was played, it will go to the top
FIXED: MiniPlayerViewModel fires a StationStarted event in SetStationCore (covers all pages — Browse, Discover, Favourites). BrowseViewModel subscribes, dedupes by Id, inserts at index 0, caps at 15, and persists to %LocalAppData%/RadioV2/recent_stations.json via a private RecentEntry record (avoids EF navigation-property serialization issues). Loaded on startup. IsRecentVisible hides the section while search is active (SearchQuery.Length >= 2). BrowsePage.xaml adds a "Recent Stations" row (Auto, MaxHeight=400) above the main station list using the same StationListItem control. (ViewModels/MiniPlayerViewModel.cs, ViewModels/BrowseViewModel.cs, Views/BrowsePage.xaml)

*in light mode, some text that is color of grey is too light grey and hard to read, make darker

*when scrolling down in all the pages, the name of the page will stay on top, with the back arrow and the search bar if there is one
FIXED: Root cause was WPF-UI's NavigationViewContentPresenter wrapping ALL page content in a DynamicScrollViewer by default — so titles always scrolled with content regardless of page structure. Fix: override the NavigationViewContentPresenter ControlTemplate in App.xaml to use a DynamicScrollViewer with VerticalScrollBarVisibility="Disabled". This constrains pages to the NavigationView's real height so internal Height="*" Grid rows and ListBox/ScrollViewer scrolling work correctly. Also rewrote DiscoverPage.xaml.cs to use GroupsScrollViewer and the ListBox's internal ScrollViewer directly (old code forwarded events to the now-disabled outer SV). Fixed InfiniteScrollBehavior conditions to add VerticalOffset > 0 guard (prevented false triggers at the top when ScrollableHeight was small). SettingsPage.xaml title was also moved outside its ScrollViewer (auto row above, * row for cards). See Debugging/sticky-header-implementation.md for full history. (App.xaml, Views/DiscoverPage.xaml.cs, Helpers/InfiniteScrollBehavior.cs, Views/SettingsPage.xaml)

*theres a button to expand and minimize the left side menu, i want that button to be two arrow pointing out diagonally for exanding and for minimizing they will be pointing inward. the button will be on the right side of the menu when it is expanded instead of stayin at the same place
FIXED: Hid the default WPF-UI hamburger (IsPaneToggleVisible="False") and added a custom overlay Button (PaneToggleBtn) in Grid.Row="1". Uses SymbolRegular.ArrowMaximizeTopLeftBottomRight20 (outward diagonal arrows) when pane is closed and SymbolRegular.ArrowMinimizeTopLeftBottomRight20 (inward diagonal arrows) when open. Margin is updated in UpdatePaneToggleButton() to always sit at the right edge of the pane — (OpenPaneLength - 40, 8) when expanded, (CompactPaneLength - 40, 8) when compact. IsPaneOpen toggled on click; DependencyPropertyDescriptor watches NavigationView.IsPaneOpenProperty to keep icon/position in sync. (MainWindow.xaml, MainWindow.xaml.cs)

*add search bar to groups

*first time entering a group, scrolling to the bottom did not load more stations; leaving and re-entering the group fixed it
FIXED: StationsListBox lives inside a Collapsed Grid on startup, so its visual tree (including the internal ScrollViewer) is never realized before the first group is opened. TrySetupStationsScrollViewer was called synchronously on the PropertyChanged event for IsGroupView — before WPF's layout pass ran — so FindChildScrollViewer returned null and the ScrollChanged listener was never attached. On the second visit WPF keeps the realized visual tree even when collapsed, so it worked. Fix: wrapped TrySetupStationsScrollViewer + ScrollToTop in Dispatcher.InvokeAsync at DispatcherPriority.Loaded so the layout pass (which realizes the ListBox template) completes first. (Views/DiscoverPage.xaml.cs)

*when scrolling down and it starts to load more stations or groups, the loading spinner stays visible instead of staying at the bottom — scrolling back up while loading keeps the spinner in view
FIXED: Root cause: ProgressRing was in a Grid row outside the scrollable ListBox/ScrollViewer, so it was always pinned to the viewport bottom regardless of scroll position. Fix: added IsAtBottom (bool, default true) and ShowLoadingSpinner (= IsLoading && IsAtBottom) computed property to BrowseViewModel and DiscoverViewModel. IsAtBottom is tracked via ScrollChanged in the code-behind (same pattern as existing scroll detection) and updated directly on the ViewModel. Spinner Visibility binding changed from IsLoading → ShowLoadingSpinner on all three spinners (BrowsePage, DiscoverPage groups, DiscoverPage station list). IsAtBottom initialises true so the spinner shows on first load, becomes false when user scrolls above the threshold, and resets to true when entering a group's station list. Attempted a behavior DP approach first (IsAtBottom as a DP on InfiniteScrollBehavior with OneWayToSource binding) — this silently broke the LoadMoreCommand binding, reverted. Final implementation uses code-behind ScrollChanged handlers only. (ViewModels/BrowseViewModel.cs, ViewModels/DiscoverViewModel.cs, Views/BrowsePage.xaml, Views/BrowsePage.xaml.cs, Views/DiscoverPage.xaml, Views/DiscoverPage.xaml.cs)

*when opening a group for the first time it loads for ~1 second, and the loading spinner is stuck/frozen instead of spinning
FIXED: Two root causes —
  1. EF Core first-time LINQ→SQL query compilation for GetStationsByGroupAsync and GetFeaturedStationsByGroupAsync added ~300ms on the first call per session (same issue as categories, but never warmed up).
  2. foreach (var s in batch) GroupStations.Add(s) — up to 100 individual ObservableCollection.Add calls, each firing CollectionChanged and creating an ItemsControl container on the UI thread, blocking it for ~500ms+. This also caused the ProgressRing animation to freeze: IsLoading=true made the spinner visible, but the UI thread was immediately swamped by 100 ItemContainer creations so the animation storyboard never got a render tick.
  Fix 1: Added two Task.Run warm-up calls in App.OnStartup (after mainWindow.Show()) — GetStationsByGroupAsync(0, 0, 1) and GetFeaturedStationsByGroupAsync(0). groupId=0 returns no rows but forces EF to compile both query shapes in the background before the user clicks any group. (App.xaml.cs)
  Fix 2: In LoadMoreGroupStationsAsync, capture isFirstBatch = _groupSkip == 0 before the query. On first batch assign GroupStations = new ObservableCollection<Station>(batch) — one CollectionChanged Reset instead of 100 Add events. Infinite scroll subsequent batches still append individually. (ViewModels/DiscoverViewModel.cs)
  Fix 3 (spinner still frozen): Even with a single collection assignment, the ItemsControl creates all 100 StationListItem UserControls synchronously in one layout pass — WPF cannot render any animation frames during this time. Root cause: WPF's ProgressRing Storyboard is animation-clock-based but visual updates still require the UI thread; a long synchronous layout pass blocks all rendering. Fix: populate in chunks of 15 with await dispatcher.InvokeAsync(() => {}, DispatcherPriority.Background) between each chunk. DispatcherPriority.Background = 4, which is below Render = 7, so render passes (and animation frames) run before each chunk resumes. ~7 render ticks interleaved across 100 items keeps the spinner visibly spinning. Added using System.Windows and using System.Windows.Threading to DiscoverViewModel. (ViewModels/DiscoverViewModel.cs)

*when clicking on browse page and on fav page, theres a half second delay until it switches
FIXED: Two causes —
  1. WPF-UI NavigationView has a built-in page-switch transition animation (~200–300ms by default). Added TransitionDuration="0" to the NavigationView in MainWindow.xaml to make navigation instant.
  2. FavouritesPage attached LoadFavouritesAsync() to the Loaded event, which fires on EVERY navigation visit (WPF-UI re-adds cached pages to the visual tree each time). This triggered a DB query on every click to the Favourites tab. Fixed by adding a _hasLoaded bool guard in FavouritesViewModel — the DB query only runs on the first visit per session. Subsequent visits return instantly with the already-loaded data. ToggleFavourite still updates the list locally (removing the item in-place) without needing a re-query.
  Note: BrowsePage already had a correct guard via IsRecentVisible — LoadMoreAsync() is skipped on return visits if recent stations are loaded.
  (MainWindow.xaml, ViewModels/FavouritesViewModel.cs)

*

*future features:

*when scrolling down and it loads more stations then the scrolling freezes of a brief moment
FIXED: Root cause — StationsListBox was inside ScrollViewer > StackPanel (unconstrained height), disabling WPF virtualization. Every Add() created a StationListItem container immediately, so each infinite-scroll batch blocked the UI thread. Fix: removed GroupScrollViewer/StackPanel wrapper; restructured group view as a 5-row Grid (Auto/Auto/Auto/*/48). StationsListBox now sits in Height="*" row with VirtualizingStackPanel.IsVirtualizing="True", VirtualizationMode="Recycling", ScrollUnit="Pixel" — only visible containers are created; Add() for off-screen items is nearly free. TrySetupStationsScrollViewer updated to find the ListBox's internal ScrollViewer via FindChildScrollViewer (added to DiscoverPage.xaml.cs). GroupScrollViewer.ScrollToTop() replaced with _stationsSv?.ScrollToTop(). (Views/DiscoverPage.xaml, Views/DiscoverPage.xaml.cs)

*import fav stations parses m3u files and adds
(check if works)

*save images that are in fav list as local cache so it wont load everytime stations pics


*script that has lots of common words and group names and for each group deletes stations that include those words if they dont fit the group they are in



