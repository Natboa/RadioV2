using System.IO;
using Microsoft.EntityFrameworkCore;
using RadioV2.Data;
using RadioV2.Models;

namespace RadioV2.DevTool.Services;

public class DevDbService
{
    private readonly string _dbPath;

    public DevDbService()
    {
        // From bin/Debug/net8.0-windows/ go up 4 levels to solution root, then into Data/
        _dbPath = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\Data\radioapp_large_groups.db"));
    }

    private RadioDbContext CreateContext()
    {
        var opts = new DbContextOptionsBuilder<RadioDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        return new RadioDbContext(opts);
    }

    // ── Groups ────────────────────────────────────────────────────────────────

    public async Task<List<GroupWithCount>> GetGroupsAsync(string? search = null)
    {
        await using var db = CreateContext();
        var query = db.Groups.AsNoTracking()
            .Select(g => new GroupWithCount
            {
                Id = g.Id,
                Name = g.Name,
                StationCount = g.Stations.Count
            });

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(g => EF.Functions.Like(g.Name, $"%{search}%"));

        return await query.OrderBy(g => g.Name).ToListAsync();
    }

    public async Task<int> GetStationCountForGroupAsync(int groupId)
    {
        await using var db = CreateContext();
        return await db.Stations.AsNoTracking().CountAsync(s => s.GroupId == groupId);
    }

    public async Task CreateGroupAsync(string name)
    {
        await using var db = CreateContext();
        db.Groups.Add(new Group { Name = name });
        await db.SaveChangesAsync();
    }

    public async Task RenameGroupAsync(int id, string newName)
    {
        await using var db = CreateContext();
        var group = await db.Groups.FindAsync(id)
            ?? throw new InvalidOperationException($"Group {id} not found.");
        group.Name = newName;
        await db.SaveChangesAsync();
    }

    public async Task DeleteGroupWithStationsAsync(int groupId)
    {
        await using var db = CreateContext();
        await db.Stations.Where(s => s.GroupId == groupId).ExecuteDeleteAsync();
        await db.Groups.Where(g => g.Id == groupId).ExecuteDeleteAsync();
    }

    public async Task MergeGroupsAsync(int sourceId, int targetId)
    {
        await using var db = CreateContext();
        await db.Stations
            .Where(s => s.GroupId == sourceId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.GroupId, targetId));
        await db.Groups.Where(g => g.Id == sourceId).ExecuteDeleteAsync();
    }

    // ── Stations ──────────────────────────────────────────────────────────────

    public async Task<List<Station>> GetStationsAsync(string? search, int? groupId, int skip, int take)
    {
        await using var db = CreateContext();
        var query = db.Stations.AsNoTracking().Include(s => s.Group).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(s => EF.Functions.Like(s.Name, $"%{search}%"));

        if (groupId.HasValue)
            query = query.Where(s => s.GroupId == groupId.Value);

        return await query.OrderBy(s => s.Name).Skip(skip).Take(take).ToListAsync();
    }

    public async Task<int> GetStationCountAsync(string? search, int? groupId)
    {
        await using var db = CreateContext();
        var query = db.Stations.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(s => EF.Functions.Like(s.Name, $"%{search}%"));

        if (groupId.HasValue)
            query = query.Where(s => s.GroupId == groupId.Value);

        return await query.CountAsync();
    }

    public async Task CreateStationAsync(Station station)
    {
        await using var db = CreateContext();
        db.Stations.Add(station);
        await db.SaveChangesAsync();
    }

    public async Task UpdateStationAsync(Station station)
    {
        await using var db = CreateContext();
        db.Stations.Update(station);
        await db.SaveChangesAsync();
    }

    public async Task DeleteStationAsync(int id)
    {
        await using var db = CreateContext();
        await db.Stations.Where(s => s.Id == id).ExecuteDeleteAsync();
    }
}
