using MetaRecord.Data;

namespace MetaRecord.Models;

/// <summary>
/// Active Record base class - entity knows how to save itself.
/// Combines data access with domain logic in a single class.
/// Uses metadata-driven SQL generation for SQLite persistence.
/// </summary>
public abstract class ActiveRecord<T> where T : ActiveRecord<T>, new()
{
    private static IObjectMetadata? _metadata;

    public Guid Id { get; set; } = Guid.NewGuid();
    public bool IsNew { get; internal set; } = true;
    public bool IsDirty { get; internal set; }

    /// <summary>
    /// Gets the metadata describing this entity's structure.
    /// </summary>
    public static IObjectMetadata Metadata =>
        _metadata ??= MetadataRegistry.GetMetadata<T>();

    /// <summary>
    /// Persists the entity to the database (insert or update).
    /// Skips the write when the entity is unchanged (not new and not dirty).
    /// </summary>
    public void Save()
    {
        var store = EntityStore.Current;
        var metadata = Metadata;

        if (IsNew)
        {
            store.Insert((T)this, metadata);
            Console.WriteLine($"  [DB] Inserted {typeof(T).Name} with Id={Id}");
        }
        else if (IsDirty)
        {
            store.Update((T)this, metadata, Id);
            Console.WriteLine($"  [DB] Updated {typeof(T).Name} with Id={Id}");
        }
        else
        {
            return; // no changes, nothing to persist
        }

        IsNew = false;
        IsDirty = false;
    }

    /// <summary>
    /// Removes the entity from the database.
    /// </summary>
    public void Delete()
    {
        EntityStore.Current.Delete(Metadata, Id);
        Console.WriteLine($"  [DB] Deleted {typeof(T).Name} with Id={Id}");
    }

    /// <summary>
    /// Finds an entity by its unique identifier.
    /// </summary>
    public static T? Find(Guid id)
    {
        var entity = EntityStore.Current.Find<T>(Metadata, id);
        if (entity != null)
        {
            entity.IsNew = false;
        }
        return entity;
    }

    /// <summary>
    /// Returns all entities of this type from the database.
    /// </summary>
    public static List<T> All()
    {
        var entities = EntityStore.Current.All<T>(Metadata);
        foreach (var entity in entities)
        {
            entity.IsNew = false;
        }
        return entities;
    }

    /// <summary>
    /// Counts all entities of this type.
    /// </summary>
    public static int Count() => EntityStore.Current.Count(Metadata);

    /// <summary>
    /// Marks a property as modified.
    /// </summary>
    protected void MarkDirty() => IsDirty = true;
}