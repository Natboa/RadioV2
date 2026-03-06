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
    private readonly RadioDbContext _db;
    private readonly M3UParserService _m3uParser;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FavouritesIOService(RadioDbContext db, M3UParserService m3uParser)
    {
        _db = db;
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

        var urls = payload.Favourites.Select(f => f.StreamUrl).ToHashSet();
        return await MarkFavouritesByUrls(urls);
    }

    private async Task<int> ImportM3UAsync(string filePath)
    {
        var parsed = _m3uParser.Parse(filePath);
        var urls = parsed.Select(p => p.StreamUrl).ToHashSet();
        return await MarkFavouritesByUrls(urls);
    }

    private async Task<int> MarkFavouritesByUrls(HashSet<string> urls)
    {
        var matched = await _db.Stations
            .Where(s => urls.Contains(s.StreamUrl) && !s.IsFavorite)
            .ToListAsync();

        foreach (var station in matched)
            station.IsFavorite = true;

        await _db.SaveChangesAsync();
        return matched.Count;
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
