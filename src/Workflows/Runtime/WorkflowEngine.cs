using MetaRecord.Workflows.Catalog;
using MetaRecord.Workflows.Definitions;
using MetaRecord.Workflows.Persistence;
using MetaRecord.Workflows.Validation;

namespace MetaRecord.Workflows.Runtime;

public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly Dictionary<string, IWorkflowNodeExecutor> _executors;
    private readonly WorkflowNodeCatalog _catalog;
    private readonly WorkflowValidator _validator;
    private readonly WorkflowRepository? _repository;

    public WorkflowEngine(
        IEnumerable<IWorkflowNodeExecutor> nodeExecutors,
        WorkflowRepository? repository = null,
        WorkflowNodeCatalog? catalog = null,
        WorkflowValidator? validator = null)
    {
        _executors = nodeExecutors.ToDictionary(executor => executor.NodeType, StringComparer.OrdinalIgnoreCase);
        _repository = repository;
        _catalog = catalog ?? WorkflowNodeCatalog.Default;
        _validator = validator ?? new WorkflowValidator(_catalog);
    }

    public Task<WorkflowRunResult> RunAsync(
        WorkflowDefinition workflow,
        WorkflowEvent workflowEvent,
        CancellationToken cancellationToken = default) =>
        RunAsync(workflow, WorkflowExecutionContext.FromEvent(workflow, workflowEvent), cancellationToken);

    public async Task<WorkflowRunResult> RunAsync(
        WorkflowDefinition workflow,
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(context);

        var startedAt = DateTime.UtcNow;
        var validationErrors = _validator.Validate(workflow)
            .Where(issue => issue.Severity == WorkflowValidationSeverity.Error)
            .ToList();

        if (validationErrors.Count > 0)
        {
            var result = CreateFailedRunResult(workflow, context, startedAt, string.Join(" ", validationErrors.Select(issue => issue.Message)));
            await PersistAsync(result, cancellationToken);
            return result;
        }

        var nodesById = workflow.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        var triggerNode = workflow.Nodes.Single(node => _catalog.TryGet(node.Type, out var nodeType) && nodeType?.IsTrigger == true);
        var outgoingEdgesByNodeId = workflow.Edges
            .GroupBy(edge => edge.FromNodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var steps = new List<NodeExecutionResult>();
        var executedNodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingNodeIds = new Queue<string>();
        pendingNodeIds.Enqueue(triggerNode.Id);

        while (pendingNodeIds.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nodeId = pendingNodeIds.Dequeue();
            if (!nodesById.TryGetValue(nodeId, out var node) || !executedNodeIds.Add(nodeId))
                continue;

            var step = await ExecuteNodeAsync(node, context, cancellationToken);
            steps.Add(step);

            if (step.Status != WorkflowRunStatus.Succeeded || step.Signal != WorkflowExecutionSignal.Continue)
                continue;

            if (step.SelectedOutputPorts.Count == 0)
                continue;

            if (!outgoingEdgesByNodeId.TryGetValue(node.Id, out var outgoingEdges))
                continue;

            foreach (var edge in outgoingEdges.Where(edge => ContainsPort(step.SelectedOutputPorts, edge.FromPort)))
                pendingNodeIds.Enqueue(edge.ToNodeId);
        }

        var completedAt = DateTime.UtcNow;
        var runResult = CreateRunResult(workflow, context, steps, startedAt, completedAt);
        await PersistAsync(runResult, cancellationToken);
        return runResult;
    }

    private async Task<NodeExecutionResult> ExecuteNodeAsync(
        WorkflowNode node,
        WorkflowExecutionContext context,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;

        if (_catalog.TryGet(node.Type, out var nodeType) && nodeType?.IsTrigger == true)
        {
            return NodeExecutionResult.Succeeded("success").ForNode(node, startedAt, DateTime.UtcNow);
        }

        if (!_executors.TryGetValue(node.Type, out var executor))
        {
            return NodeExecutionResult.Failed($"No executor is registered for node type '{node.Type}'.")
                .ForNode(node, startedAt, DateTime.UtcNow);
        }

        try
        {
            var executorResult = await executor.ExecuteAsync(node, context, cancellationToken);
            return executorResult.ForNode(node, startedAt, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            return NodeExecutionResult.Failed(ex.Message).ForNode(node, startedAt, DateTime.UtcNow);
        }
    }

    private static WorkflowRunResult CreateRunResult(
        WorkflowDefinition workflow,
        WorkflowExecutionContext context,
        IReadOnlyList<NodeExecutionResult> steps,
        DateTime startedAt,
        DateTime completedAt)
    {
        var isRejected = steps.Any(step => step.Signal == WorkflowExecutionSignal.Reject);
        var status = GetRunStatus(steps, isRejected);
        var errorMessage = steps.FirstOrDefault(step => !string.IsNullOrWhiteSpace(step.ErrorMessage))?.ErrorMessage;

        return new WorkflowRunResult
        {
            RunId = context.RunId,
            WorkflowId = workflow.Id,
            WorkflowVersion = workflow.Version,
            ObjectName = context.ObjectName,
            EventName = context.EventName,
            RecordId = context.RecordId,
            Status = status,
            IsRejected = isRejected,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            DurationMs = (int)Math.Max(0, (completedAt - startedAt).TotalMilliseconds),
            ErrorMessage = errorMessage,
            Steps = steps
        };
    }

    private static WorkflowRunResult CreateFailedRunResult(
        WorkflowDefinition workflow,
        WorkflowExecutionContext context,
        DateTime startedAt,
        string errorMessage)
    {
        var completedAt = DateTime.UtcNow;
        return new WorkflowRunResult
        {
            RunId = context.RunId,
            WorkflowId = workflow.Id,
            WorkflowVersion = workflow.Version,
            ObjectName = context.ObjectName,
            EventName = context.EventName,
            RecordId = context.RecordId,
            Status = WorkflowRunStatus.Failed,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            DurationMs = (int)Math.Max(0, (completedAt - startedAt).TotalMilliseconds),
            ErrorMessage = errorMessage
        };
    }

    private static WorkflowRunStatus GetRunStatus(IReadOnlyList<NodeExecutionResult> steps, bool isRejected)
    {
        if (steps.Count == 0)
            return WorkflowRunStatus.Skipped;
        if (steps.Any(step => step.Status == WorkflowRunStatus.Failed))
            return WorkflowRunStatus.Failed;
        if (isRejected || steps.Any(step => step.Status == WorkflowRunStatus.Canceled || step.Signal == WorkflowExecutionSignal.Cancel))
            return WorkflowRunStatus.Canceled;
        return WorkflowRunStatus.Succeeded;
    }

    private async Task PersistAsync(WorkflowRunResult result, CancellationToken cancellationToken)
    {
        if (_repository is null)
            return;

        await _repository.SaveRunAsync(ToRunEntity(result), cancellationToken);
    }

    private static WorkflowRunEntity ToRunEntity(WorkflowRunResult result) => new()
    {
        Id = result.RunId,
        WorkflowId = result.WorkflowId,
        WorkflowVersion = result.WorkflowVersion,
        ObjectName = result.ObjectName,
        EventName = result.EventName,
        RecordId = result.RecordId,
        Status = result.Status.ToString(),
        StartedAt = result.StartedAt,
        CompletedAt = result.CompletedAt,
        DurationMs = result.DurationMs,
        ErrorMessage = result.ErrorMessage,
        Steps = result.Steps.Select(ToStepEntity).ToList()
    };

    private static WorkflowRunStepEntity ToStepEntity(NodeExecutionResult step) => new()
    {
        NodeId = step.NodeId,
        NodeType = step.NodeType,
        NodeLabel = step.NodeLabel,
        Status = step.Status.ToString(),
        StartedAt = step.StartedAt,
        CompletedAt = step.CompletedAt,
        DurationMs = step.DurationMs,
        InputJson = step.InputJson,
        OutputJson = step.OutputJson,
        ErrorMessage = step.ErrorMessage
    };

    private static bool ContainsPort(IReadOnlyList<string> selectedPorts, string portName) =>
        selectedPorts.Any(selectedPort => string.Equals(selectedPort, portName, StringComparison.OrdinalIgnoreCase));
}