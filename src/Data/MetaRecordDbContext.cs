using Microsoft.EntityFrameworkCore;
using MetaRecord.Workflows.Persistence;

namespace MetaRecord.Data;

/// <summary>
/// EF Core DbContext for metadata storage.
/// Stores object and property definitions in SQLite.
/// </summary>
public class MetaRecordDbContext : DbContext
{
    public DbSet<ObjectDefinitionEntity> ObjectDefinitions => Set<ObjectDefinitionEntity>();
    public DbSet<PropertyDefinitionEntity> PropertyDefinitions => Set<PropertyDefinitionEntity>();
    public DbSet<RelationshipDefinitionEntity> RelationshipDefinitions => Set<RelationshipDefinitionEntity>();
    public DbSet<MetadataVersionEntity> MetadataVersions => Set<MetadataVersionEntity>();
    public DbSet<WorkflowDefinitionEntity> WorkflowDefinitions => Set<WorkflowDefinitionEntity>();
    public DbSet<WorkflowRunEntity> WorkflowRuns => Set<WorkflowRunEntity>();
    public DbSet<WorkflowRunStepEntity> WorkflowRunSteps => Set<WorkflowRunStepEntity>();

    public string DbPath { get; }

    public MetaRecordDbContext()
    {
        var folder = Environment.CurrentDirectory;
        DbPath = Path.Join(folder, "metarecord.db");
    }

    public MetaRecordDbContext(string dbPath)
    {
        DbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
            options.UseSqlite($"Data Source={DbPath}");
    }

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

            entity.HasMany(e => e.Relationships)
                .WithOne(r => r.Object)
                .HasForeignKey(r => r.ObjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // PropertyDefinition configuration
        modelBuilder.Entity<PropertyDefinitionEntity>(entity =>
        {
            entity.ToTable("PropertyDefinitions", "meta");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ObjectId, e.Name }).IsUnique();
        });

        modelBuilder.Entity<RelationshipDefinitionEntity>(entity =>
        {
            entity.ToTable("RelationshipDefinitions", "meta");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.SourcePropertyName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.TargetPropertyName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.DisplayPropertyName).HasMaxLength(100);
            entity.Property(e => e.Cardinality).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(e => e.DeleteBehavior).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(e => e.Caption).HasMaxLength(250);
            entity.HasIndex(e => new { e.ObjectId, e.Name }).IsUnique();
            entity.HasIndex(e => new { e.ObjectId, e.SourcePropertyName }).IsUnique();
            entity.HasIndex(e => e.TargetObjectId);
        });

        // MetadataVersion configuration
        modelBuilder.Entity<MetadataVersionEntity>(entity =>
        {
            entity.ToTable("MetadataVersion", "meta");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<WorkflowDefinitionEntity>(entity =>
        {
            entity.ToTable("WorkflowDefinitions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ObjectName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.EventName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.DefinitionJson).IsRequired();
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => new { e.ObjectName, e.EventName, e.IsEnabled });

            entity.HasMany(e => e.Runs)
                  .WithOne(r => r.WorkflowDefinition)
                  .HasForeignKey(r => r.WorkflowId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkflowRunEntity>(entity =>
        {
            entity.ToTable("WorkflowRuns");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ObjectName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.EventName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.RecordId).HasMaxLength(100);
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
            entity.HasIndex(e => e.WorkflowId);
            entity.HasIndex(e => new { e.ObjectName, e.EventName });
            entity.HasIndex(e => e.Status);

            entity.HasMany(e => e.Steps)
                  .WithOne(s => s.Run)
                  .HasForeignKey(s => s.RunId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowRunStepEntity>(entity =>
        {
            entity.ToTable("WorkflowRunSteps");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.NodeId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.NodeType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.NodeLabel).HasMaxLength(200);
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
            entity.HasIndex(e => e.RunId);
            entity.HasIndex(e => e.NodeId);
        });
    }
}
