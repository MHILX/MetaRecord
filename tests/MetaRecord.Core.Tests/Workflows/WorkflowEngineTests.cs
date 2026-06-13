using System.Text.Json;
using MetaRecord.Data;
using MetaRecord.Models;
using MetaRecord.Workflows.Definitions;
using MetaRecord.Workflows.Persistence;
using MetaRecord.Workflows.Runtime;
using Microsoft.Data.Sqlite;

namespace MetaRecord.Core.Tests.Workflows;

public sealed class WorkflowEngineTests : IDisposable
{
    public WorkflowEngineTests()
    {
        RegisterTestMetadata();
    }

    public void Dispose()
    {
        MetadataRegistry.Clear();
    }

    [Fact]
    public async Task RunAsync_executes_valid_workflow_and_persists_run_history()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = await CreateContextAsync(dbPath);
            var repository = new WorkflowRepository(context);
            var workflow = CreateCreatedWorkflow();
            await repository.SaveDefinitionAsync(workflow);
            var logExecutor = new TestNodeExecutor("action.write-log", _ => NodeExecutionResult.Succeeded("success").WithOutput("{\"written\":true}"));
            var engine = new WorkflowEngine(new[] { logExecutor }, repository);

            var result = await engine.RunAsync(workflow, CreateEvent(WorkflowEventName.Created));
            var persisted = await repository.GetRunAsync(result.RunId);

            Assert.Equal(WorkflowRunStatus.Succeeded, result.Status);
            Assert.Equal(2, result.Steps.Count);
            Assert.Contains(result.Steps, step => step.NodeId == "trigger-1" && step.Status == WorkflowRunStatus.Succeeded);
            Assert.Contains(result.Steps, step => step.NodeId == "log-1" && step.OutputJson == "{\"written\":true}");
            Assert.NotNull(persisted);
            Assert.Equal(2, persisted.Steps.Count);
            Assert.Contains(persisted.Steps, step => step.NodeId == "log-1" && step.OutputJson == "{\"written\":true}");
        }
        finally
        {
            DeleteTempDb(dbPath);
        }
    }

    [Fact]
    public async Task RunAsync_executes_condition_true_branch_only()
    {
        var trueLog = new TestNodeExecutor("action.write-log", node =>
            node.Id == "true-log" ? NodeExecutionResult.Succeeded("success") : NodeExecutionResult.Failed("Unexpected log node."));
        var condition = new TestNodeExecutor("flow.condition", _ => NodeExecutionResult.Succeeded("true"));
        var engine = new WorkflowEngine(new IWorkflowNodeExecutor[] { condition, trueLog });

        var result = await engine.RunAsync(CreateConditionWorkflow(), CreateEvent(WorkflowEventName.FieldChanged));

        Assert.Equal(WorkflowRunStatus.Succeeded, result.Status);
        Assert.Contains(result.Steps, step => step.NodeId == "true-log");
        Assert.DoesNotContain(result.Steps, step => step.NodeId == "false-log");
    }

    [Fact]
    public async Task RunAsync_executes_condition_false_branch_only()
    {
        var falseLog = new TestNodeExecutor("action.write-log", node =>
            node.Id == "false-log" ? NodeExecutionResult.Succeeded("success") : NodeExecutionResult.Failed("Unexpected log node."));
        var condition = new TestNodeExecutor("flow.condition", _ => NodeExecutionResult.Succeeded("false"));
        var engine = new WorkflowEngine(new IWorkflowNodeExecutor[] { condition, falseLog });

        var result = await engine.RunAsync(CreateConditionWorkflow(), CreateEvent(WorkflowEventName.FieldChanged));

        Assert.Equal(WorkflowRunStatus.Succeeded, result.Status);
        Assert.Contains(result.Steps, step => step.NodeId == "false-log");
        Assert.DoesNotContain(result.Steps, step => step.NodeId == "true-log");
    }

    [Fact]
    public async Task RunAsync_stop_signal_stops_current_branch()
    {
        var log = new TestNodeExecutor("action.write-log", node =>
            node.Id == "stop-1" ? NodeExecutionResult.Stopped() : NodeExecutionResult.Succeeded("success"));
        var engine = new WorkflowEngine(new[] { log });

        var result = await engine.RunAsync(CreateStopWorkflow(), CreateEvent(WorkflowEventName.Created));

        Assert.Equal(WorkflowRunStatus.Succeeded, result.Status);
        Assert.Contains(result.Steps, step => step.NodeId == "stop-1");
        Assert.DoesNotContain(result.Steps, step => step.NodeId == "log-1");
    }

    [Fact]
    public async Task RunAsync_failed_executor_records_failed_node()
    {
        var log = new TestNodeExecutor("action.write-log", _ => NodeExecutionResult.Failed("Write failed."));
        var engine = new WorkflowEngine(new[] { log });

        var result = await engine.RunAsync(CreateCreatedWorkflow(), CreateEvent(WorkflowEventName.Created));

        Assert.Equal(WorkflowRunStatus.Failed, result.Status);
        Assert.Equal("Write failed.", result.ErrorMessage);
        Assert.Contains(result.Steps, step =>
            step.NodeId == "log-1" &&
            step.Status == WorkflowRunStatus.Failed &&
            step.ErrorMessage == "Write failed.");
    }

    [Fact]
    public async Task RunAsync_executes_each_node_at_most_once()
    {
        var log = new TestNodeExecutor("action.write-log", _ => NodeExecutionResult.Succeeded("success"));
        var condition = new TestNodeExecutor("flow.condition", _ => NodeExecutionResult.Succeeded("true", "false"));
        var engine = new WorkflowEngine(new IWorkflowNodeExecutor[] { condition, log });

        var result = await engine.RunAsync(CreateConvergingWorkflow(), CreateEvent(WorkflowEventName.FieldChanged));

        Assert.Equal(WorkflowRunStatus.Succeeded, result.Status);
        Assert.Equal(1, log.ExecutionCount);
        Assert.Single(result.Steps, step => step.NodeId == "log-1");
    }

    private static WorkflowDefinition CreateCreatedWorkflow() => new()
    {
        Name = "Log todo created",
        ObjectName = "Todo",
        EventName = WorkflowEventName.Created,
        IsEnabled = true,
        Nodes = new[]
        {
            Node("trigger-1", "trigger.record-created", "{}"),
            WriteLogNode("log-1")
        },
        Edges = new[]
        {
            Edge("edge-1", "trigger-1", "success", "log-1", "input")
        }
    };

    private static WorkflowDefinition CreateConditionWorkflow() => new()
    {
        Name = "Condition branches",
        ObjectName = "Todo",
        EventName = WorkflowEventName.FieldChanged,
        IsEnabled = true,
        Nodes = new[]
        {
            Node("trigger-1", "trigger.field-changed", "{ \"fieldName\": \"Priority\" }"),
            ConditionNode("condition-1"),
            WriteLogNode("true-log"),
            WriteLogNode("false-log")
        },
        Edges = new[]
        {
            Edge("edge-1", "trigger-1", "success", "condition-1", "input"),
            Edge("edge-2", "condition-1", "true", "true-log", "input"),
            Edge("edge-3", "condition-1", "false", "false-log", "input")
        }
    };

    private static WorkflowDefinition CreateStopWorkflow() => new()
    {
        Name = "Stop branch",
        ObjectName = "Todo",
        EventName = WorkflowEventName.Created,
        IsEnabled = true,
        Nodes = new[]
        {
            Node("trigger-1", "trigger.record-created", "{}"),
            WriteLogNode("stop-1"),
            WriteLogNode("log-1")
        },
        Edges = new[]
        {
            Edge("edge-1", "trigger-1", "success", "stop-1", "input"),
            Edge("edge-2", "stop-1", "success", "log-1", "input")
        }
    };

    private static WorkflowDefinition CreateConvergingWorkflow() => new()
    {
        Name = "Converging branches",
        ObjectName = "Todo",
        EventName = WorkflowEventName.FieldChanged,
        IsEnabled = true,
        Nodes = new[]
        {
            Node("trigger-1", "trigger.field-changed", "{ \"fieldName\": \"Priority\" }"),
            ConditionNode("condition-1"),
            WriteLogNode("log-1")
        },
        Edges = new[]
        {
            Edge("edge-1", "trigger-1", "success", "condition-1", "input"),
            Edge("edge-2", "condition-1", "true", "log-1", "input"),
            Edge("edge-3", "condition-1", "false", "log-1", "input")
        }
    };

    private static WorkflowNode ConditionNode(string id) => Node(id, "flow.condition", """
    {
      "condition": {
        "left": { "source": "currentRecord", "field": "Priority" },
        "operator": "lessThan",
        "right": { "source": "literal", "value": 10 }
      }
    }
    """);

    private static WorkflowNode WriteLogNode(string id) => Node(id, "action.write-log", """
    {
      "severity": "Information",
    "message": "Todo {{currentRecord.Title}} changed."
    }
    """);

    private static WorkflowNode Node(string id, string type, string configJson) => new()
    {
        Id = id,
        Type = type,
        Config = Json(configJson)
    };

    private static WorkflowEdge Edge(
        string id,
        string fromNodeId,
        string fromPort,
        string toNodeId,
        string toPort) => new()
    {
        Id = id,
        FromNodeId = fromNodeId,
        FromPort = fromPort,
        ToNodeId = toNodeId,
        ToPort = toPort
    };

    private static WorkflowEvent CreateEvent(string eventName) => new()
    {
        ObjectName = "Todo",
        EventName = eventName,
        RecordId = Guid.NewGuid().ToString(),
        CurrentRecord = new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid(),
            ["Title"] = "Todo item",
            ["Description"] = "Sample todo",
            ["Status"] = "Open",
            ["Priority"] = 5
        },
        OriginalRecord = new Dictionary<string, object?>
        {
            ["Priority"] = 12
        },
        ChangedFields = new[] { "Priority" }
    };

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static void RegisterTestMetadata()
    {
        MetadataRegistry.Clear();
        MetadataRegistry.RegisterByName(new ObjectMetadata
        {
            Name = "Todo",
            TableName = "Todos",
            Properties = new[]
            {
                new PropertyMetadata("Id", "Id", typeof(Guid), true) { IsPrimaryKey = true },
                new PropertyMetadata("Title", "Title", typeof(string), true),
                new PropertyMetadata("Description", "Description", typeof(string), false),
                new PropertyMetadata("Status", "Status", typeof(string), true),
                new PropertyMetadata("Priority", "Priority", typeof(int), false)
            }
        });
    }

    private static string CreateTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"metarecord-{Guid.NewGuid():N}.db");

    private static async Task<MetaRecordDbContext> CreateContextAsync(string dbPath)
    {
        var context = new MetaRecordDbContext(dbPath);
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static void DeleteTempDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();

        foreach (var path in new[] { dbPath, $"{dbPath}-shm", $"{dbPath}-wal", $"{dbPath}-journal" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private sealed class TestNodeExecutor : IWorkflowNodeExecutor
    {
        private readonly Func<WorkflowNode, NodeExecutionResult> _execute;

        public TestNodeExecutor(string nodeType, Func<WorkflowNode, NodeExecutionResult> execute)
        {
            NodeType = nodeType;
            _execute = execute;
        }

        public string NodeType { get; }
        public int ExecutionCount { get; private set; }

        public Task<NodeExecutionResult> ExecuteAsync(
            WorkflowNode node,
            WorkflowExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return Task.FromResult(_execute(node));
        }
    }
}

file static class NodeExecutionResultTestExtensions
{
    public static NodeExecutionResult WithOutput(this NodeExecutionResult result, string outputJson) => new()
    {
        Status = result.Status,
        Signal = result.Signal,
        SelectedOutputPorts = result.SelectedOutputPorts,
        ErrorMessage = result.ErrorMessage,
        OutputJson = outputJson
    };
}