using RadioV2.Models;
using RadioV2.Services;

namespace RadioV2.Tests;

/// <summary>Records what the view model asked the player to do, and lets a test
/// raise the callbacks VLC would normally raise.</summary>
internal sealed class FakePlayerService : IRadioPlayerService
{
    public List<string> Played { get; } = [];
    public int StopCount { get; private set; }

    public bool IsPlaying { get; set; }
    public int Volume { get; set; } = 50;

    public Task PlayAsync(string streamUrl)
    {
        Played.Add(streamUrl);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopCount++;
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public event EventHandler<string>? MetadataChanged;
    public event EventHandler<float>? BufferingChanged;
    public event EventHandler? PlaybackStarted;
    public event EventHandler? PlaybackStopped;
    public event EventHandler<string>? PlaybackError;

    public void RaiseStarted()
    {
        IsPlaying = true;
        PlaybackStarted?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseStopped()
    {
        IsPlaying = false;
        PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseError()
    {
        IsPlaying = false;
        PlaybackError?.Invoke(this, "stream error");
    }

    public void RaiseMetadata(string raw) => MetadataChanged?.Invoke(this, raw);

    public void RaiseBuffering(float cache) => BufferingChanged?.Invoke(this, cache);
}

/// <summary>Only the members MiniPlayerViewModel actually touches are implemented;
/// the rest throw loudly so a future dependency cannot slip in unnoticed.</summary>
internal sealed class FakeStationService : IStationService
{
    public List<Station> Favourites { get; } = [];
    public List<int> Toggled { get; } = [];
    public Func<int, int, string?, IReadOnlyCollection<string>?, Task<List<Station>>>? GroupLoad { get; set; }
    public Func<int, Task<List<Station>>>? FeaturedLoad { get; set; }

    public event EventHandler<FavouriteToggledEventArgs>? FavouriteToggled;

    public Task<List<Station>> GetFavouritesAsync(CancellationToken ct = default)
        => Task.FromResult(Favourites.ToList());

    public Task ToggleFavouriteAsync(int stationId, CancellationToken ct = default)
    {
        Toggled.Add(stationId);
        FavouriteToggled?.Invoke(this, new FavouriteToggledEventArgs(stationId, true));
        return Task.CompletedTask;
    }

    public bool CategoriesAreCached => false;

    public Task<List<Station>> GetStationsAsync(int skip, int take, string? searchQuery = null, CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task<List<GroupWithCount>> GetGroupsWithCountsAsync(int skip, int take, CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task<List<Station>> GetStationsByGroupAsync(int groupId, int skip, int take, string? searchQuery = null, CancellationToken ct = default, IReadOnlyCollection<string>? unavailableArtworkUrls = null)
        => GroupLoad?.Invoke(groupId, skip, searchQuery, unavailableArtworkUrls) ?? throw new NotSupportedException();
    public Task<List<CategoryWithGroups>> GetCategoriesWithGroupsAsync(CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task<string?> GetSettingAsync(string key, CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task SetSettingAsync(string key, string value, CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task<int> BulkImportStationsAsync(List<ParsedStation> stations, CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task<List<Station>> GetFeaturedStationsByGroupAsync(int groupId, CancellationToken ct = default, IReadOnlyCollection<string>? unavailableArtworkUrls = null)
        => FeaturedLoad?.Invoke(groupId) ?? Task.FromResult(new List<Station>());
    public Task SetStationFeaturedAsync(int stationId, bool isFeatured, CancellationToken ct = default)
        => throw new NotSupportedException();
}
