using System.Text.Json;
using MetaRecord.Data;
using MetaRecord.Workflows.Definitions;
using MetaRecord.Workflows.Persistence;
using Microsoft.Data.Sqlite;

namespace MetaRecord.Core.Tests.Workflows;

public sealed class WorkflowRepositoryTests
{
    [Fact]
    public async Task SaveDefinitionAsync_round_trips_workflow_definition_json()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = await CreateContextAsync(dbPath);
            var repository = new WorkflowRepository(context);
            var workflow = CreateBeforeSaveWorkflow(isEnabled: true);

            await repository.SaveDefinitionAsync(workflow);

            var loaded = await repository.GetDefinitionAsync(workflow.Id);
            Assert.NotNull(loaded);
            Assert.Equal(workflow.Id, loaded.Id);
            Assert.Equal("Reject empty todo title", loaded.Name);
            Assert.Equal("Todo", loaded.ObjectName);
            Assert.Equal(WorkflowEventName.BeforeSave, loaded.EventName);
            Assert.True(loaded.IsEnabled);
            Assert.Equal(1, loaded.Version);
            Assert.Equal(2, loaded.Nodes.Count);
            Assert.Single(loaded.Edges);

            var logNode = Assert.Single(loaded.Nodes, node => node.Id == "log-1");
            Assert.True(logNode.Config.TryGetProperty("message", out var message));
            Assert.Equal("Todo {{currentRecord.Title}} saved.", message.GetString());
        }
        finally
        {
            DeleteTempDb(dbPath);
        }
    }

    [Fact]
    public async Task GetEnabledDefinitionsAsync_filters_by_enabled_object_and_event()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = await CreateContextAsync(dbPath);
            var repository = new WorkflowRepository(context);
            var matching = CreateFieldChangedWorkflow("Matching enabled", isEnabled: true);
            var disabled = CreateFieldChangedWorkflow("Matching disabled", isEnabled: false);
            var otherEvent = CreateBeforeSaveWorkflow(isEnabled: true);

            await repository.SaveDefinitionAsync(matching);
            await repository.SaveDefinitionAsync(disabled);
            await repository.SaveDefinitionAsync(otherEvent);

            var enabled = await repository.GetEnabledDefinitionsAsync("Todo", WorkflowEventName.FieldChanged);

            var workflow = Assert.Single(enabled);
            Assert.Equal(matching.Id, workflow.Id);
            Assert.Equal(WorkflowEventName.FieldChanged, workflow.EventName);
            Assert.True(workflow.IsEnabled);
        }
        finally
        {
            DeleteTempDb(dbPath);
        }
    }

    [Fact]
    public async Task EnableAsync_and_DisableAsync_toggle_workflow_enabled_state()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = await CreateContextAsync(dbPath);
            var repository = new WorkflowRepository(context);
            var workflow = CreateFieldChangedWorkflow("Toggle me", isEnabled: false);
            await repository.SaveDefinitionAsync(workflow);

            var enabled = await repository.EnableAsync(workflow.Id);
            var enabledWorkflow = await repository.GetDefinitionAsync(workflow.Id);

            var disabled = await repository.DisableAsync(workflow.Id);
            var disabledWorkflow = await repository.GetDefinitionAsync(workflow.Id);

            Assert.True(enabled);
            Assert.True(enabledWorkflow?.IsEnabled);
            Assert.True(disabled);
            Assert.False(disabledWorkflow?.IsEnabled);
        }
        finally
        {
            DeleteTempDb(dbPath);
        }
    }

    [Fact]
    public async Task SaveRunAsync_persists_run_with_step_details()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = await CreateContextAsync(dbPath);
            var repository = new WorkflowRepository(context);
            var workflow = CreateFieldChangedWorkflow("Run source", isEnabled: true);
            await repository.SaveDefinitionAsync(workflow);

            var run = new WorkflowRunEntity
            {
                WorkflowId = workflow.Id,
                WorkflowVersion = workflow.Version,
                ObjectName = workflow.ObjectName,
                EventName = workflow.EventName,
                RecordId = Guid.NewGuid().ToString(),
                Status = WorkflowRunStatus.Succeeded.ToString(),
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow.AddMilliseconds(15),
                DurationMs = 15,
                Steps = new List<WorkflowRunStepEntity>
                {
                    new()
                    {
                        NodeId = "trigger-1",
                        NodeType = "trigger.field-changed",
                        NodeLabel = "Quantity changed",
                        Status = WorkflowRunStatus.Succeeded.ToString(),
                        StartedAt = DateTime.UtcNow,
                        CompletedAt = DateTime.UtcNow.AddMilliseconds(5),
                        DurationMs = 5,
                        OutputJson = "{\"fieldName\":\"Quantity\"}"
                    },
                    new()
                    {
                        NodeId = "log-1",
                        NodeType = "action.write-log",
                        NodeLabel = "Write log",
                        Status = WorkflowRunStatus.Succeeded.ToString(),
                        StartedAt = DateTime.UtcNow,
                        CompletedAt = DateTime.UtcNow.AddMilliseconds(10),
                        DurationMs = 10,
                        InputJson = "{\"message\":\"Quantity changed\"}",
                        OutputJson = "{\"written\":true}"
                    }
                }
            };

            await repository.SaveRunAsync(run);

            var loaded = await repository.GetRunAsync(run.Id);
            Assert.NotNull(loaded);
            Assert.Equal(workflow.Id, loaded.WorkflowId);
            Assert.Equal(WorkflowRunStatus.Succeeded.ToString(), loaded.Status);
            Assert.Equal(2, loaded.Steps.Count);
            Assert.Contains(loaded.Steps, step => step.NodeId == "trigger-1" && step.OutputJson == "{\"fieldName\":\"Quantity\"}");
            Assert.Contains(loaded.Steps, step => step.NodeId == "log-1" && step.OutputJson == "{\"written\":true}");
        }
        finally
        {
            DeleteTempDb(dbPath);
        }
    }

    private static WorkflowDefinition CreateBeforeSaveWorkflow(bool isEnabled)
    {
        return new WorkflowDefinition
        {
                        Name = "Reject empty todo title",
                        ObjectName = "Todo",
            EventName = WorkflowEventName.BeforeSave,
            IsEnabled = isEnabled,
            Version = 1,
            Nodes = new[]
            {
                Node("trigger-1", "trigger.before-save", "{}"),
                Node("log-1", "action.write-log", """
                {
                  "severity": "Information",
                                    "message": "Todo {{currentRecord.Title}} saved."
                }
                """)
            },
            Edges = new[]
            {
                Edge("edge-1", "trigger-1", "success", "log-1", "input")
            }
        };
    }

    private static WorkflowDefinition CreateFieldChangedWorkflow(string name, bool isEnabled)
    {
        return new WorkflowDefinition
        {
            Name = name,
                        ObjectName = "Todo",
            EventName = WorkflowEventName.FieldChanged,
            IsEnabled = isEnabled,
            Version = 1,
            Nodes = new[]
            {
                                Node("trigger-1", "trigger.field-changed", "{ \"fieldName\": \"Status\" }"),
                Node("log-1", "action.write-log", """
                {
                  "severity": "Information",
                                    "message": "Status changed for {{currentRecord.Title}}."
                }
                """)
            },
            Edges = new[]
            {
                Edge("edge-1", "trigger-1", "success", "log-1", "input")
            }
        };
    }

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

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

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
}