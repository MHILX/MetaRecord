namespace MetaRecord.Workflows.Persistence;

public sealed class WorkflowRunEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public int WorkflowVersion { get; set; }
    public string ObjectName { get; set; } = "";
    public string EventName { get; set; } = "";
    public string? RecordId { get; set; }
    public string Status { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int? DurationMs { get; set; }
    public string? ErrorMessage { get; set; }

    public WorkflowDefinitionEntity? WorkflowDefinition { get; set; }
    public List<WorkflowRunStepEntity> Steps { get; set; } = new();
}