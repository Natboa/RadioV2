using System.IO;
using Microsoft.EntityFrameworkCore;
using RadioV2.Data;
using RadioV2.Models;

namespace RadioV2.Services;

public record FavouriteToggledEventArgs(int StationId, bool IsFavorite);

public class StationService : IStationService
{
    public event EventHandler<FavouriteToggledEventArgs>? FavouriteToggled;
    private readonly IDbContextFactory<StationsDbContext> _stationsFactory;
    private readonly IDbContextFactory<UserDbContext> _userFactory;
    private readonly IStationLogoCache _logoCache;
    private volatile List<CategoryWithGroups>? _categoriesCache;

    public StationService(IDbContextFactory<StationsDbContext> stationsFactory, IDbContextFactory<UserDbContext> userFactory, IStationLogoCache logoCache)
    {
        _stationsFactory = stationsFactory;
        _userFactory = userFactory;
        _logoCache = logoCache;
    }

    public bool CategoriesAreCached => _categoriesCache is not null;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<HashSet<int>> GetFavouriteIdsAsync(CancellationToken ct = default)
    {
        using var db = _userFactory.CreateDbContext();
        var ids = await db.Favourites.AsNoTracking().Select(f => f.StationId).ToListAsync(ct);
        return [.. ids];
    }

    private async Task EnrichWithFavouritesAsync(List<Station> stations, CancellationToken ct = default)
    {
        if (stations.Count == 0) return;
        var ids = await GetFavouriteIdsAsync(ct);
        foreach (var s in stations)
            s.IsFavorite = ids.Contains(s.Id);
    }

    // ── Station queries ───────────────────────────────────────────────────────

    public async Task<List<Station>> GetStationsAsync(int skip, int take, string? searchQuery = null, CancellationToken ct = default)
    {
        using var db = _stationsFactory.CreateDbContext();
        var query = db.Stations.AsNoTracking().Where(s => !s.IsFeatured);
        if (!string.IsNullOrWhiteSpace(searchQuery))
            query = query.Where(s => EF.Functions.Like(s.Name, $"%{searchQuery}%"));
        var stations = await query.OrderBy(s => s.Name).Skip(skip).Take(take).ToListAsync(ct);
        await EnrichWithFavouritesAsync(stations, ct);
        return stations;
    }

    public async Task<List<GroupWithCount>> GetGroupsWithCountsAsync(int skip, int take, CancellationToken ct = default)
    {
        using var db = _stationsFactory.CreateDbContext();
        return await db.Groups.AsNoTracking()
            .Select(g => new GroupWithCount { Id = g.Id, Name = g.Name, StationCount = g.Stations.Count })
            .OrderBy(g => g.Name)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
    }

    public async Task<List<CategoryWithGroups>> GetCategoriesWithGroupsAsync(CancellationToken ct = default)
    {
        if (_categoriesCache is not null)
            return _categoriesCache;

        using var db = _stationsFactory.CreateDbContext();

        var counts = await db.Stations
            .AsNoTracking()
            .GroupBy(s => s.GroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count, ct);

        var categories = await db.Categories
            .AsNoTracking()
            .OrderBy(c => c.DisplayOrder)
            .Include(c => c.Groups)
            .ToListAsync(ct);

        var result = categories.Select(c => new CategoryWithGroups
        {
            Id = c.Id,
            Name = c.Name,
            DisplayOrder = c.DisplayOrder,
            Groups = c.Groups
                .Select(g => new GroupWithCount
                {
                    Id = g.Id,
                    Name = g.Name,
                    StationCount = counts.GetValueOrDefault(g.Id, 0)
                })
                .OrderBy(g => g.Name)
                .ToList()
        }).ToList();

        _categoriesCache = result;
        return result;
    }

    public async Task<List<Station>> GetStationsByGroupAsync(int groupId, int skip, int take, string? searchQuery = null, CancellationToken ct = default)
    {
        using var db = _stationsFactory.CreateDbContext();
        bool hasQuery = !string.IsNullOrWhiteSpace(searchQuery);
        var query = db.Stations.AsNoTracking().Where(s => s.GroupId == groupId && (hasQuery || !s.IsFeatured));
        if (hasQuery)
            query = query.Where(s => EF.Functions.Like(s.Name, $"%{searchQuery}%"));
        var stations = await query.OrderByDescending(s => s.LogoUrl != null && s.LogoUrl != "").ThenBy(s => s.Name).Skip(skip).Take(take).ToListAsync(ct);
        await EnrichWithFavouritesAsync(stations, ct);
        return stations;
    }

    public async Task<List<Station>> GetFeaturedStationsByGroupAsync(int groupId, CancellationToken ct = default)
    {
        using var db = _stationsFactory.CreateDbContext();
        var stations = await db.Stations
            .AsNoTracking()
            .Where(s => s.GroupId == groupId && s.IsFeatured)
            .OrderByDescending(s => s.LogoUrl != null && s.LogoUrl != "")
            .ThenBy(s => s.Name)
            .ToListAsync(ct);
        await EnrichWithFavouritesAsync(stations, ct);
        return stations;
    }

    // ── Favourites ────────────────────────────────────────────────────────────

    public async Task<List<Station>> GetFavouritesAsync(CancellationToken ct = default)
    {
        using var userDb = _userFactory.CreateDbContext();
        var favIds = await userDb.Favourites.AsNoTracking().Select(f => f.StationId).ToListAsync(ct);
        if (favIds.Count == 0) return [];

        using var stationsDb = _stationsFactory.CreateDbContext();
        var stations = await stationsDb.Stations.AsNoTracking()
            .Where(s => favIds.Contains(s.Id))
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        foreach (var s in stations) s.IsFavorite = true;
        return stations;
    }

    public async Task ToggleFavouriteAsync(int stationId, CancellationToken ct = default)
    {
        using var db = _userFactory.CreateDbContext();
        var existing = await db.Favourites.FirstOrDefaultAsync(f => f.StationId == stationId, ct);
        bool adding = existing is null;

        if (adding)
            db.Favourites.Add(new Favourite { StationId = stationId });
        else
            db.Favourites.Remove(existing!);
        await db.SaveChangesAsync(ct);

        FavouriteToggled?.Invoke(this, new FavouriteToggledEventArgs(stationId, adding));

        if (adding)
        {
            _ = Task.Run(async () =>
            {
                using var stDb = _stationsFactory.CreateDbContext();
                var logoUrl = await stDb.Stations.AsNoTracking()
                    .Where(s => s.Id == stationId)
                    .Select(s => s.LogoUrl)
                    .FirstOrDefaultAsync();
                if (!string.IsNullOrEmpty(logoUrl))
                    await _logoCache.DownloadAsync(stationId, logoUrl);
            });
        }
        else
        {
            _logoCache.Delete(stationId);
        }
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    public async Task<string?> GetSettingAsync(string key, CancellationToken ct = default)
    {
        using var db = _userFactory.CreateDbContext();
        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        return setting?.Value;
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken ct = default)
    {
        using var db = _userFactory.CreateDbContext();
        var setting = await db.Settings.FindAsync([key], ct);
        if (setting is null)
            db.Settings.Add(new Setting { Key = key, Value = value });
        else
            setting.Value = value;
        await db.SaveChangesAsync(ct);
    }

    // ── Bulk import ───────────────────────────────────────────────────────────

    public async Task<int> BulkImportStationsAsync(List<ParsedStation> stations, CancellationToken ct = default)
    {
        using var db = _stationsFactory.CreateDbContext();
        var existingUrls = (await db.Stations.AsNoTracking().Select(s => s.StreamUrl).ToListAsync(ct)).ToHashSet();

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
            });
            added++;
        }

        if (added > 0)
            await db.SaveChangesAsync(ct);

        return added;
    }

    // ── Featured ──────────────────────────────────────────────────────────────

    public async Task SetStationFeaturedAsync(int stationId, bool isFeatured, CancellationToken ct = default)
    {
        using var db = _stationsFactory.CreateDbContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Stations SET IsFeatured = {(isFeatured ? 1 : 0)} WHERE Id = {stationId}", ct);

#if DEBUG
        // Also write to the source DB so featured data is baked into the seed for future builds
        var sourceDb = FindSourceDbPath();
        if (sourceDb != null)
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={sourceDb}");
            await conn.OpenAsync(ct);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE Stations SET IsFeatured = {(isFeatured ? 1 : 0)} WHERE Id = {stationId}";
            await cmd.ExecuteNonQueryAsync(ct);
        }
#endif
    }

#if DEBUG
    private static string? FindSourceDbPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Data", "stations.db");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
#endif
}
