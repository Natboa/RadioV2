using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RadioV2.Tests;

/// <summary>
/// The Browse page's query is the one that scanned and sorted all 138,925 rows on
/// every page. These tests pin the two things that fix it: a predicate SQLite can use
/// as an equality constraint, and an index that covers it.
/// </summary>
public class StationServiceQueryTests
{
    [Fact]
    public async Task GetStationsAsync_EmitsIndexableEqualityPredicate()
    {
        using var db = new TestDatabase();

        await db.Service.GetStationsAsync(0, 50);

        // NOT ("IsFeatured") cannot satisfy a composite (IsFeatured, Name) index as an
        // equality constraint, so SQLite falls back to a scan plus a temp b-tree.
        // A bound parameter plans identically to a literal, so "= @p" is what we want.
        Assert.DoesNotContain("NOT (\"s\".\"IsFeatured\")", db.CapturedSql);
        Assert.Contains("\"s\".\"IsFeatured\" = @", db.CapturedSql);
    }

    [Fact]
    public async Task GetStationsByGroupAsync_EmitsIndexableEqualityPredicate()
    {
        using var db = new TestDatabase();

        await db.Service.GetStationsByGroupAsync(1, 0, 30);

        Assert.DoesNotContain("NOT (\"s\".\"IsFeatured\")", db.CapturedSql);
        Assert.Contains("\"s\".\"IsFeatured\" = @", db.CapturedSql);
    }

    [Fact]
    public async Task BrowseQuery_UsesTheCoveringIndex_WithNoTempBTree()
    {
        using var db = new TestDatabase();

        using (var ctx = db.StationsFactory.CreateDbContext())
            await ctx.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS idx_stations_feat_name ON Stations(IsFeatured, Name);");

        var plan = QueryPlan(db.StationsDbPath,
            "SELECT Id, Name FROM Stations WHERE IsFeatured = 0 ORDER BY Name LIMIT 50 OFFSET 500");

        Assert.Contains("idx_stations_feat_name", plan);
        Assert.DoesNotContain("TEMP B-TREE", plan, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStationsAsync_StillReturnsOnlyNonFeaturedStations()
    {
        // The predicate change must not alter results, only the plan.
        using var db = new TestDatabase(stationCount: 200, featuredEvery: 10);

        var page = await db.Service.GetStationsAsync(0, 200);

        Assert.Equal(180, page.Count);
        Assert.All(page, s => Assert.False(s.IsFeatured));
    }

    [Fact]
    public async Task GetStationsAsync_PagesWithoutOverlap()
    {
        using var db = new TestDatabase(stationCount: 200, featuredEvery: 0);

        var first = await db.Service.GetStationsAsync(0, 50);
        var second = await db.Service.GetStationsAsync(50, 50);

        Assert.Equal(50, first.Count);
        Assert.Equal(50, second.Count);
        Assert.Empty(first.Select(s => s.Id).Intersect(second.Select(s => s.Id)));
    }

    private static string QueryPlan(string dbPath, string sql)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXPLAIN QUERY PLAN " + sql;
        using var reader = cmd.ExecuteReader();

        var lines = new List<string>();
        while (reader.Read())
            lines.Add(reader.GetString(reader.FieldCount - 1));

        return string.Join(" | ", lines);
    }
}
