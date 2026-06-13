namespace MetaRecord.Workflows.Persistence;

public sealed class WorkflowDefinitionEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ObjectName { get; set; } = "";
    public string EventName { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string DefinitionJson { get; set; } = "";
    public int Version { get; set; } = 1;
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    public DateTime DateModified { get; set; } = DateTime.UtcNow;

    public List<WorkflowRunEntity> Runs { get; set; } = new();
}