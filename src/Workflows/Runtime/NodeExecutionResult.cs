using MetaRecord.Workflows.Definitions;

namespace MetaRecord.Workflows.Runtime;

public sealed class NodeExecutionResult
{
    public string NodeId { get; init; } = "";
    public string NodeType { get; init; } = "";
    public string? NodeLabel { get; init; }
    public WorkflowRunStatus Status { get; init; } = WorkflowRunStatus.Succeeded;
    public WorkflowExecutionSignal Signal { get; init; } = WorkflowExecutionSignal.Continue;
    public IReadOnlyList<string> SelectedOutputPorts { get; init; } = Array.Empty<string>();
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int? DurationMs { get; init; }
    public string? InputJson { get; init; }
    public string? OutputJson { get; init; }
    public string? ErrorMessage { get; init; }

    public static NodeExecutionResult Succeeded(params string[] selectedOutputPorts) => new()
    {
        Status = WorkflowRunStatus.Succeeded,
        Signal = WorkflowExecutionSignal.Continue,
        SelectedOutputPorts = selectedOutputPorts
    };

    public static NodeExecutionResult Stopped(string? outputJson = null) => new()
    {
        Status = WorkflowRunStatus.Succeeded,
        Signal = WorkflowExecutionSignal.Stop,
        OutputJson = outputJson
    };

    public static NodeExecutionResult Canceled(string? message = null) => new()
    {
        Status = WorkflowRunStatus.Canceled,
        Signal = WorkflowExecutionSignal.Cancel,
        ErrorMessage = message
    };

    public static NodeExecutionResult Rejected(string message) => new()
    {
        Status = WorkflowRunStatus.Canceled,
        Signal = WorkflowExecutionSignal.Reject,
        ErrorMessage = message
    };

    public static NodeExecutionResult Failed(string message) => new()
    {
        Status = WorkflowRunStatus.Failed,
        Signal = WorkflowExecutionSignal.Stop,
        ErrorMessage = message
    };

    public NodeExecutionResult ForNode(WorkflowNode node, DateTime startedAt, DateTime completedAt) => new()
    {
        NodeId = node.Id,
        NodeType = node.Type,
        NodeLabel = node.Label,
        Status = Status,
        Signal = Signal,
        SelectedOutputPorts = SelectedOutputPorts,
        StartedAt = startedAt,
        CompletedAt = completedAt,
        DurationMs = (int)Math.Max(0, (completedAt - startedAt).TotalMilliseconds),
        InputJson = node.Config.ValueKind is System.Text.Json.JsonValueKind.Undefined ? null : node.Config.GetRawText(),
        OutputJson = OutputJson,
        ErrorMessage = ErrorMessage
    };
}