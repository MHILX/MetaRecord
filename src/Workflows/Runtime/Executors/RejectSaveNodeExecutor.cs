using MetaRecord.Workflows.Definitions;

namespace MetaRecord.Workflows.Runtime.Executors;

public sealed class RejectSaveNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => "action.reject-save";

    public Task<NodeExecutionResult> ExecuteAsync(
        WorkflowNode node,
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!WorkflowEventName.IsBeforeEvent(context.EventName))
                return Task.FromResult(NodeExecutionResult.Failed("Reject Save can only run during BeforeSave workflows."));

            var messageTemplate = WorkflowValueResolver.GetRequiredString(node, "message");
            var message = WorkflowValueResolver.ResolveTemplate(messageTemplate, context);
            return Task.FromResult(NodeExecutionResult.Rejected(message));
        }
        catch (Exception ex)
        {
            return Task.FromResult(WorkflowNodeExecutorResults.Failed(ex));
        }
    }
}