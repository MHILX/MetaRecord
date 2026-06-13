namespace MetaRecord.Models;

public enum RelationshipCardinality
{
    ManyToOne,
    OneToOne,
    OneToMany,
    ManyToMany
}

public enum RelationshipDeleteBehavior
{
    Restrict,
    SetNull,
    Cascade,
    NoAction
}

public record RelationshipMetadata(
    string Name,
    string SourcePropertyName,
    Guid TargetObjectId)
{
    public string? TargetObjectName { get; init; }

    public string TargetPropertyName { get; init; } = "Id";

    public RelationshipCardinality Cardinality { get; init; } = RelationshipCardinality.ManyToOne;

    public RelationshipDeleteBehavior DeleteBehavior { get; init; } = RelationshipDeleteBehavior.Restrict;

    public string? DisplayPropertyName { get; init; }

    public string? Caption { get; init; }

    public string? Description { get; init; }
}