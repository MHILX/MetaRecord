namespace MetaRecord.Workflows.Definitions;

/// <summary>
/// Saved workflow graph definition for one object event.
/// </summary>
public sealed class WorkflowDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public required string ObjectName { get; init; }
    public required string EventName { get; init; }
    public bool IsEnabled { get; init; }
    public int Version { get; init; } = 1;
    public required IReadOnlyList<WorkflowNode> Nodes { get; init; }
    public required IReadOnlyList<WorkflowEdge> Edges { get; init; }
}