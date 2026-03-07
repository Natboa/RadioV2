using Microsoft.EntityFrameworkCore;
using RadioV2.Models;

namespace RadioV2.Data;

public class RadioDbContext : DbContext
{
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Setting> Settings => Set<Setting>();

    public RadioDbContext(DbContextOptions<RadioDbContext> options) : base(options)
    {
        // WAL mode improves concurrent read performance on the large SQLite database
        Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Station>(e =>
        {
            e.HasIndex(s => s.StreamUrl).IsUnique();
            e.HasIndex(s => s.Name);
            e.HasOne(s => s.Group).WithMany(g => g.Stations).HasForeignKey(s => s.GroupId);
        });

        modelBuilder.Entity<Setting>(e =>
        {
            e.HasKey(s => s.Key);
        });
    }
}
