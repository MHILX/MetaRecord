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

            var product = new Product { Name = "Widget", Price = 9.99m, Quantity = 1 };

            await product.SaveAsync();

            var saved = store.Find<Product>(Product.Metadata, product.Id);
            Assert.NotNull(saved);
            Assert.Equal(25, product.Quantity);
            Assert.Equal(25, saved.Quantity);
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
            var product = new Product { Name = "Widget", Price = -1m, Quantity = 1 };

            var exception = await Assert.ThrowsAsync<WorkflowSaveRejectedException>(() => product.SaveAsync());

            Assert.Equal("Price -1 is invalid.", exception.Message);
            Assert.True(product.IsNew);
            Assert.Equal(0, store.Count(Product.Metadata));
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
            await repository.SaveDefinitionAsync(CreateWriteLogWorkflow(WorkflowEventName.Created, "trigger.record-created", "created-log", "Created {{currentRecord.Name}}."));

            var product = new Product { Name = "Widget", Price = 9.99m, Quantity = 1 };

            await product.SaveAsync();

            var run = await GetOnlyRunAsync(context, WorkflowEventName.Created);
            Assert.Equal("Succeeded", run.Status);
            Assert.Contains(run.Steps, step => step.NodeId == "log-1" && step.OutputJson!.Contains("Created Widget."));
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
            var product = new Product { Name = "Widget", Price = 9.99m, Quantity = 1 };
            await product.SaveAsync();

            var updatedWorkflow = CreateWriteLogWorkflow(WorkflowEventName.Updated, "trigger.record-updated", "updated-log", "Updated {{currentRecord.Name}}.");
            var priceChangedWorkflow = CreateFieldChangedWorkflow("Price", "Price changed to {{currentRecord.Price}}.");
            var quantityChangedWorkflow = CreateFieldChangedWorkflow("Quantity", "Quantity changed to {{currentRecord.Quantity}}.");
            await repository.SaveDefinitionAsync(updatedWorkflow);
            await repository.SaveDefinitionAsync(priceChangedWorkflow);
            await repository.SaveDefinitionAsync(quantityChangedWorkflow);

            product.Price = 12.50m;
            await product.SaveAsync();

            var updatedRun = await GetOnlyRunAsync(context, WorkflowEventName.Updated);
            var fieldChangedRuns = await context.WorkflowRuns
                .AsNoTracking()
                .Where(run => run.EventName == WorkflowEventName.FieldChanged)
                .ToListAsync();

            Assert.Equal("Succeeded", updatedRun.Status);
            var fieldChangedRun = Assert.Single(fieldChangedRuns);
            Assert.Equal(priceChangedWorkflow.Id, fieldChangedRun.WorkflowId);
            Assert.NotEqual(quantityChangedWorkflow.Id, fieldChangedRun.WorkflowId);
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
            var product = new Product { Name = "Widget", Price = 9.99m, Quantity = 1 };

            await product.SaveAsync();

            Assert.NotNull(store.Find<Product>(Product.Metadata, product.Id));
            Assert.Equal("Failed", await GetOnlyRunStatusAsync(context, WorkflowEventName.Created));
        }
        finally
        {
            DeleteTempDb(dbPath);
        }
    }

    private static async Task<MetaRecordDbContext> CreateConfiguredContextAsync(string dbPath)
    {
        RegisterProductMetadata();
        var context = new MetaRecordDbContext(dbPath);
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static EntityStore ConfigureRuntime(string dbPath, WorkflowRepository repository)
    {
        var store = new EntityStore(dbPath);
        store.EnsureTableExists(Product.Metadata);
        WorkflowRuntime.Configure(store, repository);
        return store;
    }

    private static WorkflowDefinition CreateBeforeSaveSetFieldWorkflow() => new()
    {
        Name = "Set quantity before save",
        ObjectName = "Product",
        EventName = WorkflowEventName.BeforeSave,
        IsEnabled = true,
        Nodes = new[]
        {
            Node("trigger-1", "trigger.before-save", "{}"),
            Node("set-1", "action.set-field", """
            {
              "fieldName": "Quantity",
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
        Name = "Reject product before save",
        ObjectName = "Product",
        EventName = WorkflowEventName.BeforeSave,
        IsEnabled = true,
        Nodes = new[]
        {
            Node("trigger-1", "trigger.before-save", "{}"),
            Node("reject-1", "action.reject-save", """
            {
              "message": "Price {{currentRecord.Price}} is invalid."
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
        ObjectName = "Product",
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
        ObjectName = "Product",
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

    private static void RegisterProductMetadata()
    {
        MetadataRegistry.Clear();
        MetadataRegistry.RegisterByName(new ObjectMetadata
        {
            Name = "Product",
            TableName = "Products",
            Properties = new[]
            {
                new PropertyMetadata("Id", "Id", typeof(Guid), true) { IsPrimaryKey = true },
                new PropertyMetadata("Name", "Name", typeof(string), true),
                new PropertyMetadata("Price", "Price", typeof(decimal), true),
                new PropertyMetadata("Quantity", "Quantity", typeof(int), false)
            }
        });
        MetadataRegistry.LinkType<Product>("Product");
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