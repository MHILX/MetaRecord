using MetaRecord.Workflows.Definitions;

namespace MetaRecord.Workflows.Runtime.Executors;

internal static class WorkflowNodeExecutorResults
{
    public static NodeExecutionResult Succeeded(string? outputJson = null, params string[] selectedOutputPorts) => new()
    {
        Status = WorkflowRunStatus.Succeeded,
        Signal = WorkflowExecutionSignal.Continue,
        SelectedOutputPorts = selectedOutputPorts,
        OutputJson = outputJson
    };

    public static NodeExecutionResult Failed(Exception exception) => NodeExecutionResult.Failed(exception.Message);
}