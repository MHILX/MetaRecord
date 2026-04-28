namespace MetaRecord.Workflows.Runtime;

public sealed class WorkflowEvent
{
    public required string ObjectName { get; init; }
    public required string EventName { get; init; }
    public string? RecordId { get; init; }
    public IReadOnlyDictionary<string, object?> CurrentRecord { get; init; } = new Dictionary<string, object?>();
    public IReadOnlyDictionary<string, object?> OriginalRecord { get; init; } = new Dictionary<string, object?>();
    public IReadOnlyList<string> ChangedFields { get; init; } = Array.Empty<string>();
}