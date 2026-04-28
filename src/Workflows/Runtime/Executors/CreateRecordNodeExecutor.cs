using System.Text.Json;
using MetaRecord.Data;
using MetaRecord.Models;
using MetaRecord.Workflows.Definitions;

namespace MetaRecord.Workflows.Runtime.Executors;

public sealed class CreateRecordNodeExecutor : IWorkflowNodeExecutor
{
    private readonly EntityStore _entityStore;

    public CreateRecordNodeExecutor(EntityStore? entityStore = null)
    {
        _entityStore = entityStore ?? EntityStore.Current;
    }

    public string NodeType => "action.create-record";

    public Task<NodeExecutionResult> ExecuteAsync(
        WorkflowNode node,
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var targetObjectName = WorkflowValueResolver.GetRequiredString(node, "targetObjectName");
            if (!MetadataRegistry.TryGetMetadata(targetObjectName, out var metadata) || metadata is null)
                return Task.FromResult(NodeExecutionResult.Failed($"Object '{targetObjectName}' does not exist in metadata."));

            var fieldMappings = WorkflowValueResolver.GetRequiredProperty(node, "fieldMappings");
            if (fieldMappings.ValueKind != JsonValueKind.Object)
                return Task.FromResult(NodeExecutionResult.Failed("Config field 'fieldMappings' must be an object."));

            var values = ResolveFieldMappings(metadata, fieldMappings, context);
            EnsurePrimaryKeyValue(metadata, values);

            _entityStore.EnsureTableExists(metadata);
            _entityStore.InsertValues(metadata, values);

            var recordId = WorkflowValueResolver.GetPrimaryKeyProperty(metadata) is { } primaryKey && values.TryGetValue(primaryKey.Name, out var id)
                ? id
                : null;
            var outputJson = JsonSerializer.Serialize(new { targetObjectName = metadata.Name, recordId, values });
            return Task.FromResult(WorkflowNodeExecutorResults.Succeeded(outputJson, "success"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(WorkflowNodeExecutorResults.Failed(ex));
        }
    }

    private static Dictionary<string, object?> ResolveFieldMappings(
        IObjectMetadata metadata,
        JsonElement fieldMappings,
        WorkflowExecutionContext context)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in fieldMappings.EnumerateObject())
        {
            var property = WorkflowValueResolver.GetRequiredPropertyMetadata(metadata, mapping.Name);
            var resolvedValue = WorkflowValueResolver.ResolveConfiguredValue(mapping.Value, context);
            values[property.Name] = WorkflowValueResolver.ConvertToPropertyValue(resolvedValue, property);
        }

        return values;
    }

    private static void EnsurePrimaryKeyValue(IObjectMetadata metadata, IDictionary<string, object?> values)
    {
        var primaryKey = WorkflowValueResolver.GetPrimaryKeyProperty(metadata);
        if (primaryKey is null || values.ContainsKey(primaryKey.Name) || primaryKey.ClrType != typeof(Guid))
            return;

        values[primaryKey.Name] = Guid.NewGuid();
    }
}