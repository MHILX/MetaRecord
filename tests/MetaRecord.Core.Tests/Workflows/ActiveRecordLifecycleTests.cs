using System.Text.Json;
using MetaRecord.Data;
using MetaRecord.Models;
using MetaRecord.Workflows.Definitions;
using MetaRecord.Workflows.Persistence;
using MetaRecord.Workflows.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MetaRecord.Core.Tests.Workflows;

public sealed class ActiveRecordLifecycleTests : IDisposable
{
    public void Dispose()
    {
        WorkflowRuntime.Reset();
        MetadataRegistry.Clear();
    }

    [Fact]
    public async Task SaveAsync_runs_before_save_workflow_and_applies_mutated_values_before_insert()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = await CreateConfiguredContextAsync(dbPath);
            var repository = new WorkflowRepository(context);
            var store = ConfigureRuntime(dbPath, repository);
            await repository.SaveDefinitionAsync(CreateBeforeSaveSetFieldWorkflow());

            var todo = new Todo { Title = "Todo item", Description = "Initial todo", Status = "Open", Priority = 1 };

            await todo.SaveAsync();

            var saved = store.Find<Todo>(Todo.Metadata, todo.Id);
            Assert.NotNull(saved);
            Assert.Equal(25, todo.Priority);
            Assert.Equal(25, saved.Priority);
            Assert.Equal("Succeeded", await GetOnlyRunStatusAsync(context, WorkflowEventName.BeforeSave));
        }
        finally
        {
            DeleteTempDb(dbPath);
        }
    }

    [Fact]
    public async Task SaveAsync_blocks_insert_when_before_save_workflow_rejects()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = await CreateConfiguredContextAsync(dbPath);
            var repository = new WorkflowRepository(context);
            var store = ConfigureRuntime(dbPath, repository);
            await repository.SaveDefinitionAsync(CreateBeforeSaveRejectWorkflow());
            var todo = new Todo { Title = "Todo item", Description = "Initial todo", Status = "Open", Priority = -1 };

            var exception = await Assert.ThrowsAsync<WorkflowSaveRejectedException>(() => todo.SaveAsync());

            Assert.Equal("Priority -1 is invalid.", exception.Message);
            Assert.True(todo.IsNew);
            Assert.Equal(0, store.Count(Todo.Metadata));
            Assert.Equal("Canceled", await GetOnlyRunStatusAsync(context, WorkflowEventName.BeforeSave));
        }
        finally
        {
            DeleteTempDb(dbPath);
        }
    }

    [Fact]
    public async Task SaveAsync_dispatches_created_after_insert()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = await CreateConfiguredContextAsync(dbPath);
            var repository = new WorkflowRepository(context);
            ConfigureRuntime(dbPath, repository);
            await repository.SaveDefinitionAsync(CreateWriteLogWorkflow(WorkflowEventName.Created, "trigger.record-created", "created-log", "Created {{currentRecord.Title}}."));

            var todo = new Todo { Title = "Todo item", Description = "Initial todo", Status = "Open", Priority = 1 };

            await todo.SaveAsync();

            var run = await GetOnlyRunAsync(context, WorkflowEventName.Created);
            Assert.Equal("Succeeded", run.Status);
            Assert.Contains(run.Steps, step => step.NodeId == "log-1" && step.OutputJson!.Contains("Created Todo item."));
        }
        finally
        {
            DeleteTempDb(dbPath);
        }
    }

    [Fact]
    public async Task SaveAsync_dispatches_updated_and_matching_field_changed_after_update()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = await CreateConfiguredContextAsync(dbPath);
            var repository = new WorkflowRepository(context);
            ConfigureRuntime(dbPath, repository);
            var todo = new Todo { Title = "Todo item", Description = "Initial todo", Status = "Open", Priority = 1 };
            await todo.SaveAsync();

            var updatedWorkflow = CreateWriteLogWorkflow(WorkflowEventName.Updated, "trigger.record-updated", "updated-log", "Updated {{currentRecord.Title}}.");
            var statusChangedWorkflow = CreateFieldChangedWorkflow("Status", "Status changed to {{currentRecord.Status}}.");
            var priorityChangedWorkflow = CreateFieldChangedWorkflow("Priority", "Priority changed to {{currentRecord.Priority}}.");
            await repository.SaveDefinitionAsync(updatedWorkflow);
            await repository.SaveDefinitionAsync(statusChangedWorkflow);
            await repository.SaveDefinitionAsync(priorityChangedWorkflow);

            todo.Status = "Done";
            await todo.SaveAsync();

            var updatedRun = await GetOnlyRunAsync(context, WorkflowEventName.Updated);
            var fieldChangedRuns = await context.WorkflowRuns
                .AsNoTracking()
                .Where(run => run.EventName == WorkflowEventName.FieldChanged)
                .ToListAsync();

            Assert.Equal("Succeeded", updatedRun.Status);
            var fieldChangedRun = Assert.Single(fieldChangedRuns);
            Assert.Equal(statusChangedWorkflow.Id, fieldChangedRun.WorkflowId);
            Assert.NotEqual(priorityChangedWorkflow.Id, fieldChangedRun.WorkflowId);
        }
        finally
        {
            DeleteTempDb(dbPath);
        }
    }

    [Fact]
    public async Task SaveAsync_does_not_roll_back_insert_when_after_save_workflow_fails()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            await using var context = await CreateConfiguredContextAsync(dbPath);
            var repository = new WorkflowRepository(context);
            var store = ConfigureRuntime(dbPath, repository);
            await repository.SaveDefinitionAsync(CreateWriteLogWorkflow(WorkflowEventName.Created, "trigger.record-created", "bad-created-log", "Missing {{currentRecord.DoesNotExist}}."));
            var todo = new Todo { Title = "Todo item", Description = "Initial todo", Status = "Open", Priority = 1 };

            await todo.SaveAsync();

            Assert.NotNull(store.Find<Todo>(Todo.Metadata, todo.Id));
            Assert.Equal("Failed", await GetOnlyRunStatusAsync(context, WorkflowEventName.Created));
        }
        finally
        {
            DeleteTempDb(dbPath);
        }
    }

    private static async Task<MetaRecordDbContext> CreateConfiguredContextAsync(string dbPath)
    {
        RegisterTodoMetadata();
        var context = new MetaRecordDbContext(dbPath);
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static EntityStore ConfigureRuntime(string dbPath, WorkflowRepository repository)
    {
        var store = new EntityStore(dbPath);
        store.EnsureTableExists(Todo.Metadata);
        WorkflowRuntime.Configure(store, repository);
        return store;
    }

    private static WorkflowDefinition CreateBeforeSaveSetFieldWorkflow() => new()
    {
        Name = "Set priority before save",
        ObjectName = "Todo",
        EventName = WorkflowEventName.BeforeSave,
        IsEnabled = true,
        Nodes = new[]
        {
            Node("trigger-1", "trigger.before-save", "{}"),
            Node("set-1", "action.set-field", """
            {
              "fieldName": "Priority",
              "value": "25"
            }
            """)
        },
        Edges = new[]
        {
            Edge("edge-1", "trigger-1", "success", "set-1", "input")
        }
    };

    private static WorkflowDefinition CreateBeforeSaveRejectWorkflow() => new()
    {
        Name = "Reject todo before save",
        ObjectName = "Todo",
        EventName = WorkflowEventName.BeforeSave,
        IsEnabled = true,
        Nodes = new[]
        {
            Node("trigger-1", "trigger.before-save", "{}"),
            Node("reject-1", "action.reject-save", """
            {
              "message": "Priority {{currentRecord.Priority}} is invalid."
            }
            """)
        },
        Edges = new[]
        {
            Edge("edge-1", "trigger-1", "success", "reject-1", "input")
        }
    };

    private static WorkflowDefinition CreateWriteLogWorkflow(
        string eventName,
        string triggerType,
        string name,
        string message) => new()
    {
        Name = name,
        ObjectName = "Todo",
        EventName = eventName,
        IsEnabled = true,
        Nodes = new[]
        {
            Node("trigger-1", triggerType, "{}"),
            WriteLogNode("log-1", message)
        },
        Edges = new[]
        {
            Edge("edge-1", "trigger-1", "success", "log-1", "input")
        }
    };

    private static WorkflowDefinition CreateFieldChangedWorkflow(string fieldName, string message) => new()
    {
        Name = $"{fieldName} changed",
        ObjectName = "Todo",
        EventName = WorkflowEventName.FieldChanged,
        IsEnabled = true,
        Nodes = new[]
        {
            Node("trigger-1", "trigger.field-changed", $"{{ \"fieldName\": \"{fieldName}\" }}"),
            WriteLogNode("log-1", message)
        },
        Edges = new[]
        {
            Edge("edge-1", "trigger-1", "success", "log-1", "input")
        }
    };

    private static WorkflowNode WriteLogNode(string id, string message) => Node(id, "action.write-log", $$"""
    {
      "severity": "Information",
      "message": "{{message}}"
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

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static async Task<WorkflowRunEntity> GetOnlyRunAsync(MetaRecordDbContext context, string eventName)
    {
        var runs = await context.WorkflowRuns
            .AsNoTracking()
            .Include(run => run.Steps)
            .Where(run => run.EventName == eventName)
            .ToListAsync();

        return Assert.Single(runs);
    }

    private static async Task<string> GetOnlyRunStatusAsync(MetaRecordDbContext context, string eventName)
    {
        var run = await GetOnlyRunAsync(context, eventName);
        return run.Status;
    }

    private static void RegisterTodoMetadata()
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
                new PropertyMetadata("Status", "Status", typeof(string), false),
                new PropertyMetadata("Priority", "Priority", typeof(int), false)
            }
        });
        MetadataRegistry.LinkType<Todo>("Todo");
    }

    private static string CreateTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"metarecord-{Guid.NewGuid():N}.db");

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