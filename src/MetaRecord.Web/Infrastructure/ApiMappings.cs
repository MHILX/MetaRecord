using System.Text.Json;
using MetaRecord.Models;
using MetaRecord.Web.Contracts;
using MetaRecord.Workflows.Persistence;
using MetaRecord.Workflows.Runtime;

namespace MetaRecord.Web.Infrastructure;

internal static class ApiMappings
{
    public static IObjectMetadata ToMetadata(ObjectMetadataUpsertRequest request, Guid id) => new ObjectMetadata
    {
        Id = id,
        Name = request.Name,
        TableName = request.TableName,
        Properties = (request.Properties ?? Array.Empty<PropertyMetadataUpsertRequest>())
            .Select(ToMetadata)
            .ToList()
    };

    public static ObjectMetadataResponse ToResponse(IObjectMetadata metadata) => new(
        metadata.Id,
        metadata.Name,
        metadata.TableName,
        metadata.Properties.Select(ToResponse).ToList());

    public static WorkflowRunSummaryResponse ToSummaryResponse(WorkflowRunEntity run) => new(
        run.Id,
        run.WorkflowId,
        run.WorkflowVersion,
        run.ObjectName,
        run.EventName,
        run.RecordId,
        run.Status,
        run.StartedAt,
        run.CompletedAt,
        run.DurationMs,
        run.ErrorMessage);

    public static WorkflowRunDetailResponse ToDetailResponse(WorkflowRunEntity run) => new(
        run.Id,
        run.WorkflowId,
        run.WorkflowVersion,
        run.ObjectName,
        run.EventName,
        run.RecordId,
        run.Status,
        run.StartedAt,
        run.CompletedAt,
        run.DurationMs,
        run.ErrorMessage,
        run.Steps.OrderBy(step => step.StartedAt).Select(ToResponse).ToList());

    public static WorkflowTestRunResponse ToResponse(WorkflowRunResult result) => new(
        result.RunId,
        result.WorkflowId,
        result.WorkflowVersion,
        result.ObjectName,
        result.EventName,
        result.RecordId,
        result.Status,
        result.IsRejected,
        result.StartedAt,
        result.CompletedAt,
        result.DurationMs,
        result.ErrorMessage,
        result.Steps);

    public static Dictionary<string, object?> ToObjectDictionary(Dictionary<string, JsonElement>? values)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (values is null)
            return result;

        foreach (var (key, value) in values)
            result[key] = ToObject(value);

        return result;
    }

    private static PropertyMetadata ToMetadata(PropertyMetadataUpsertRequest property)
    {
        if (!MetadataTypeMapper.TryParseClrType(property.ClrType, out var clrType))
            clrType = MetadataTypeMapper.ParseClrType(property.ClrType);

        return new PropertyMetadata(property.Name, property.ColumnName, clrType, property.IsRequired)
        {
            MaxLength = property.MaxLength,
            IsUnique = property.IsUnique,
            IsPrimaryKey = property.IsPrimaryKey,
            DefaultValue = property.DefaultValue,
            Caption = property.Caption
        };
    }

    private static PropertyMetadataResponse ToResponse(PropertyMetadata property) => new(
        property.Name,
        property.ColumnName,
        property.ClrType.Name,
        property.IsRequired,
        property.MaxLength,
        property.IsUnique,
        property.IsPrimaryKey,
        property.DefaultValue,
        property.Caption);

    private static WorkflowRunStepResponse ToResponse(WorkflowRunStepEntity step) => new(
        step.Id,
        step.NodeId,
        step.NodeType,
        step.NodeLabel,
        step.Status,
        step.StartedAt,
        step.CompletedAt,
        step.DurationMs,
        step.InputJson,
        step.OutputJson,
        step.ErrorMessage);

    private static object? ToObject(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt32(out var intValue) => intValue,
        JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
        JsonValueKind.Number when value.TryGetDecimal(out var decimalValue) => decimalValue,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(
            property => property.Name,
            property => ToObject(property.Value),
            StringComparer.OrdinalIgnoreCase),
        JsonValueKind.Array => value.EnumerateArray().Select(ToObject).ToList(),
        _ => null
    };
}