using Microsoft.EntityFrameworkCore;

namespace MetaRecord.Data;

/// <summary>
/// EF Core DbContext for metadata storage.
/// Stores object and property definitions in SQLite.
/// </summary>
public class MetaRecordDbContext : DbContext
{
    public DbSet<ObjectDefinitionEntity> ObjectDefinitions => Set<ObjectDefinitionEntity>();
    public DbSet<PropertyDefinitionEntity> PropertyDefinitions => Set<PropertyDefinitionEntity>();
    public DbSet<MetadataVersionEntity> MetadataVersions => Set<MetadataVersionEntity>();

    public string DbPath { get; }

    public MetaRecordDbContext()
    {
        var folder = Environment.CurrentDirectory;
        DbPath = Path.Join(folder, "metarecord.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ObjectDefinition configuration
        modelBuilder.Entity<ObjectDefinitionEntity>(entity =>
        {
            entity.ToTable("ObjectDefinitions", "meta");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            
            entity.HasMany(e => e.Properties)
                  .WithOne(p => p.Object)
                  .HasForeignKey(p => p.ObjectId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // PropertyDefinition configuration
        modelBuilder.Entity<PropertyDefinitionEntity>(entity =>
        {
            entity.ToTable("PropertyDefinitions", "meta");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ObjectId, e.Name }).IsUnique();
        });

        // MetadataVersion configuration
        modelBuilder.Entity<MetadataVersionEntity>(entity =>
        {
            entity.ToTable("MetadataVersion", "meta");
            entity.HasKey(e => e.Id);
        });
    }
}
