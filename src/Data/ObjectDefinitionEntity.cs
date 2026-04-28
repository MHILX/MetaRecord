using System.ComponentModel.DataAnnotations;

namespace MetaRecord.Data;

/// <summary>
/// Database entity for object/entity metadata definitions.
/// Stored in meta.ObjectDefinitions table.
/// </summary>
public class ObjectDefinitionEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = "";

    [Required]
    [MaxLength(100)]
    public string TableName { get; set; } = "";

    [MaxLength(50)]
    public string SchemaName { get; set; } = "dbo";

    [MaxLength(250)]
    public string? Caption { get; set; }

    public string? Description { get; set; }

    public bool IsSystem { get; set; }

    public bool IsAbstract { get; set; }

    public Guid? SuperTypeId { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    public DateTime DateModified { get; set; } = DateTime.UtcNow;

    // Navigation property
    public List<PropertyDefinitionEntity> Properties { get; set; } = new();
}
