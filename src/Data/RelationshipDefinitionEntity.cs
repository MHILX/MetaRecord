using System.ComponentModel.DataAnnotations;
using MetaRecord.Models;

namespace MetaRecord.Data;

/// <summary>
/// Database entity for metadata relationships between objects.
/// Stored in the metadata database.
/// </summary>
public class RelationshipDefinitionEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ObjectId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string SourcePropertyName { get; set; } = string.Empty;

    public Guid TargetObjectId { get; set; }

    [Required]
    [MaxLength(100)]
    public string TargetObjectName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string TargetPropertyName { get; set; } = "Id";

    [MaxLength(100)]
    public string? DisplayPropertyName { get; set; }

    public RelationshipCardinality Cardinality { get; set; } = RelationshipCardinality.ManyToOne;

    public RelationshipDeleteBehavior DeleteBehavior { get; set; } = RelationshipDeleteBehavior.Restrict;

    [MaxLength(250)]
    public string? Caption { get; set; }

    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    public DateTime DateModified { get; set; } = DateTime.UtcNow;

    // Navigation property
    public ObjectDefinitionEntity? Object { get; set; }
}