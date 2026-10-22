using RadioV2.Helpers;
using RadioV2.Models;
using RadioV2.ViewModels;
using Xunit;

namespace RadioV2.Tests;

/// <summary>
/// Review Focus 3 and 4: a reconnect must never resurrect a station the user
/// deliberately stopped, and a wake overlapping a network restore must produce
/// exactly one stream rather than two.
/// </summary>
public class MiniPlayerReconnectTests
{
    private static (MiniPlayerViewModel Vm, FakePlayerService Player) Create()
    {
        var player = new FakePlayerService();
        var vm = new MiniPlayerViewModel(player, new FakeStationService(), new NetworkMonitor());
        return (vm, player);
    }

    private static Station Station(int id, string name) => new()
    {
        Id = id,
        Name = name,
        StreamUrl = $"http://example.com/{id}",
        GroupId = 1,
    };

    [Fact]
    public async Task ExplicitStop_ThenReconnectTrigger_DoesNotResume()
    {
        var (vm, player) = Create();

        vm.SetStation(Station(1, "A"));
        player.RaiseStarted();

        await vm.StopCommand.ExecuteAsync(null);
        player.RaiseStopped();
        player.Played.Clear();

        vm.TryAutoReconnect();

        Assert.Empty(player.Played);
    }

    [Fact]
    public async Task PauseButton_ThenReconnectTrigger_DoesNotResume()
    {
        var (vm, player) = Create();

        vm.SetStation(Station(1, "A"));
        player.RaiseStarted();

        await vm.PlayPauseCommand.ExecuteAsync(null); // pausing while playing
        player.RaiseStopped();
        player.Played.Clear();

        vm.TryAutoReconnect();

        Assert.Empty(player.Played);
    }

    [Fact]
    public void TwoOverlappingReconnectTriggers_StartPlaybackOnce()
    {
        var (vm, player) = Create();

        vm.SetStation(Station(1, "A"));
        player.RaiseStarted();
        player.RaiseStopped(); // the network dropped out from under us
        player.Played.Clear();

        vm.TryAutoReconnect(); // network restored
        vm.TryAutoReconnect(); // wake timer fires a moment later

        Assert.Single(player.Played);
    }

    [Fact]
    public void UserSelectingAStation_AlwaysWins_EvenWhileReconnecting()
    {
        var (vm, player) = Create();

        vm.SetStation(Station(1, "A"));
        player.RaiseStarted();
        player.RaiseStopped();
        player.Played.Clear();

        vm.TryAutoReconnect();            // a reconnect is now in flight
        vm.SetStation(Station(2, "B"));   // the user picks a different station

        Assert.Equal(2, player.Played.Count);
        Assert.Equal("http://example.com/2", player.Played[^1]);
    }

    [Fact]
    public async Task Sleeping_StopsPlayback_ButLeavesReconnectArmed()
    {
        var (vm, player) = Create();

        vm.SetStation(Station(1, "A"));
        player.RaiseStarted();

        await vm.OnSleepingAsync();
        Assert.Equal(1, player.StopCount);

        player.RaiseStopped();
        player.Played.Clear();

        vm.TryAutoReconnect();

        Assert.Single(player.Played);
    }

    [Fact]
    public void PlaybackError_ClearsTheGuard_SoALaterReconnectCanRun()
    {
        var (vm, player) = Create();

        vm.SetStation(Station(1, "A"));
        player.RaiseError();
        player.Played.Clear();

        vm.TryAutoReconnect();

        Assert.Single(player.Played);
    }

    [Fact]
    public void Metadata_IsParsedIntoArtistAndTitle()
    {
        var (vm, player) = Create();

        vm.SetStation(Station(1, "A"));
        player.RaiseMetadata("Pink Floyd - Time");

        Assert.Equal("Pink Floyd", vm.NowPlayingArtist);
        Assert.Equal("Time", vm.NowPlayingTitle);
        Assert.Equal("Pink Floyd — Time", vm.NowPlayingDisplay);
    }
}
