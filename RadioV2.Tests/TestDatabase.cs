using System.IO;
using Microsoft.EntityFrameworkCore;
using RadioV2.Data;
using RadioV2.Models;
using RadioV2.Services;

namespace RadioV2.Tests;

/// <summary>
/// A disposable pair of real SQLite files (stations + userdata) wired into a
/// <see cref="StationService"/>, plus a capture of every SQL statement EF emits.
///
/// Real files rather than in-memory, because the behaviour under test is how SQLite
/// plans these queries — an in-memory provider would answer a different question.
/// </summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly string _dir;

    public List<string> Sql { get; } = [];
    public StationService Service { get; }
    public IDbContextFactory<StationsDbContext> StationsFactory { get; }
    public IDbContextFactory<UserDbContext> UserFactory { get; }
    public string StationsDbPath { get; }

    public TestDatabase(int stationCount = 2000, int featuredEvery = 50)
    {
        _dir = Path.Combine(Path.GetTempPath(), "RadioV2Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        StationsDbPath = Path.Combine(_dir, "stations.db");
        var userDbPath = Path.Combine(_dir, "userdata.db");

        var stationsOptions = new DbContextOptionsBuilder<StationsDbContext>()
            .UseSqlite($"Data Source={StationsDbPath}")
            .LogTo(Sql.Add, Microsoft.Extensions.Logging.LogLevel.Information)
            .Options;

        var userOptions = new DbContextOptionsBuilder<UserDbContext>()
            .UseSqlite($"Data Source={userDbPath}")
            .Options;

        StationsFactory = new TestDbContextFactory<StationsDbContext>(stationsOptions);
        UserFactory = new TestDbContextFactory<UserDbContext>(userOptions);

        using (var db = StationsFactory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            Seed(db, stationCount, featuredEvery);
        }

        using (var userDb = UserFactory.CreateDbContext())
            userDb.Database.EnsureCreated();

        Service = new StationService(StationsFactory, UserFactory, new NullLogoCache());
        Sql.Clear(); // drop the setup statements so tests see only their own
    }

    private static void Seed(StationsDbContext db, int stationCount, int featuredEvery)
    {
        var group = new Group { Name = "Test Group" };
        db.Groups.Add(group);
        db.SaveChanges();

        for (int i = 0; i < stationCount; i++)
        {
            db.Stations.Add(new Station
            {
                Name = $"Station {i:D5}",
                StreamUrl = $"http://example.com/{i}",
                LogoUrl = i % 3 == 0 ? $"http://example.com/{i}.png" : null,
                GroupId = group.Id,
                IsFeatured = featuredEvery > 0 && i % featuredEvery == 0,
            });
        }

        db.SaveChanges();
    }

    /// <summary>All SQL captured so far, joined — convenient for substring assertions.</summary>
    public string CapturedSql => string.Join("\n", Sql);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp dir, best effort */ }
    }

    private sealed class TestDbContextFactory<TContext>(DbContextOptions<TContext> options)
        : IDbContextFactory<TContext> where TContext : DbContext
    {
        public TContext CreateDbContext() => (TContext)Activator.CreateInstance(typeof(TContext), options)!;
    }

    private sealed class NullLogoCache : IStationLogoCache
    {
        public string? GetCachedPath(int stationId) => null;
        public Task DownloadAsync(int stationId, string logoUrl, CancellationToken ct = default) => Task.CompletedTask;
        public void Delete(int stationId) { }
    }
}
