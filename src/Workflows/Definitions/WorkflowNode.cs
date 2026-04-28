using System.Text.Json;

namespace MetaRecord.Workflows.Definitions;

/// <summary>
/// A configured node instance inside a workflow definition.
/// </summary>
public sealed class WorkflowNode
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public string? Label { get; init; }
    public WorkflowPosition Position { get; init; } = new();
    public JsonElement Config { get; init; }
}