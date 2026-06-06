using System.Text.Json;
using MetaRecord.Workflows.Definitions;
using MetaRecord.Workflows.Runtime;
using MetaRecord.Workflows.Validation;

namespace MetaRecord.Web.Contracts;

public sealed record ObjectMetadataUpsertRequest(
    Guid? Id,
    string Name,
    string TableName,
    IReadOnlyList<PropertyMetadataUpsertRequest> Properties);

public sealed record PropertyMetadataUpsertRequest(
    string Name,
    string ColumnName,
    string ClrType,
    bool IsRequired,
    int? MaxLength,
    bool IsUnique,
    bool IsPrimaryKey,
    string? DefaultValue,
    string? Caption);

public enum MetadataValidationSeverity
{
    Info,
    Warning,
    Error
}

public sealed record MetadataValidationIssue(
    MetadataValidationSeverity Severity,
    string Message,
    string? Field = null);

public sealed record MetadataValidationResponse(
    bool IsValid,
    IReadOnlyList<MetadataValidationIssue> Issues);

public sealed record ObjectMetadataResponse(
    Guid Id,
    string Name,
    string TableName,
    IReadOnlyList<PropertyMetadataResponse> Properties);

public sealed record PropertyMetadataResponse(
    string Name,
    string ColumnName,
    string ClrType,
    bool IsRequired,
    int? MaxLength,
    bool IsUnique,
    bool IsPrimaryKey,
    string? DefaultValue,
    string? Caption);

public sealed record WorkflowValidationResponse(
    bool IsValid,
    IReadOnlyList<WorkflowValidationIssue> Issues);

public sealed record WorkflowTestRunRequest(
    string? RecordId,
    Dictionary<string, JsonElement>? CurrentRecord,
    Dictionary<string, JsonElement>? OriginalRecord,
    IReadOnlyList<string>? ChangedFields);

public sealed record WorkflowRunSummaryResponse(
    Guid Id,
    Guid WorkflowId,
    int WorkflowVersion,
    string ObjectName,
    string EventName,
    string? RecordId,
    string Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int? DurationMs,
    string? ErrorMessage);

public sealed record WorkflowRunDetailResponse(
    Guid Id,
    Guid WorkflowId,
    int WorkflowVersion,
    string ObjectName,
    string EventName,
    string? RecordId,
    string Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int? DurationMs,
    string? ErrorMessage,
    IReadOnlyList<WorkflowRunStepResponse> Steps);

public sealed record WorkflowRunStepResponse(
    Guid Id,
    string NodeId,
    string NodeType,
    string? NodeLabel,
    string Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int? DurationMs,
    string? InputJson,
    string? OutputJson,
    string? ErrorMessage);

public sealed record WorkflowTestRunResponse(
    Guid RunId,
    Guid WorkflowId,
    int WorkflowVersion,
    string ObjectName,
    string EventName,
    string? RecordId,
    WorkflowRunStatus Status,
    bool IsRejected,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int? DurationMs,
    string? ErrorMessage,
    IReadOnlyList<NodeExecutionResult> Steps);