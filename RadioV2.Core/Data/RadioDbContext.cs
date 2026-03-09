using Microsoft.EntityFrameworkCore;
using RadioV2.Models;

namespace RadioV2.Data;

public class RadioDbContext : DbContext
{
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<Category> Categories => Set<Category>();

    public RadioDbContext(DbContextOptions<RadioDbContext> options) : base(options) { }

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
