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

        // 3. Seed the 9 categories and link groups to them (skips if already done)
        await CategorySeeder.SeedAsync(db);
    }
}
