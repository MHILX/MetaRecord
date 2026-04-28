using MetaRecord.Workflows.Definitions;

namespace MetaRecord.Workflows.Runtime.Executors;

public sealed class ConditionNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => "flow.condition";

    public Task<NodeExecutionResult> ExecuteAsync(
        WorkflowNode node,
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var condition = WorkflowValueResolver.GetRequiredProperty(node, "condition");
            var branch = WorkflowConditionEvaluator.Evaluate(condition, context) ? "true" : "false";
            return Task.FromResult(WorkflowNodeExecutorResults.Succeeded($"{{\"result\":\"{branch}\"}}", branch));
        }
        catch (Exception ex)
        {
            return Task.FromResult(WorkflowNodeExecutorResults.Failed(ex));
        }
    }
}