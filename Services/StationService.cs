using Microsoft.EntityFrameworkCore;
using RadioV2.Data;
using RadioV2.Models;

namespace RadioV2.Services;

public class StationService : IStationService
{
    private readonly IDbContextFactory<RadioDbContext> _factory;

    public StationService(IDbContextFactory<RadioDbContext> factory) => _factory = factory;

    public async Task<List<Station>> GetStationsAsync(int skip, int take, string? searchQuery = null, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        var query = db.Stations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(searchQuery))
            query = query.Where(s => EF.Functions.Like(s.Name, $"%{searchQuery}%"));
        return await query.OrderBy(s => s.Name).Skip(skip).Take(take).ToListAsync(ct);
    }

    public async Task<List<GroupWithCount>> GetGroupsWithCountsAsync(int skip, int take, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        return await db.Groups.AsNoTracking()
            .Select(g => new GroupWithCount { Id = g.Id, Name = g.Name, StationCount = g.Stations.Count })
            .OrderBy(g => g.Name)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
    }

    public async Task<List<CategoryWithGroups>> GetCategoriesWithGroupsAsync(CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        return await db.Categories
            .AsNoTracking()
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new CategoryWithGroups
            {
                Id = c.Id,
                Name = c.Name,
                DisplayOrder = c.DisplayOrder,
                Groups = c.Groups
                    .Select(g => new GroupWithCount
                    {
                        Id = g.Id,
                        Name = g.Name,
                        StationCount = g.Stations.Count
                    })
                    .OrderBy(g => g.Name)
                    .ToList()
            })
            .ToListAsync(ct);
    }

    public async Task<List<Station>> GetStationsByGroupAsync(int groupId, int skip, int take, string? searchQuery = null, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        var query = db.Stations.AsNoTracking().Where(s => s.GroupId == groupId);
        if (!string.IsNullOrWhiteSpace(searchQuery))
            query = query.Where(s => EF.Functions.Like(s.Name, $"%{searchQuery}%"));
        return await query.OrderByDescending(s => s.LogoUrl != null && s.LogoUrl != "").ThenBy(s => s.Name).Skip(skip).Take(take).ToListAsync(ct);
    }

    public async Task<List<Station>> GetFavouritesAsync(CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        return await db.Stations.AsNoTracking()
            .Where(s => s.IsFavorite)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
    }

    public async Task ToggleFavouriteAsync(int stationId, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        var station = await db.Stations.FindAsync([stationId], ct);
        if (station is null) return;
        station.IsFavorite = !station.IsFavorite;
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        return setting?.Value;
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        var setting = await db.Settings.FindAsync([key], ct);
        if (setting is null)
            db.Settings.Add(new Setting { Key = key, Value = value });
        else
            setting.Value = value;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> BulkImportStationsAsync(List<ParsedStation> stations, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        var existingUrlsList = await db.Stations.AsNoTracking()
            .Select(s => s.StreamUrl)
            .ToListAsync(ct);
        var existingUrls = existingUrlsList.ToHashSet();

        int added = 0;
        foreach (var parsed in stations)
        {
            if (existingUrls.Contains(parsed.StreamUrl)) continue;

            var group = await db.Groups.FirstOrDefaultAsync(g => g.Name == parsed.GroupName, ct);
            if (group is null)
            {
                group = new Group { Name = parsed.GroupName };
                db.Groups.Add(group);
                await db.SaveChangesAsync(ct);
            }

            db.Stations.Add(new Station
            {
                Name = parsed.Name,
                StreamUrl = parsed.StreamUrl,
                LogoUrl = parsed.LogoUrl,
                GroupId = group.Id,
                IsFavorite = false
            });
            added++;
        }

        if (added > 0)
            await db.SaveChangesAsync(ct);

        return added;
    }
}
