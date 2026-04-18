namespace MetaRecord.Models;

/// <summary>
/// Defines the contract for object metadata - describes an entity's structure at runtime.
/// </summary>
public interface IObjectMetadata
{
    Guid Id { get; }
    string Name { get; }
    string TableName { get; }
    IReadOnlyList<PropertyMetadata> Properties { get; }
}

/// <summary>
/// Default implementation of object metadata.
/// </summary>
public class ObjectMetadata : IObjectMetadata
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public required string TableName { get; init; }
    public required IReadOnlyList<PropertyMetadata> Properties { get; init; }
}

/// <summary>
/// Describes a single property of an entity.
/// </summary>
public record PropertyMetadata(
    string Name,
    string ColumnName,
    Type ClrType,
    bool IsRequired)
{
    /// <summary>Optional maximum length for string columns.</summary>
    public int? MaxLength { get; init; }

    /// <summary>Whether the column has a UNIQUE constraint.</summary>
    public bool IsUnique { get; init; }

    /// <summary>Whether this property is the primary key. If no property sets this, <c>Id</c> is used.</summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary>Optional default value expression emitted into the CREATE TABLE statement.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Human-readable caption for UI generation.</summary>
    public string? Caption { get; init; }
}

/// <summary>
/// Attribute to specify the database table name for an entity.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class TableAttribute : Attribute
{
    public string Name { get; }
    public TableAttribute(string name) => Name = name;
}
