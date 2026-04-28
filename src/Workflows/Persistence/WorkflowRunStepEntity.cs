namespace MetaRecord.Workflows.Persistence;

public sealed class WorkflowRunStepEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public string NodeId { get; set; } = "";
    public string NodeType { get; set; } = "";
    public string? NodeLabel { get; set; }
    public string Status { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int? DurationMs { get; set; }
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? ErrorMessage { get; set; }

    public WorkflowRunEntity? Run { get; set; }
}