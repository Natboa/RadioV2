using Microsoft.EntityFrameworkCore;
using RadioV2.Data;

namespace RadioV2.Services;

/// <summary>
/// Applies non-destructive schema additions to the pre-seeded SQLite database,
/// then seeds category data. Safe to call on every startup.
/// </summary>
public static class DatabaseInitService
{
    public static async Task InitialiseAsync(RadioDbContext db)
    {
        // 1. Create Categories table if it doesn't exist yet
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS Categories (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                Name         TEXT    NOT NULL,
                DisplayOrder INTEGER NOT NULL DEFAULT 0
            );");

        // 2. Add CategoryId column to Groups if it doesn't exist yet
        //    SQLite throws an error if the column already exists — swallow it
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Groups ADD COLUMN CategoryId INTEGER REFERENCES Categories(Id);");
        }
        catch { /* column already exists — safe to ignore */ }

        // 3. Add IsFeatured column to Stations if it doesn't exist yet
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Stations ADD COLUMN IsFeatured INTEGER NOT NULL DEFAULT 0;");
        }
        catch { /* column already exists — safe to ignore */ }

        // 4. Index GroupId on Stations — critical for GROUP BY count performance
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS idx_stations_groupid ON Stations(GroupId);");

        // 5. If the old single "Countries & Regions" category exists, wipe all categories
        //    so SeedAsync re-runs with the new Europe / Americas / Asia split.
        bool hasOldCountries = await db.Categories.AnyAsync(c => c.Name == "Countries & Regions");
        if (hasOldCountries)
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE Groups SET CategoryId = NULL");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM Categories");
        }

        // 6. Seed the 11 categories and link groups to them (skips if already done)
        await CategorySeeder.SeedAsync(db);

        // 7. Re-apply DisplayOrder in case the category order was changed after initial seeding
        var orderMap = new (string Name, int Order)[]
        {
            ("Rock & Metal",               0),
            ("Electronic & Dance",         1),
            ("Pop, Charts & Decades",      2),
            ("Urban & Latin",              3),
            ("Jazz, Chill & Instrumental", 4),
            ("News, Talk & Sports",        5),
            ("Specialty & Mood",           6),
            ("Global & Cultural",          7),
            ("Europe",                     8),
            ("Americas",                   9),
            ("Asia, Pacific & Africa",    10),
        };
        foreach (var (name, order) in orderMap)
            await db.Database.ExecuteSqlRawAsync(
                $"UPDATE Categories SET DisplayOrder = {order} WHERE Name = '{name}'");
    }
}
