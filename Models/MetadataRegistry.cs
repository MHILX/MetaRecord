namespace MetaRecord.Models;

/// <summary>
/// Metadata registry - central repository for object definitions.
/// Enables runtime introspection and dynamic behavior.
/// Supports both type-based and name-based registration.
/// </summary>
public static class MetadataRegistry
{
    private static readonly Dictionary<Type, IObjectMetadata> _registryByType = new();
    private static readonly Dictionary<string, IObjectMetadata> _registryByName = new();

    /// <summary>
    /// Registers metadata for an entity type (compile-time known type).
    /// </summary>
    public static void Register<T>(IObjectMetadata metadata) where T : ActiveRecord<T>, new()
    {
        _registryByType[typeof(T)] = metadata;
        _registryByName[metadata.Name] = metadata;
    }

    /// <summary>
    /// Registers metadata by name only (for DB-loaded metadata).
    /// </summary>
    public static void RegisterByName(IObjectMetadata metadata)
    {
        _registryByName[metadata.Name] = metadata;
    }

    /// <summary>
    /// Links a CLR type to already-registered metadata.
    /// Call this after loading metadata from DB to enable type-based lookup.
    /// </summary>
    public static void LinkType<T>(string objectName) where T : ActiveRecord<T>, new()
    {
        if (_registryByName.TryGetValue(objectName, out var metadata))
        {
            _registryByType[typeof(T)] = metadata;
        }
    }

    /// <summary>
    /// Gets metadata for an entity type.
    /// </summary>
    public static IObjectMetadata GetMetadata<T>() => _registryByType[typeof(T)];

    /// <summary>
    /// Gets metadata by object name.
    /// </summary>
    public static IObjectMetadata GetMetadata(string objectName) => _registryByName[objectName];

    /// <summary>
    /// Tries to get metadata by name.
    /// </summary>
    public static bool TryGetMetadata(string objectName, out IObjectMetadata? metadata) =>
        _registryByName.TryGetValue(objectName, out metadata);

    /// <summary>
    /// Returns all registered metadata.
    /// </summary>
    public static IEnumerable<IObjectMetadata> GetAll() => _registryByName.Values;

    /// <summary>
    /// Clears all registered metadata.
    /// </summary>
    public static void Clear()
    {
        _registryByType.Clear();
        _registryByName.Clear();
    }
}