using MetaRecord.Data;
using MetaRecord.Models;
using Microsoft.EntityFrameworkCore;

namespace MetaRecord.Services;

/// <summary>
/// Repository for loading and saving metadata from the database.
/// Bridges the gap between DB entities and runtime metadata objects.
/// </summary>
public class MetadataRepository
{
    private readonly MetaRecordDbContext _context;

    public MetadataRepository(MetaRecordDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Loads all object metadata from the database.
    /// </summary>
    public async Task<IEnumerable<IObjectMetadata>> LoadAllMetadataAsync()
    {
        var entities = await _context.ObjectDefinitions
            .AsNoTracking()
            .Include(o => o.Properties.OrderBy(p => p.SortOrder))
            .ToListAsync();

        return entities.Select(ToObjectMetadata).ToList();
    }

    /// <summary>
    /// Gets metadata for a specific object by name.
    /// </summary>
    public async Task<IObjectMetadata?> GetByNameAsync(string name)
    {
        var entity = await _context.ObjectDefinitions
            .AsNoTracking()
            .Include(o => o.Properties.OrderBy(p => p.SortOrder))
            .FirstOrDefaultAsync(o => o.Name == name);

        return entity != null ? ToObjectMetadata(entity) : null;
    }

    /// <summary>
    /// Gets metadata for a specific object by identifier.
    /// </summary>
    public async Task<IObjectMetadata?> GetByIdAsync(Guid id)
    {
        var entity = await _context.ObjectDefinitions
            .AsNoTracking()
            .Include(o => o.Properties.OrderBy(p => p.SortOrder))
            .FirstOrDefaultAsync(o => o.Id == id);

        return entity != null ? ToObjectMetadata(entity) : null;
    }

    /// <summary>
    /// Saves object metadata to the database.
    /// </summary>
    public async Task SaveAsync(IObjectMetadata metadata)
    {
        var existing = await _context.ObjectDefinitions
            .Include(o => o.Properties)
            .FirstOrDefaultAsync(o => o.Id == metadata.Id);

        if (existing != null)
        {
            // Replace the existing definition so EF does not have to reconcile tracked child rows.
            existing.Properties.Clear();
            _context.PropertyDefinitions.RemoveRange(existing.Properties);
            _context.ObjectDefinitions.Remove(existing);
            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear();
        }

        // Insert the replacement or new definition.
        var entity = ToObjectDefinitionEntity(metadata);
        _context.ObjectDefinitions.Add(entity);

        // Bump metadata version
        _context.MetadataVersions.Add(new MetadataVersionEntity());

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Deletes metadata by identifier.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id)
    {
        var existing = await _context.ObjectDefinitions
            .Include(o => o.Properties)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (existing is null)
            return false;

        _context.PropertyDefinitions.RemoveRange(existing.Properties);
        _context.ObjectDefinitions.Remove(existing);
        _context.MetadataVersions.Add(new MetadataVersionEntity());
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Seeds initial metadata if the database is empty.
    /// </summary>
    public async Task SeedIfEmptyAsync(IEnumerable<IObjectMetadata> seedData)
    {
        if (await _context.ObjectDefinitions.AnyAsync())
            return;

        foreach (var metadata in seedData)
        {
            var entity = ToObjectDefinitionEntity(metadata);
            _context.ObjectDefinitions.Add(entity);
        }

        _context.MetadataVersions.Add(new MetadataVersionEntity());
        await _context.SaveChangesAsync();

        Console.WriteLine($"  [META] Seeded {seedData.Count()} object definitions to database");
    }

    /// <summary>
    /// Seeds any missing metadata rows by name.
    /// </summary>
    public async Task SeedMissingAsync(IEnumerable<IObjectMetadata> seedData)
    {
        var insertedCount = 0;

        foreach (var metadata in seedData)
        {
            if (await GetByNameAsync(metadata.Name) is not null)
                continue;

            var existing = await _context.ObjectDefinitions
                .Include(o => o.Properties)
                .FirstOrDefaultAsync(o => o.Id == metadata.Id);

            if (existing is not null)
            {
                _context.PropertyDefinitions.RemoveRange(existing.Properties);
                _context.ObjectDefinitions.Remove(existing);
            }

            _context.ObjectDefinitions.Add(ToObjectDefinitionEntity(metadata));
            _context.MetadataVersions.Add(new MetadataVersionEntity());
            insertedCount++;
        }

        if (insertedCount > 0)
        {
            await _context.SaveChangesAsync();
            Console.WriteLine($"  [META] Seeded {insertedCount} missing object definition(s) to database");
        }
    }

    /// <summary>
    /// Gets the current metadata version.
    /// </summary>
    public async Task<int> GetVersionAsync()
    {
        return await _context.MetadataVersions.CountAsync();
    }

    #region Mapping Methods

    private static IObjectMetadata ToObjectMetadata(ObjectDefinitionEntity entity)
    {
        return new ObjectMetadata
        {
            Id = entity.Id,
            Name = entity.Name,
            TableName = entity.TableName,
            Properties = entity.Properties
                .OrderBy(p => p.SortOrder)
                .Select(ToPropertyMetadata)
                .ToList()
        };
    }

    private static PropertyMetadata ToPropertyMetadata(PropertyDefinitionEntity entity)
    {
        var clrType = MetadataTypeMapper.ParseClrType(entity.DataType);
        return new PropertyMetadata(entity.Name, entity.ColumnName, clrType, entity.IsRequired)
        {
            MaxLength = entity.MaxLength,
            IsUnique = entity.IsUnique,
            IsPrimaryKey = entity.IsPrimaryKey,
            DefaultValue = entity.DefaultValue,
            Caption = entity.Caption
        };
    }

    private static ObjectDefinitionEntity ToObjectDefinitionEntity(IObjectMetadata metadata)
    {
        return new ObjectDefinitionEntity
        {
            Id = metadata.Id,
            Name = metadata.Name,
            TableName = metadata.TableName,
            Properties = metadata.Properties.Select((p, i) => ToPropertyEntity(p, metadata.Id, i)).ToList()
        };
    }

    private static PropertyDefinitionEntity ToPropertyEntity(PropertyMetadata prop, Guid objectId, int sortOrder)
    {
        return new PropertyDefinitionEntity
        {
            ObjectId = objectId,
            Name = prop.Name,
            ColumnName = prop.ColumnName,
            DataType = prop.ClrType.Name,
            IsRequired = prop.IsRequired,
            MaxLength = prop.MaxLength,
            IsUnique = prop.IsUnique,
            IsPrimaryKey = prop.IsPrimaryKey,
            DefaultValue = prop.DefaultValue,
            Caption = prop.Caption,
            SortOrder = sortOrder
        };
    }

    #endregion
}
