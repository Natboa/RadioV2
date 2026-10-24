using System.Net;
using Microsoft.EntityFrameworkCore;
using RadioV2.Helpers;
using Xunit;

namespace RadioV2.Tests;

public class StationArtworkTests
{
    [Theory]
    [InlineData("Radio One", "RO")]
    [InlineData("  Jazz  ", "JA")]
    [InlineData("רדיו ישראל", "רי")]
    [InlineData("東京音楽", "東京")]
    [InlineData("e\u0301cho", "E\u0301C")]
    [InlineData("🎵 Radio", "🎵R")]
    [InlineData(" & | - ", "?")]
    public void InitialsPreserveTextElements(string name, string expected) =>
        Assert.Equal(expected, StationInitials.For(name));

    [Fact]
    public void FailuresExpireAreBoundedAndSnapshotsStayFrozen()
    {
        var now = DateTimeOffset.UtcNow;
        var cache = new StationArtworkCache(() => now);
        cache.RecordHttpFailure("https://a/1", HttpStatusCode.NotFound);
        var frozen = cache.Snapshot();
        cache.RecordDecodeFailure("https://a/2");
        Assert.Single(frozen);
        for (int i = 0; i < 300; i++) cache.RecordDecodeFailure($"https://a/{i}");
        Assert.Equal(256, cache.Snapshot().Length);
        now += TimeSpan.FromHours(7);
        Assert.Empty(cache.Snapshot());
    }

    [Theory]
    [InlineData(408)] [InlineData(429)] [InlineData(500)] [InlineData(503)] [InlineData(403)]
    public void TransientHttpResponsesDoNotDemoteArtwork(int status)
    {
        var cache = new StationArtworkCache();
        cache.RecordHttpFailure("https://a/logo.png", (HttpStatusCode)status);
        Assert.Empty(cache.Snapshot());
    }

    [Theory]
    [InlineData(null, false)] [InlineData(" ", false)]
    [InlineData("https://a/logo.SVG?x=1", false)]
    [InlineData("https://a/logo.svg#logo", false)]
    [InlineData("https://a/svg/logo.png", true)]
    [InlineData("https://a/logo.png?type=.svg", true)]
    [InlineData("not a URL", false)] [InlineData("ftp://a/logo.png", false)]
    public void SupportedArtworkUsesTheUrlPath(string? url, bool expected) =>
        Assert.Equal(expected, StationArtworkCache.Supports(url));

    [Fact]
    public async Task GroupPagesAndSearchUseFrozenFailuresAndUniqueTieBreakers()
    {
        using var db = new TestDatabase(stationCount: 75, featuredEvery: 0);
        using (var ctx = db.StationsFactory.CreateDbContext())
        {
            var rows = await ctx.Stations.OrderBy(s => s.Id).ToListAsync();
            foreach (var row in rows) { row.Name = "Same name"; row.LogoUrl = null; }
            rows[0].LogoUrl = "https://a/dead.png";
            rows[1].LogoUrl = "https://a/logo.SVG?x=1";
            rows[2].LogoUrl = "   ";
            rows[3].LogoUrl = "https://a/working.png";
            rows[4].IsFeatured = true;
            await ctx.SaveChangesAsync();
        }
        string[] frozen = ["https://a/dead.png"];
        var first = await db.Service.GetStationsByGroupAsync(1, 0, 30, unavailableArtworkUrls: frozen);
        var next = await db.Service.GetStationsByGroupAsync(1, 30, 50, unavailableArtworkUrls: frozen);
        Assert.Equal(4, first[0].Id);
        Assert.Equal(74, first.Concat(next).Select(s => s.Id).Distinct().Count());
        var searched = await db.Service.GetStationsByGroupAsync(1, 0, 80, "Same", unavailableArtworkUrls: frozen);
        Assert.Equal(5, searched[0].Id); // featured stays pinned even in search
        Assert.Equal(4, searched[1].Id);
        Assert.Equal(first.Select(s => s.Id), searched.Where(s => !s.IsFeatured).Take(30).Select(s => s.Id));
    }
}
