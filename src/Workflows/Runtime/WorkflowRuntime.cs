using MetaRecord.Data;
using MetaRecord.Workflows.Persistence;
using MetaRecord.Workflows.Runtime.Executors;

namespace MetaRecord.Workflows.Runtime;

public static class WorkflowRuntime
{
    private static readonly object SyncRoot = new();
    private static WorkflowRuntimeServices? _current;

    public static WorkflowRuntimeServices? Current
    {
        get
        {
            lock (SyncRoot)
            {
                return _current;
            }
        }
    }

    public static void Configure(
        EntityStore entityStore,
        WorkflowRepository repository,
        IWorkflowEngine? engine = null)
    {
        ArgumentNullException.ThrowIfNull(entityStore);
        ArgumentNullException.ThrowIfNull(repository);

        lock (SyncRoot)
        {
            _current = new WorkflowRuntimeServices(
                entityStore,
                repository,
                engine ?? new WorkflowEngine(CreateDefaultExecutors(entityStore), repository));
        }
    }

    public static IReadOnlyList<IWorkflowNodeExecutor> CreateDefaultExecutors(EntityStore entityStore) => new IWorkflowNodeExecutor[]
    {
        new ConditionNodeExecutor(),
        new StopNodeExecutor(),
        new SetFieldNodeExecutor(),
        new RejectSaveNodeExecutor(),
        new CreateRecordNodeExecutor(entityStore),
        new WriteLogNodeExecutor()
    };

    public static void Reset()
    {
        lock (SyncRoot)
        {
            _current = null;
        }
    }
}

public sealed class WorkflowRuntimeServices
{
    public WorkflowRuntimeServices(EntityStore entityStore, WorkflowRepository repository, IWorkflowEngine engine)
    {
        EntityStore = entityStore;
        Repository = repository;
        Engine = engine;
    }

    public EntityStore EntityStore { get; }
    public WorkflowRepository Repository { get; }
    public IWorkflowEngine Engine { get; }
}