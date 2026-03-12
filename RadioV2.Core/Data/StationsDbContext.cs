using Microsoft.EntityFrameworkCore;
using RadioV2.Models;

namespace RadioV2.Data;

/// <summary>
/// Read-only context for the installer-managed stations database (stations.db).
/// Contains Stations, Groups, and Categories. Never touches user data.
/// </summary>
public class StationsDbContext : DbContext
{
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Category> Categories => Set<Category>();

    public StationsDbContext(DbContextOptions<StationsDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Station>(e =>
        {
            e.HasIndex(s => s.StreamUrl).IsUnique();
            e.HasIndex(s => s.Name);
            e.HasOne(s => s.Group).WithMany(g => g.Stations).HasForeignKey(s => s.GroupId);
            // IsFavorite lives in userdata.db — ignored here so EF never reads/writes that column
            e.Ignore(s => s.IsFavorite);
        });

        modelBuilder.Entity<Group>(e =>
        {
            e.HasOne(g => g.Category)
             .WithMany(c => c.Groups)
             .HasForeignKey(g => g.CategoryId)
             .IsRequired(false);
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.HasKey(c => c.Id);
        });
    }
}
