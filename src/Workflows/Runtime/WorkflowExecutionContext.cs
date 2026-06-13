using MetaRecord.Workflows.Definitions;

namespace MetaRecord.Workflows.Runtime;

public sealed class WorkflowExecutionContext
{
    public Guid RunId { get; init; } = Guid.NewGuid();
    public Guid WorkflowId { get; init; }
    public int WorkflowVersion { get; init; }
    public required string ObjectName { get; init; }
    public required string EventName { get; init; }
    public string? RecordId { get; init; }
    public Dictionary<string, object?> CurrentRecord { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object?> OriginalRecord { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> ChangedFields { get; init; } = Array.Empty<string>();
    public Dictionary<string, object?> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static WorkflowExecutionContext FromEvent(WorkflowDefinition workflow, WorkflowEvent workflowEvent)
    {
        return new WorkflowExecutionContext
        {
            WorkflowId = workflow.Id,
            WorkflowVersion = workflow.Version,
            ObjectName = workflowEvent.ObjectName,
            EventName = workflowEvent.EventName,
            RecordId = workflowEvent.RecordId,
            CurrentRecord = new Dictionary<string, object?>(workflowEvent.CurrentRecord, StringComparer.OrdinalIgnoreCase),
            OriginalRecord = new Dictionary<string, object?>(workflowEvent.OriginalRecord, StringComparer.OrdinalIgnoreCase),
            ChangedFields = workflowEvent.ChangedFields.ToList()
        };
    }
}