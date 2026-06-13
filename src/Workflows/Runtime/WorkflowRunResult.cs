using MetaRecord.Workflows.Definitions;

namespace MetaRecord.Workflows.Runtime;

public sealed class WorkflowRunResult
{
    public Guid RunId { get; init; }
    public Guid WorkflowId { get; init; }
    public int WorkflowVersion { get; init; }
    public required string ObjectName { get; init; }
    public required string EventName { get; init; }
    public string? RecordId { get; init; }
    public WorkflowRunStatus Status { get; init; }
    public bool IsRejected { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int? DurationMs { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<NodeExecutionResult> Steps { get; init; } = Array.Empty<NodeExecutionResult>();
}