using System.Text.Json;
using MetaRecord.Models;
using MetaRecord.Workflows.Definitions;

namespace MetaRecord.Workflows.Runtime.Executors;

public sealed class SetFieldNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => "action.set-field";

    public Task<NodeExecutionResult> ExecuteAsync(
        WorkflowNode node,
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!WorkflowEventName.IsBeforeEvent(context.EventName))
                return Task.FromResult(NodeExecutionResult.Failed("Set Field can only run during BeforeSave workflows."));

            var fieldName = WorkflowValueResolver.GetRequiredString(node, "fieldName");
            var valueConfig = WorkflowValueResolver.GetRequiredProperty(node, "value");

            if (!MetadataRegistry.TryGetMetadata(context.ObjectName, out var metadata) || metadata is null)
                return Task.FromResult(NodeExecutionResult.Failed($"Object '{context.ObjectName}' does not exist in metadata."));

            var property = WorkflowValueResolver.GetRequiredPropertyMetadata(metadata, fieldName);
            var resolvedValue = WorkflowValueResolver.ResolveConfiguredValue(valueConfig, context);
            var convertedValue = WorkflowValueResolver.ConvertToPropertyValue(resolvedValue, property);
            context.CurrentRecord[property.Name] = convertedValue;

            var outputJson = JsonSerializer.Serialize(new { fieldName = property.Name, value = convertedValue });
            return Task.FromResult(WorkflowNodeExecutorResults.Succeeded(outputJson, "success"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(WorkflowNodeExecutorResults.Failed(ex));
        }
    }
}