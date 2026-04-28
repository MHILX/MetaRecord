using System.ComponentModel.DataAnnotations;

namespace MetaRecord.Data;

/// <summary>
/// Database entity for property metadata definitions.
/// Stored in meta.PropertyDefinitions table.
/// </summary>
public class PropertyDefinitionEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ObjectId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = "";

    [Required]
    [MaxLength(100)]
    public string ColumnName { get; set; } = "";

    [MaxLength(250)]
    public string? Caption { get; set; }

    [Required]
    [MaxLength(50)]
    public string DataType { get; set; } = "String";  // CLR type name

    public int? MaxLength { get; set; }

    public int? Precision { get; set; }

    public int? Scale { get; set; }

    public bool IsRequired { get; set; }

    public bool IsUnique { get; set; }

    public bool IsPrimaryKey { get; set; }

    public string? DefaultValue { get; set; }

    public int SortOrder { get; set; }

    // Navigation property
    public ObjectDefinitionEntity? Object { get; set; }
}
