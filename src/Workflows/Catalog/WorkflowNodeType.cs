using MetaRecord.Workflows.Definitions;

namespace MetaRecord.Workflows.Catalog;

public sealed class WorkflowNodeType
{
    public required string Type { get; init; }
    public required WorkflowNodeCategory Category { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public WorkflowNodeTiming Timing { get; init; } = WorkflowNodeTiming.Any;
    public string? TriggerEventName { get; init; }
    public IReadOnlyList<NodePortDefinition> InputPorts { get; init; } = Array.Empty<NodePortDefinition>();
    public IReadOnlyList<NodePortDefinition> OutputPorts { get; init; } = Array.Empty<NodePortDefinition>();
    public NodeConfigSchema ConfigSchema { get; init; } = NodeConfigSchema.Empty;

    public bool IsTrigger => Category == WorkflowNodeCategory.Trigger;

    public bool IsAllowedForEvent(string eventName) => Timing switch
    {
        WorkflowNodeTiming.Any => true,
        WorkflowNodeTiming.BeforeOnly => WorkflowEventName.IsBeforeEvent(eventName),
        WorkflowNodeTiming.AfterOnly => WorkflowEventName.IsAfterEvent(eventName),
        WorkflowNodeTiming.ManualOnly => WorkflowEventName.IsManualEvent(eventName),
        _ => false
    };
}