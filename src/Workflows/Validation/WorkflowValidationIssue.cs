namespace MetaRecord.Workflows.Validation;

public sealed record WorkflowValidationIssue(
    WorkflowValidationSeverity Severity,
    string Message,
    string? NodeId = null,
    string? Field = null);