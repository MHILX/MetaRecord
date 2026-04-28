using MetaRecord.Workflows.Definitions;

namespace MetaRecord.Workflows.Runtime;

public interface IWorkflowEngine
{
    Task<WorkflowRunResult> RunAsync(
        WorkflowDefinition workflow,
        WorkflowEvent workflowEvent,
        CancellationToken cancellationToken = default);

    Task<WorkflowRunResult> RunAsync(
        WorkflowDefinition workflow,
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default);
}