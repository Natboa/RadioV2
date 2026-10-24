using RadioV2.Helpers;
using RadioV2.Models;
using RadioV2.ViewModels;
using Xunit;

namespace RadioV2.Tests;

public class PlaybackNoticeTests
{
    [Fact]
    public void RepeatedFailureCoalescesButRetryAndNewStationCanNotify()
    {
        var now = DateTimeOffset.UtcNow;
        var gate = new TransientMessageGate(() => now);
        Assert.True(gate.ShouldShow(1));
        Assert.False(gate.ShouldShow(1));
        Assert.True(gate.ShouldShow(2));
        gate.Reset();
        Assert.True(gate.ShouldShow(2));
        now += TimeSpan.FromSeconds(11);
        Assert.True(gate.ShouldShow(2));
    }

    [Fact]
    public async Task UnavailableStationHasCalmStatusAndRetryPlaysCurrentStation()
    {
        var player = new FakePlayerService();
        var vm = new MiniPlayerViewModel(player, new FakeStationService(), new NetworkMonitor());
        vm.SetStation(new Station { Id = 1, Name = "Station", StreamUrl = "https://example.com/stream" });
        player.RaiseError();
        Assert.Equal("Station unavailable right now", vm.ErrorMessage);
        Assert.Equal("Retry station", vm.PlayPauseLabel);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(2, player.Played.Count);
        Assert.Equal(player.Played[0], player.Played[1]);
    }
}
