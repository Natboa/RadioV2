using RadioV2.Helpers;
using RadioV2.Models;
using RadioV2.ViewModels;
using Xunit;

namespace RadioV2.Tests;

public class DiscoverVisitTests
{
    private static DiscoverViewModel Create(FakeStationService stations)
    {
        var network = new NetworkMonitor();
        return new DiscoverViewModel(stations, new MiniPlayerViewModel(new FakePlayerService(), stations, network), network);
    }
    private static List<Station> Batch(int start, int group) => Enumerable.Range(start, 30)
        .Select(i => new Station { Id = i, Name = $"Station {i}", GroupId = group }).ToList();

    [Fact]
    public async Task FailureLearnedDuringVisitCannotChangePagingOrSearchSnapshot()
    {
        var url = $"https://example.com/{Guid.NewGuid()}.png";
        var calls = new List<string[]>();
        var stations = new FakeStationService { GroupLoad = (_, skip, _, snapshot) =>
        { lock (calls) calls.Add(snapshot!.ToArray()); return Task.FromResult(Batch(skip, 1)); } };
        var vm = Create(stations);
        await vm.SelectGroupAsync(new GroupWithCount { Id = 1 });
        StationArtworkCache.Shared.RecordDecodeFailure(url);
        await vm.LoadMoreGroupStationsAsync();
        vm.GroupSearchQuery = "test";
        await WaitUntil(() => calls.Count == 3 && !vm.IsLoading);
        Assert.All(calls, snapshot => Assert.DoesNotContain(url, snapshot));
        await vm.SelectGroupAsync(new GroupWithCount { Id = 1 });
        Assert.Contains(url, calls.Last());
    }

    [Fact]
    public async Task LateOldGroupResultsCannotOverwriteNewGroup()
    {
        var pending = new TaskCompletionSource<List<Station>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stations = new FakeStationService { GroupLoad = (group, _, _, _) =>
        {
            if (group == 1) { entered.TrySetResult(); return pending.Task; }
            return Task.FromResult(Batch(100, group));
        } };
        var vm = Create(stations);
        var old = vm.SelectGroupAsync(new GroupWithCount { Id = 1 });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await vm.SelectGroupAsync(new GroupWithCount { Id = 2 });
        pending.SetResult(Batch(0, 1));
        await old;
        Assert.Equal(30, vm.GroupStations.Count);
        Assert.All(vm.GroupStations, s => Assert.Equal(2, s.GroupId));
    }

    [Fact]
    public async Task FeaturedResultArrivingAfterSearchStillInsertsAtTheTop()
    {
        var pending = new TaskCompletionSource<List<Station>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stations = new FakeStationService
        {
            FeaturedLoad = _ => { entered.SetResult(); return pending.Task; },
            GroupLoad = (_, _, _, _) => Task.FromResult(Batch(0, 1))
        };
        var vm = Create(stations);
        var opening = vm.SelectGroupAsync(new GroupWithCount { Id = 1 });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        vm.GroupSearchQuery = "radio";
        await WaitUntil(() => vm.GroupStations.Count == 30 && !vm.IsLoading);
        var searched = vm.GroupStations;
        vm.GroupSearchQuery = "";
        await WaitUntil(() => !ReferenceEquals(searched, vm.GroupStations) && !vm.IsLoading);
        pending.SetResult([new Station { Id = 100, IsFeatured = true }]);
        await opening;
        Assert.IsType<FeaturedHeaderItem>(vm.AllStationItems[0]);
        Assert.Equal(100, Assert.IsType<StationGroupViewItem>(vm.AllStationItems[1]).Station.Id);
        Assert.Single(vm.AllStationItems.OfType<FeaturedHeaderItem>());
    }

    [Fact]
    public async Task LateSearchResultsCannotAppendToNewerQuery()
    {
        var pending = new TaskCompletionSource<List<Station>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stations = new FakeStationService { GroupLoad = (_, _, query, _) =>
        {
            if (query == "old") { entered.SetResult(); return pending.Task; }
            return Task.FromResult(Batch(query == "new" ? 100 : 0, 1));
        } };
        var vm = Create(stations);
        await vm.SelectGroupAsync(new GroupWithCount { Id = 1 });
        vm.GroupSearchQuery = "old";
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        vm.GroupSearchQuery = "new";
        await WaitUntil(() => !vm.IsLoading && vm.GroupStations.FirstOrDefault()?.Id == 100);
        pending.SetResult(Batch(0, 1));
        await Task.Delay(100);
        Assert.Equal(30, vm.GroupStations.Count);
        Assert.All(vm.GroupStations, s => Assert.True(s.Id >= 100));
    }

    [Fact]
    public async Task BackDuringFeaturedLoadDoesNotRestoreAbandonedRows()
    {
        var pending = new TaskCompletionSource<List<Station>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stations = new FakeStationService { FeaturedLoad = _ => { entered.SetResult(); return pending.Task; } };
        var vm = Create(stations);
        var old = vm.SelectGroupAsync(new GroupWithCount { Id = 1 });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        vm.BackToGroupsCommand.Execute(null);
        pending.SetResult(Batch(0, 1));
        await old;
        Assert.Empty(vm.AllStationItems);
        Assert.False(vm.IsLoading);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(20, timeout.Token);
    }
}
