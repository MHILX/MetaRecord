namespace MetaRecord.Workflows.Runtime;

public interface IWorkflowEventDispatcher
{
    Task<IReadOnlyList<WorkflowRunResult>> DispatchAsync(
        WorkflowEvent workflowEvent,
        CancellationToken cancellationToken = default);
}