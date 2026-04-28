namespace MetaRecord.Workflows.Definitions;

/// <summary>
/// Directed connection from one node output port to another node input port.
/// </summary>
public sealed class WorkflowEdge
{
    public required string Id { get; init; }
    public required string FromNodeId { get; init; }
    public required string FromPort { get; init; }
    public required string ToNodeId { get; init; }
    public required string ToPort { get; init; }
}