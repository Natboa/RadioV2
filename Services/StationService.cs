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
    private readonly SchemaGate _schema;
    private volatile List<CategoryWithGroups>? _categoriesCache;

    public StationService(IDbContextFactory<StationsDbContext> stationsFactory, IDbContextFactory<UserDbContext> userFactory, SchemaGate schema)
    {
        _stationsFactory = stationsFactory;
        _userFactory = userFactory;
        _schema = schema;
    }

    /// <summary>
    /// Opens a stations.db context, waiting for the schema migration first.
    /// Every read of stations.db goes through here — see <see cref="SchemaGate"/>.
    /// </summary>
    private async Task<StationsDbContext> OpenStationsAsync(CancellationToken ct = default)
    {
        await _schema.Ready.WaitAsync(ct);
        return _stationsFactory.CreateDbContext();
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
        using var db = await OpenStationsAsync(ct);
        // Do NOT simplify this to !s.IsFeatured or s.IsFeatured == false. EF Core 8
        // folds both into NOT ("s"."IsFeatured"), which SQLite cannot use as an
        // equality constraint — the plan degrades to a scan plus a temp b-tree sort
        // (17 ms per page against 0.1 ms). Comparing against a variable emits
        // "IsFeatured" = @p, which SEARCHes idx_stations_feat_name.
        // Verified with EXPLAIN QUERY PLAN; pinned by StationServiceQueryTests.
        var notFeatured = false;
        var query = db.Stations.AsNoTracking().Where(s => s.IsFeatured == notFeatured);
        if (!string.IsNullOrWhiteSpace(searchQuery))
            query = query.Where(s => EF.Functions.Like(s.Name, $"%{searchQuery}%"));
        var stations = await query.OrderBy(s => s.Name).Skip(skip).Take(take).ToListAsync(ct);
        await EnrichWithFavouritesAsync(stations, ct);
        return stations;
    }

    public async Task<List<GroupWithCount>> GetGroupsWithCountsAsync(int skip, int take, CancellationToken ct = default)
    {
        using var db = await OpenStationsAsync(ct);
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

        using var db = await OpenStationsAsync(ct);

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
        using var db = await OpenStationsAsync(ct);
        bool hasQuery = !string.IsNullOrWhiteSpace(searchQuery);
        // See GetStationsAsync — the variable is what makes this an indexable equality.
        var notFeatured = false;
        var query = db.Stations.AsNoTracking().Where(s => s.GroupId == groupId && (hasQuery || s.IsFeatured == notFeatured));
        if (hasQuery)
            query = query.Where(s => EF.Functions.Like(s.Name, $"%{searchQuery}%"));
        var stations = await query.OrderByDescending(s => s.LogoUrl != null && s.LogoUrl != "").ThenBy(s => s.Name).Skip(skip).Take(take).ToListAsync(ct);
        await EnrichWithFavouritesAsync(stations, ct);
        return stations;
    }

    public async Task<List<Station>> GetFeaturedStationsByGroupAsync(int groupId, CancellationToken ct = default)
    {
        using var db = await OpenStationsAsync(ct);
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

        using var stationsDb = await OpenStationsAsync(ct);
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

        // Artwork used to be fetched into a second, station-id-keyed cache here.
        // AsyncImageLoader caches every logo it displays, keyed by URL, so a station
        // you can see well enough to favourite is already cached — the extra
        // download, the extra copy on disk and the delete-on-unfavourite were all
        // duplicating it.
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
        using var db = await OpenStationsAsync(ct);
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
        using var db = await OpenStationsAsync(ct);
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
