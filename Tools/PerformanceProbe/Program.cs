using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadioV2;
using RadioV2.Helpers;
using RadioV2.Services;
using RadioV2.ViewModels;
using RadioV2.Views;

// Opt-in developer probe. Runs the real App startup, navigation, DB and WPF rows.
// Close RadioV2 first. Does not change settings, favourites or start playback.
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "profile.json");
        var timer = Stopwatch.StartNew();
        var app = new App();
        app.InitializeComponent();
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        poll.Tick += async (_, _) =>
        {
            if (app.MainWindow is not MainWindow window || !window.IsLoaded) return;
            poll.Stop();
            try
            {
                var result = new Dictionary<string, object> { ["windowLoadedMs"] = timer.Elapsed.TotalMilliseconds };
                var host = (IHost)typeof(App).GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
                if (args.Contains("--review"))
                {
                    await ReviewProbe.RunAsync(host.Services, window, Path.GetDirectoryName(output)!);
                    return;
                }
                await Task.Delay(20000); // schema, restore and prewarm must settle
                var proc = Process.GetCurrentProcess();
                var cpu = proc.TotalProcessorTime;
                await Task.Delay(10000);
                proc.Refresh();
                result["idle10s"] = new { cpuMs = (proc.TotalProcessorTime - cpu).TotalMilliseconds, workingSetMB = proc.WorkingSet64 / 1048576d, privateMB = proc.PrivateMemorySize64 / 1048576d, handles = proc.HandleCount };
                var service = host.Services.GetRequiredService<IStationService>();
                var vm = host.Services.GetRequiredService<DiscoverViewModel>();
                var nav = (Wpf.Ui.Controls.NavigationView)window.FindName("RootNavigation");
                nav.Navigate(typeof(DiscoverPage));
                await vm.LoadCategoriesAsync();
                await Settle();
                var page = host.Services.GetRequiredService<DiscoverPage>();
                var list = (ListBox)page.FindName("StationsListBox");
                var groups = (await service.GetCategoriesWithGroupsAsync()).SelectMany(c => c.Groups).DistinctBy(g => g.Id).ToList();
                var openings = new List<object>();
                foreach (var name in new[] { "Brazil", "Rock", "Israel", "Jazz", "Brazil", "Rock" })
                {
                    var group = groups.FirstOrDefault(g => g.Name == name);
                    if (group is null) continue;
                    vm.BackToGroupsCommand.Execute(null);
                    var sw = Stopwatch.StartNew();
                    await vm.SelectGroupAsync(group);
                    await Settle();
                    openings.Add(new { name, renderReadyMs = sw.Elapsed.TotalMilliseconds, rows = vm.AllStationItems.Count, realized = Count<ListBoxItem>(list) });
                }
                result["groupOpenings"] = openings;
                var scroll = Find<ScrollViewer>(list)!;
                var scrollTimes = new List<double>();
                for (int i = 0; i < 30; i++)
                {
                    var sw = Stopwatch.StartNew();
                    scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 180);
                    await Settle();
                    scrollTimes.Add(sw.Elapsed.TotalMilliseconds);
                    await Task.Delay(50);
                }
                result["scroll"] = new { medianMs = Median(scrollTimes), p95Ms = scrollTimes.Order().ElementAt((int)(scrollTimes.Count * .95)), rows = vm.AllStationItems.Count, realized = Count<ListBoxItem>(list) };
                vm.GroupSearchQuery = "radio";
                await Task.Delay(1000);
                result["searchRows"] = vm.GroupStations.Count;
                // Sample only logos naturally loaded in this group's viewport. Cache-warm
                // decoder latency is independent from remote host speed.
                var loader = typeof(AsyncImageLoader).GetMethod("LoadAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
                var images = new List<object>();
                foreach (var station in vm.GroupStations.Where(s => !string.IsNullOrWhiteSpace(s.LogoUrl)).Take(4))
                {
                    var sw = Stopwatch.StartNew();
                    var task = (Task<BitmapImage?>)loader.Invoke(null, new object[] { station.LogoUrl!, CancellationToken.None })!;
                    var bitmap = await task;
                    images.Add(new { ms = sw.Elapsed.TotalMilliseconds, loaded = bitmap != null, width = bitmap?.PixelWidth });
                }
                result["imageLoads"] = images;
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine(output);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { app.Shutdown(); }
        };
        poll.Start();
        app.Run();
    }

    private static async Task Settle()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await Task.Delay(20);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }
    private static double Median(List<double> values) => values.Order().ElementAt(values.Count / 2);
    private static int Count<T>(DependencyObject root) where T : DependencyObject
    {
        int count = root is T ? 1 : 0;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) count += Count<T>(VisualTreeHelper.GetChild(root, i));
        return count;
    }
    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (Find<T>(VisualTreeHelper.GetChild(root, i)) is T found) return found;
        return null;
    }
}
