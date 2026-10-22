using Microsoft.EntityFrameworkCore;
using RadioV2.Services;
using Xunit;

namespace RadioV2.Tests;

public class DatabaseInitServiceTests
{
    [Fact]
    public async Task InitialiseAsync_IsIdempotent_AndWritesNothingOnASecondRun()
    {
        using var db = new TestDatabase(stationCount: 10, featuredEvery: 0);

        using (var ctx = db.StationsFactory.CreateDbContext())
            await DatabaseInitService.InitialiseAsync(ctx);

        var first = await ReadCategoriesAsync(db);
        db.Sql.Clear();

        using (var ctx = db.StationsFactory.CreateDbContext())
            await DatabaseInitService.InitialiseAsync(ctx);

        var second = await ReadCategoriesAsync(db);

        Assert.Equal(first, second);

        // Startup ran this on every launch. Re-ordering writes should happen only
        // when the order is actually wrong.
        Assert.DoesNotContain("UPDATE \"Categories\"", db.CapturedSql);
        Assert.DoesNotContain("UPDATE Categories", db.CapturedSql);
    }

    [Fact]
    public async Task InitialiseAsync_RepairsDisplayOrderWhenItIsWrong()
    {
        using var db = new TestDatabase(stationCount: 10, featuredEvery: 0);

        using (var ctx = db.StationsFactory.CreateDbContext())
            await DatabaseInitService.InitialiseAsync(ctx);

        using (var ctx = db.StationsFactory.CreateDbContext())
        {
            var europe = await ctx.Categories.SingleAsync(c => c.Name == "Europe");
            europe.DisplayOrder = 99;
            await ctx.SaveChangesAsync();
        }

        using (var ctx = db.StationsFactory.CreateDbContext())
            await DatabaseInitService.InitialiseAsync(ctx);

        using (var ctx = db.StationsFactory.CreateDbContext())
        {
            var europe = await ctx.Categories.SingleAsync(c => c.Name == "Europe");
            Assert.Equal(10, europe.DisplayOrder);
        }
    }

    [Fact]
    public async Task InitialiseAsync_CreatesBothStationIndexes()
    {
        using var db = new TestDatabase(stationCount: 10, featuredEvery: 0);

        using (var ctx = db.StationsFactory.CreateDbContext())
            await DatabaseInitService.InitialiseAsync(ctx);

        using var check = db.StationsFactory.CreateDbContext();
        var indexes = await check.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='index' AND sql IS NOT NULL")
            .ToListAsync();

        Assert.Contains("idx_stations_groupid", indexes);
        Assert.Contains("idx_stations_feat_name", indexes);
    }

    private static async Task<List<string>> ReadCategoriesAsync(TestDatabase db)
    {
        using var ctx = db.StationsFactory.CreateDbContext();
        return await ctx.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => c.Name + "=" + c.DisplayOrder)
            .ToListAsync();
    }
}
