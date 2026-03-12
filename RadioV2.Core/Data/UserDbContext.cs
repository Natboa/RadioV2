using Microsoft.EntityFrameworkCore;
using RadioV2.Models;

namespace RadioV2.Data;

/// <summary>
/// User-owned context for userdata.db — favourites and settings.
/// Created on first run via EnsureCreated(). Never overwritten by the installer.
/// </summary>
public class UserDbContext : DbContext
{
    public DbSet<Favourite> Favourites => Set<Favourite>();
    public DbSet<Setting> Settings => Set<Setting>();

    public UserDbContext(DbContextOptions<UserDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Favourite>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasIndex(f => f.StationId).IsUnique();
        });

        modelBuilder.Entity<Setting>(e =>
        {
            e.HasKey(s => s.Key);
        });
    }
}
