using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using RadioV2;
using RadioV2.Data;
using RadioV2.Helpers;
using RadioV2.Models;
using RadioV2.Services;
using RadioV2.ViewModels;
using RadioV2.Views;

internal static class ReviewProbe
{
    // Deterministic visual fixtures live only in view-model memory. Restore the
    // real station/theme and reverse the one favourite toggle even on failure.
    public static async Task RunAsync(IServiceProvider services, MainWindow window, string dir)
    {
        await services.GetRequiredService<SchemaGate>().Ready;
        await Task.Delay(1500);
        Directory.CreateDirectory(dir);
        var service = services.GetRequiredService<IStationService>();
        var mini = services.GetRequiredService<MiniPlayerViewModel>();
        var player = services.GetRequiredService<IRadioPlayerService>();
        var original = mini.CurrentStation;
        if (original is null)
            throw new InvalidOperationException("Visual review requires a restored favourite; play a favourite, pause and close RadioV2 first.");
        var theme = await service.GetSettingAsync("Theme") ?? "Dark";
        Station? toggled = null;
        var checks = new List<string>();
        var nav = (Wpf.Ui.Controls.NavigationView)window.FindName("RootNavigation");
        try
        {
            var catalog = await service.GetStationsAsync(0, 50);
            var favourites = await service.GetFavouritesAsync();
            var candidate = catalog.First(s => favourites.All(f => f.Id != s.Id));
            await service.ToggleFavouriteAsync(candidate.Id);
            toggled = candidate;
            nav.Navigate(typeof(FavouritesPage));
            var favVm = services.GetRequiredService<FavouritesViewModel>();
            await favVm.LoadFavouritesAsync(true);
            if (!favVm.Favourites.Any(f => f.Id == candidate.Id)) throw new Exception("Favourite addition did not reach view");
            await service.ToggleFavouriteAsync(candidate.Id);
            toggled = null;
            await favVm.LoadFavouritesAsync(true);
            if (favVm.Favourites.Any(f => f.Id == candidate.Id)) throw new Exception("Favourite removal did not reach view");
            checks.Add("Favourite add/remove through real SQLite service and favourites view; original state restored");
            foreach (var mode in new[] { "Dark", "Light" })
            {
                ThemeHelper.ApplyTheme(mode);
                nav.Navigate(typeof(BrowsePage));
                var browse = services.GetRequiredService<BrowseViewModel>();
                await Task.Delay(500);
                browse.IsRecentVisible = false;
                browse.Stations.Clear();
                var good = favourites.FirstOrDefault(s => s.Name == "100FM") ?? catalog.First(s => s.LogoUrl != null);
                var fixtures = new[]
                {
                    good,
                    new Station { Id = -1, Name = "An exceptionally long station name that must remain readable without shifting the player or artwork", Group = new Group { Name = "Long name" } },
                    new Station { Id = -2, Name = "רדיו ישראל", Group = new Group { Name = "Unicode initials" } },
                    new Station { Id = -3, Name = "東京音楽", Group = new Group { Name = "Unicode initials" } },
                    new Station { Id = -4, Name = "🎵 Radio", LogoUrl = "https://example.com/logo.svg", Group = new Group { Name = "Unsupported SVG: no request" } },
                    new Station { Id = -5, Name = "e\u0301cho", Group = new Group { Name = "Combining character" } },
                };
                foreach (var station in fixtures) browse.Stations.Add(station);
                mini.RestoreStation(fixtures[2]);
                mini.ErrorMessage = null;
                ((Border)window.FindName("PlaybackNotice")).Visibility = Visibility.Collapsed;
                await Settle();
                Capture(window, Path.Combine(dir, $"review-{mode.ToLowerInvariant()}-artwork.png"));
                checks.Add($"{mode}: actual Browse rows and mini-player rendered with cached logo, missing/SVG logo, long name, Hebrew, Japanese, emoji and combining marks");

                // A refused local port drives the real VLC failure callback; no
                // fake event injection, external station or catalog mutation.
                var unavailable = new Station { Id = mode == "Dark" ? -10 : -11, Name = "Review station", StreamUrl = "http://127.0.0.1:1/stream" };
                mini.RestoreStation(unavailable);
                await mini.PlayPauseCommand.ExecuteAsync(null);
                await WaitUntil(() => mini.HasErrorMessage);
                await Settle();
                Capture(window, Path.Combine(dir, $"review-{mode.ToLowerInvariant()}-notice.png"));
                var notice = (Border)window.FindName("PlaybackNotice");
                if (notice.Visibility != Visibility.Visible) throw new Exception("Unavailable notice not visible");
                var retry = (Wpf.Ui.Controls.Button)window.FindName("RetryStationButton");
                retry.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                await WaitUntil(() => mini.HasErrorMessage);
                if (!ReferenceEquals(mini.CurrentStation, unavailable)) throw new Exception("Retry changed station");
                checks.Add($"{mode}: real unavailable playback callback, neutral visible notice, Retry button handler and retry failure");
                await player.StopAsync();
            }
            await File.WriteAllTextAsync(Path.Combine(dir, "review-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            await player.StopAsync();
            if (toggled is not null) await service.ToggleFavouriteAsync(toggled.Id);
            if (original is not null) mini.RestoreStation(original);
            mini.ErrorMessage = null;
            ThemeHelper.ApplyTheme(theme);
        }
    }

    private static async Task Settle()
    {
        await Task.Delay(250);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }
    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition()) await Task.Delay(50, timeout.Token);
    }
    private static void Capture(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        // Mica is composed by DWM, outside RenderTargetBitmap. Composite the
        // transparent WPF visual over the theme's base brush for portable PNGs.
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var background = window.TryFindResource("ApplicationBackgroundBrush") as System.Windows.Media.Brush
                ?? window.TryFindResource("SolidBackgroundFillColorBaseBrush") as System.Windows.Media.Brush
                ?? throw new InvalidOperationException("No theme base brush for screenshot composition");
            drawing.DrawRectangle(background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
            drawing.DrawRectangle(new VisualBrush(window), null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
