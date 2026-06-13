using System.Text.Json;
using MetaRecord.Workflows.Definitions;

namespace MetaRecord.Workflows.Runtime.Executors;

public sealed class StopNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => "flow.stop";

    public Task<NodeExecutionResult> ExecuteAsync(
        WorkflowNode node,
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var reasonTemplate = WorkflowValueResolver.GetOptionalString(node, "reason");
            var reason = string.IsNullOrWhiteSpace(reasonTemplate)
                ? null
                : WorkflowValueResolver.ResolveTemplate(reasonTemplate, context);
            var outputJson = reason is null ? null : JsonSerializer.Serialize(new { reason });
            return Task.FromResult(NodeExecutionResult.Stopped(outputJson));
        }
        catch (Exception ex)
        {
            return Task.FromResult(WorkflowNodeExecutorResults.Failed(ex));
        }
    }
}