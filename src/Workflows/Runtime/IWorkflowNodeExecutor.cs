using MetaRecord.Workflows.Definitions;

namespace MetaRecord.Workflows.Runtime;

public interface IWorkflowNodeExecutor
{
    string NodeType { get; }

    Task<NodeExecutionResult> ExecuteAsync(
        WorkflowNode node,
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default);
}