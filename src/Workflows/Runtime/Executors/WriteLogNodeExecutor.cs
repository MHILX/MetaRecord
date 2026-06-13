using System.Text.Json;
using MetaRecord.Workflows.Definitions;

namespace MetaRecord.Workflows.Runtime.Executors;

public sealed class WriteLogNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => "action.write-log";

    public Task<NodeExecutionResult> ExecuteAsync(
        WorkflowNode node,
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var severity = WorkflowValueResolver.GetRequiredString(node, "severity");
            var messageTemplate = WorkflowValueResolver.GetRequiredString(node, "message");
            var message = WorkflowValueResolver.ResolveTemplate(messageTemplate, context);
            var outputJson = JsonSerializer.Serialize(new
            {
                severity,
                message,
                writtenAt = DateTime.UtcNow
            });

            return Task.FromResult(WorkflowNodeExecutorResults.Succeeded(outputJson, "success"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(WorkflowNodeExecutorResults.Failed(ex));
        }
    }
}