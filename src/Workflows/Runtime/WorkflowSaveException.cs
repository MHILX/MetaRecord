namespace MetaRecord.Workflows.Runtime;

public class WorkflowSaveException : InvalidOperationException
{
    public WorkflowSaveException(string message, IReadOnlyList<WorkflowRunResult> workflowResults)
        : base(message)
    {
        WorkflowResults = workflowResults;
    }

    public IReadOnlyList<WorkflowRunResult> WorkflowResults { get; }
}

public sealed class WorkflowSaveRejectedException : WorkflowSaveException
{
    public WorkflowSaveRejectedException(string message, IReadOnlyList<WorkflowRunResult> workflowResults)
        : base(message, workflowResults)
    {
    }
}

public sealed class WorkflowSaveFailedException : WorkflowSaveException
{
    public WorkflowSaveFailedException(string message, IReadOnlyList<WorkflowRunResult> workflowResults)
        : base(message, workflowResults)
    {
    }
}