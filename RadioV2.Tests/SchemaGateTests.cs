using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RadioV2.Data;
using RadioV2.Models;
using RadioV2.Services;
using Xunit;

namespace RadioV2.Tests;

/// <summary>
/// Review Focus 1: schema init moved off the startup path, so a page can reach the
/// station service before the migration has added the IsFeatured column. On the
/// shipped seed that is harmless; for a user upgrading from the legacy
/// single-database layout it is a hard failure on the first page they open.
/// </summary>
public class SchemaGateTests : IDisposable
{
    private readonly string _dir;
    private readonly string _stationsPath;
    private readonly IDbContextFactory<StationsDbContext> _stationsFactory;
    private readonly IDbContextFactory<UserDbContext> _userFactory;

    public SchemaGateTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "RadioV2Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _stationsPath = Path.Combine(_dir, "stations.db");

        CreateLegacyStationsDb(_stationsPath);

        _stationsFactory = new Factory<StationsDbContext>(
            new DbContextOptionsBuilder<StationsDbContext>().UseSqlite($"Data Source={_stationsPath}").Options);
        _userFactory = new Factory<UserDbContext>(
            new DbContextOptionsBuilder<UserDbContext>().UseSqlite($"Data Source={Path.Combine(_dir, "userdata.db")}").Options);

        using var userDb = _userFactory.CreateDbContext();
        userDb.Database.EnsureCreated();
        userDb.Favourites.Add(new Favourite { StationId = 1 });
        userDb.SaveChanges();
    }

    /// <summary>The pre-IsFeatured schema, as shipped before the featured-stations feature.</summary>
    private static void CreateLegacyStationsDb(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE Groups (
                Id   INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT    NOT NULL UNIQUE
            );
            CREATE TABLE Stations (
                Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                Name       TEXT    NOT NULL,
                StreamUrl  TEXT    NOT NULL UNIQUE,
                LogoUrl    TEXT,
                GroupId    INTEGER NOT NULL REFERENCES Groups(Id),
                IsFavorite INTEGER NOT NULL DEFAULT 0
            );
            INSERT INTO Groups (Id, Name) VALUES (1, 'Rock');
            INSERT INTO Stations (Id, Name, StreamUrl, GroupId)
                VALUES (1, 'Legacy Station', 'http://example.com/legacy', 1);
            """;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task QueryBeforeMigration_WouldFailWithoutTheGate()
    {
        // Establishes that the hazard is real: EF selects IsFeatured, which this
        // database does not have yet.
        using var db = _stationsFactory.CreateDbContext();

        var ex = await Assert.ThrowsAsync<SqliteException>(
            () => db.Stations.AsNoTracking().ToListAsync());

        Assert.Contains("IsFeatured", ex.Message);
    }

    [Fact]
    public async Task GetFavouritesAsync_WaitsForMigration_AndSucceeds()
    {
        var gate = new SchemaGate();
        var service = new StationService(_stationsFactory, _userFactory, gate);

        // Query first, migration second — the order the app now starts in.
        var query = service.GetFavouritesAsync();

        Assert.False(query.IsCompleted, "the query should be waiting on the schema gate");

        using (var db = _stationsFactory.CreateDbContext())
            await DatabaseInitService.InitialiseAsync(db);
        gate.MarkReady();

        var favourites = await query;

        var station = Assert.Single(favourites);
        Assert.Equal("Legacy Station", station.Name);
        Assert.True(station.IsFavorite);
    }

    [Fact]
    public async Task Gate_ReleasesEvenWhenMigrationFails()
    {
        // The gate is an ordering primitive, not an error channel: never completing
        // would turn one bad migration step into a permanently hung app.
        var gate = new SchemaGate();
        var service = new StationService(_stationsFactory, _userFactory, gate);

        var query = service.GetFavouritesAsync();
        gate.MarkReady(); // as App's finally block does, migration failure or not

        await Assert.ThrowsAsync<SqliteException>(() => query);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp dir, best effort */ }
    }

    private sealed class Factory<TContext>(DbContextOptions<TContext> options)
        : IDbContextFactory<TContext> where TContext : DbContext
    {
        public TContext CreateDbContext() => (TContext)Activator.CreateInstance(typeof(TContext), options)!;
    }

}
