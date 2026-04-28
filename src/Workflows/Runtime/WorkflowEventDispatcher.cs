using MetaRecord.Workflows.Persistence;

namespace MetaRecord.Workflows.Runtime;

public sealed class WorkflowEventDispatcher : IWorkflowEventDispatcher
{
    private readonly WorkflowRepository _repository;
    private readonly IWorkflowEngine _engine;

    public WorkflowEventDispatcher(WorkflowRepository repository, IWorkflowEngine engine)
    {
        _repository = repository;
        _engine = engine;
    }

    public async Task<IReadOnlyList<WorkflowRunResult>> DispatchAsync(
        WorkflowEvent workflowEvent,
        CancellationToken cancellationToken = default)
    {
        var workflows = await _repository.GetEnabledDefinitionsAsync(
            workflowEvent.ObjectName,
            workflowEvent.EventName,
            cancellationToken);

        var results = new List<WorkflowRunResult>();
        foreach (var workflow in workflows)
        {
            results.Add(await _engine.RunAsync(workflow, workflowEvent, cancellationToken));
        }

        return results;
    }
}