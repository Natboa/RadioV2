using Microsoft.EntityFrameworkCore;
using RadioV2.Data;
using RadioV2.Models;

namespace RadioV2.Services;

public class StationService : IStationService
{
    private readonly RadioDbContext _db;

    public StationService(RadioDbContext db) => _db = db;

    public Task<List<Station>> GetStationsAsync(int skip, int take, string? searchQuery = null, CancellationToken ct = default)
    {
        var query = _db.Stations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(searchQuery))
            query = query.Where(s => EF.Functions.Like(s.Name, $"%{searchQuery}%"));
        return query.OrderBy(s => s.Name).Skip(skip).Take(take).ToListAsync(ct);
    }

    public Task<List<GroupWithCount>> GetGroupsWithCountsAsync(CancellationToken ct = default) =>
        _db.Groups.AsNoTracking()
            .Select(g => new GroupWithCount { Id = g.Id, Name = g.Name, StationCount = g.Stations.Count })
            .OrderBy(g => g.Name)
            .ToListAsync(ct);

    public Task<List<Station>> GetStationsByGroupAsync(int groupId, int skip, int take, string? searchQuery = null, CancellationToken ct = default)
    {
        var query = _db.Stations.AsNoTracking().Where(s => s.GroupId == groupId);
        if (!string.IsNullOrWhiteSpace(searchQuery))
            query = query.Where(s => EF.Functions.Like(s.Name, $"%{searchQuery}%"));
        return query.OrderBy(s => s.Name).Skip(skip).Take(take).ToListAsync(ct);
    }

    public Task<List<Station>> GetFavouritesAsync(CancellationToken ct = default) =>
        _db.Stations.AsNoTracking()
            .Where(s => s.IsFavorite)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

    public async Task ToggleFavouriteAsync(int stationId, CancellationToken ct = default)
    {
        var station = await _db.Stations.FindAsync([stationId], ct);
        if (station is null) return;
        station.IsFavorite = !station.IsFavorite;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken ct = default)
    {
        var setting = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        return setting?.Value;
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken ct = default)
    {
        var setting = await _db.Settings.FindAsync([key], ct);
        if (setting is null)
            _db.Settings.Add(new Setting { Key = key, Value = value });
        else
            setting.Value = value;
        await _db.SaveChangesAsync(ct);
    }
}
