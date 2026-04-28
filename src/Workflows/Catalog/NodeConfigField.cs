namespace MetaRecord.Workflows.Catalog;

public sealed class NodeConfigField
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public required NodeConfigFieldKind Kind { get; init; }
    public bool IsRequired { get; init; }

    /// <summary>
    /// For property and field-mapping config, names another config field that supplies the target object name.
    /// When omitted, the workflow's target object is used.
    /// </summary>
    public string? ObjectNameField { get; init; }
}