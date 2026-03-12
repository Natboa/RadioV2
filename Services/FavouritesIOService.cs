using Microsoft.EntityFrameworkCore;
using RadioV2.Data;
using RadioV2.Models;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadioV2.Services;

public class FavouritesIOService : IFavouritesIOService
{
    private readonly IDbContextFactory<StationsDbContext> _stationsFactory;
    private readonly IDbContextFactory<UserDbContext> _userFactory;
    private readonly M3UParserService _m3uParser;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FavouritesIOService(
        IDbContextFactory<StationsDbContext> stationsFactory,
        IDbContextFactory<UserDbContext> userFactory,
        M3UParserService m3uParser)
    {
        _stationsFactory = stationsFactory;
        _userFactory = userFactory;
        _m3uParser = m3uParser;
    }

    // ── Export ────────────────────────────────────────────────────────────

    public async Task ExportAsync(string filePath, string format, List<Station> favourites)
    {
        if (format == "json")
            await ExportJsonAsync(filePath, favourites);
        else
            await ExportM3UAsync(filePath, favourites);
    }

    private static async Task ExportJsonAsync(string filePath, List<Station> favourites)
    {
        var payload = new FavouritesExport
        {
            Version = 1,
            Exported = DateTime.UtcNow,
            Favourites = favourites.Select(s => new FavouriteEntry
            {
                Name = s.Name,
                StreamUrl = s.StreamUrl,
                LogoUrl = s.LogoUrl,
                Group = s.Group?.Name ?? string.Empty
            }).ToList()
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
    }

    private static async Task ExportM3UAsync(string filePath, List<Station> favourites)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");

        foreach (var s in favourites)
        {
            var logo = string.IsNullOrEmpty(s.LogoUrl) ? string.Empty : $" tvg-logo=\"{s.LogoUrl}\"";
            var group = s.Group?.Name ?? "Uncategorized";
            sb.AppendLine($"#EXTINF:-1{logo} group-title=\"{group}\",{s.Name}");
            sb.AppendLine(s.StreamUrl);
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
    }

    // ── Import ────────────────────────────────────────────────────────────

    public async Task<int> ImportAsync(string filePath, string format)
    {
        if (format == "json")
            return await ImportJsonAsync(filePath);
        return await ImportM3UAsync(filePath);
    }

    private async Task<int> ImportJsonAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var payload = JsonSerializer.Deserialize<FavouritesExport>(json, JsonOptions);
        if (payload?.Favourites is null) return 0;

        var urlMap = payload.Favourites
            .GroupBy(f => f.StreamUrl)
            .ToDictionary(g => g.Key, g => g.First());
        return await AddFavouritesByUrls(urlMap);
    }

    private async Task<int> ImportM3UAsync(string filePath)
    {
        var parsed = _m3uParser.Parse(filePath);
        var urlMap = parsed
            .GroupBy(p => p.StreamUrl)
            .ToDictionary(g => g.Key, g => new FavouriteEntry
            {
                Name = g.First().Name,
                StreamUrl = g.Key,
                LogoUrl = g.First().LogoUrl,
                Group = g.First().GroupName
            });
        return await AddFavouritesByUrls(urlMap);
    }

    private async Task<int> AddFavouritesByUrls(Dictionary<string, FavouriteEntry> urlMap)
    {
        var urls = urlMap.Keys.ToHashSet();

        // 1. Find existing station IDs in stations.db that match the URLs
        using var stationsDb = _stationsFactory.CreateDbContext();
        var matched = await stationsDb.Stations
            .AsNoTracking()
            .Where(s => urls.Contains(s.StreamUrl))
            .Select(s => new { s.Id, s.StreamUrl })
            .ToListAsync();

        var matchedUrls = matched.Select(s => s.StreamUrl).ToHashSet();
        var unmatchedUrls = urls.Except(matchedUrls).ToList();

        // 2. For unmatched URLs, create minimal station records in stations.db
        var newIds = new List<int>();
        foreach (var url in unmatchedUrls)
        {
            if (!urlMap.TryGetValue(url, out var entry)) continue;

            var groupName = string.IsNullOrWhiteSpace(entry.Group) ? "Imported" : entry.Group;
            var group = await stationsDb.Groups.FirstOrDefaultAsync(g => g.Name == groupName);
            if (group is null)
            {
                group = new Group { Name = groupName };
                stationsDb.Groups.Add(group);
                await stationsDb.SaveChangesAsync();
            }

            var newStation = new Station
            {
                Name = entry.Name,
                StreamUrl = url,
                LogoUrl = entry.LogoUrl,
                GroupId = group.Id
            };
            stationsDb.Stations.Add(newStation);
            await stationsDb.SaveChangesAsync();
            newIds.Add(newStation.Id);
        }

        // 3. Add all matching station IDs to UserDbContext.Favourites (skip already-added)
        var allIds = matched.Select(s => s.Id).Concat(newIds).ToList();
        if (allIds.Count == 0) return 0;

        using var userDb = _userFactory.CreateDbContext();
        var alreadyFaved = (await userDb.Favourites.AsNoTracking()
            .Where(f => allIds.Contains(f.StationId))
            .Select(f => f.StationId)
            .ToListAsync()).ToHashSet();

        int added = 0;
        foreach (var id in allIds)
        {
            if (alreadyFaved.Contains(id)) continue;
            userDb.Favourites.Add(new Favourite { StationId = id });
            added++;
        }

        if (added > 0)
            await userDb.SaveChangesAsync();

        return added;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────

    private class FavouritesExport
    {
        public int Version { get; set; }
        public DateTime Exported { get; set; }
        public List<FavouriteEntry> Favourites { get; set; } = [];
    }

    private class FavouriteEntry
    {
        public string Name { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LogoUrl { get; set; }
        public string Group { get; set; } = string.Empty;
    }
}
