namespace MetaRecord.Workflows.Catalog;

public sealed class NodeConfigSchema
{
    public static NodeConfigSchema Empty { get; } = new();

    public IReadOnlyList<NodeConfigField> Fields { get; init; } = Array.Empty<NodeConfigField>();
}