using System.Text.Json;
using MetaRecord.Data;
using MetaRecord.Models;
using MetaRecord.Workflows.Definitions;
using MetaRecord.Workflows.Runtime;
using MetaRecord.Workflows.Runtime.Executors;
using Microsoft.Data.Sqlite;

namespace MetaRecord.Core.Tests.Workflows;

public sealed class WorkflowNodeExecutorTests : IDisposable
{
    public WorkflowNodeExecutorTests()
    {
        RegisterTestMetadata();
    }

    public void Dispose()
    {
        MetadataRegistry.Clear();
    }

    [Theory]
    [InlineData("equals", "{ \"source\": \"currentRecord\", \"field\": \"Quantity\" }", "{ \"source\": \"literal\", \"value\": 5 }", "true")]
    [InlineData("notEquals", "{ \"source\": \"currentRecord\", \"field\": \"Quantity\" }", "{ \"source\": \"literal\", \"value\": 3 }", "true")]
    [InlineData("greaterThan", "{ \"source\": \"currentRecord\", \"field\": \"Quantity\" }", "{ \"source\": \"literal\", \"value\": 3 }", "true")]
    [InlineData("greaterThanOrEqual", "{ \"source\": \"currentRecord\", \"field\": \"Quantity\" }", "{ \"source\": \"literal\", \"value\": 5 }", "true")]
    [InlineData("lessThan", "{ \"source\": \"currentRecord\", \"field\": \"Quantity\" }", "{ \"source\": \"literal\", \"value\": 8 }", "true")]
    [InlineData("lessThanOrEqual", "{ \"source\": \"currentRecord\", \"field\": \"Quantity\" }", "{ \"source\": \"literal\", \"value\": 5 }", "true")]
    [InlineData("contains", "{ \"source\": \"currentRecord\", \"field\": \"Name\" }", "{ \"source\": \"literal\", \"value\": \"idg\" }", "true")]
    [InlineData("startsWith", "{ \"source\": \"currentRecord\", \"field\": \"Name\" }", "{ \"source\": \"literal\", \"value\": \"Wid\" }", "true")]
    [InlineData("endsWith", "{ \"source\": \"currentRecord\", \"field\": \"Name\" }", "{ \"source\": \"literal\", \"value\": \"get\" }", "true")]
    [InlineData("isEmpty", "{ \"source\": \"currentRecord\", \"field\": \"Notes\" }", null, "true")]
    [InlineData("isNotEmpty", "{ \"source\": \"currentRecord\", \"field\": \"Name\" }", null, "true")]
    [InlineData("greaterThan", "{ \"source\": \"currentRecord\", \"field\": \"Quantity\" }", "{ \"source\": \"literal\", \"value\": 8 }", "false")]
    public async Task ConditionNodeExecutor_selects_expected_branch(
        string operatorName,
        string leftOperandJson,
        string? rightOperandJson,
        string expectedPort)
    {
        var executor = new ConditionNodeExecutor();

        var result = await executor.ExecuteAsync(
            ConditionNode(operatorName, leftOperandJson, rightOperandJson),
            CreateContext(WorkflowEventName.FieldChanged));

        Assert.Equal(WorkflowRunStatus.Succeeded, result.Status);
        Assert.Contains(expectedPort, result.SelectedOutputPorts);
    }

    [Fact]
    public async Task SetFieldNodeExecutor_mutates_current_record_with_converted_value()
    {
        var context = CreateContext(WorkflowEventName.BeforeSave);
        var executor = new SetFieldNodeExecutor();

        var result = await executor.ExecuteAsync(Node("set-1", "action.set-field", """
        {
          "fieldName": "Quantity",
          "value": "12"
        }
        """), context);

        Assert.Equal(WorkflowRunStatus.Succeeded, result.Status);
        Assert.Contains("success", result.SelectedOutputPorts);
        Assert.Equal(12, context.CurrentRecord["Quantity"]);
    }

    [Fact]
    public async Task SetFieldNodeExecutor_rejects_after_save_event()
    {
        var executor = new SetFieldNodeExecutor();

        var result = await executor.ExecuteAsync(Node("set-1", "action.set-field", """
        {
          "fieldName": "Name",
          "value": "Updated"
        }
        """), CreateContext(WorkflowEventName.Created));

        Assert.Equal(WorkflowRunStatus.Failed, result.Status);
        Assert.Contains("BeforeSave", result.ErrorMessage);
    }

    [Fact]
    public async Task RejectSaveNodeExecutor_returns_reject_signal_with_template_message()
    {
        var executor = new RejectSaveNodeExecutor();

        var result = await executor.ExecuteAsync(Node("reject-1", "action.reject-save", """
        {
          "message": "Price {{currentRecord.Price}} is invalid for {{currentRecord.Name}}."
        }
        """), CreateContext(WorkflowEventName.BeforeSave));

        Assert.Equal(WorkflowRunStatus.Canceled, result.Status);
        Assert.Equal(WorkflowExecutionSignal.Reject, result.Signal);
        Assert.Equal("Price 9.99 is invalid for Widget.", result.ErrorMessage);
    }

    [Fact]
    public async Task StopNodeExecutor_returns_stop_signal_with_resolved_reason()
    {
        var executor = new StopNodeExecutor();

        var result = await executor.ExecuteAsync(Node("stop-1", "flow.stop", """
        {
          "reason": "No more work for {{currentRecord.Name}}."
        }
        """), CreateContext(WorkflowEventName.Created));

        Assert.Equal(WorkflowRunStatus.Succeeded, result.Status);
        Assert.Equal(WorkflowExecutionSignal.Stop, result.Signal);
        Assert.NotNull(result.OutputJson);
        Assert.Contains("No more work for Widget.", result.OutputJson);
    }

    [Fact]
    public async Task WriteLogNodeExecutor_outputs_resolved_log_message()
    {
        var executor = new WriteLogNodeExecutor();

        var result = await executor.ExecuteAsync(WriteLogNode("log-1", "Created {{currentRecord.Name}}."), CreateContext(WorkflowEventName.Created));

        Assert.Equal(WorkflowRunStatus.Succeeded, result.Status);
        Assert.Contains("success", result.SelectedOutputPorts);
        Assert.NotNull(result.OutputJson);
        using var document = JsonDocument.Parse(result.OutputJson);
        Assert.Equal("Information", document.RootElement.GetProperty("severity").GetString());
        Assert.Equal("Created Widget.", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task WriteLogNodeExecutor_returns_failed_result_for_missing_template_value()
    {
        var executor = new WriteLogNodeExecutor();

        var result = await executor.ExecuteAsync(WriteLogNode("log-1", "Missing {{currentRecord.DoesNotExist}}."), CreateContext(WorkflowEventName.Created));

        Assert.Equal(WorkflowRunStatus.Failed, result.Status);
        Assert.Contains("currentRecord.DoesNotExist", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateRecordNodeExecutor_inserts_metadata_record_from_field_mappings()
    {
        var dbPath = CreateTempDbPath();
        var productId = Guid.NewGuid();

        try
        {
            var store = new EntityStore(dbPath);
            var executor = new CreateRecordNodeExecutor(store);

            var result = await executor.ExecuteAsync(Node("create-1", "action.create-record", """
            {
              "targetObjectName": "Task",
              "fieldMappings": {
                "Title": "Follow up for {{currentRecord.Name}}",
                "RelatedProductId": "{{currentRecord.Id}}",
                "Priority": "High"
              }
            }
            """), CreateContext(WorkflowEventName.Created, productId));

            Assert.Equal(WorkflowRunStatus.Succeeded, result.Status);
            Assert.Contains("success", result.SelectedOutputPorts);
            AssertCreatedTask(dbPath, productId);
        }
        finally
        {
            DeleteTempDb(dbPath);
        }
    }

    private static WorkflowNode ConditionNode(string operatorName, string leftOperandJson, string? rightOperandJson)
    {
        var rightOperand = rightOperandJson is null ? string.Empty : $", \"right\": {rightOperandJson}";
        return Node("condition-1", "flow.condition", $$"""
        {
          "condition": {
            "left": {{leftOperandJson}},
            "operator": "{{operatorName}}"{{rightOperand}}
          }
        }
        """);
    }

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

    private static WorkflowExecutionContext CreateContext(string eventName, Guid? productId = null)
    {
        var id = productId ?? Guid.NewGuid();
        return new WorkflowExecutionContext
        {
            WorkflowId = Guid.NewGuid(),
            WorkflowVersion = 1,
            ObjectName = "Product",
            EventName = eventName,
            RecordId = id.ToString(),
            CurrentRecord = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = id,
                ["Name"] = "Widget",
                ["Price"] = 9.99m,
                ["Quantity"] = 5,
                ["Notes"] = ""
            },
            OriginalRecord = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Quantity"] = 12
            },
            ChangedFields = new[] { "Quantity" }
        };
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static void RegisterTestMetadata()
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
                new PropertyMetadata("Quantity", "Quantity", typeof(int), false),
                new PropertyMetadata("Notes", "Notes", typeof(string), false)
            }
        });

        MetadataRegistry.RegisterByName(new ObjectMetadata
        {
            Name = "Task",
            TableName = "Tasks",
            Properties = new[]
            {
                new PropertyMetadata("Id", "Id", typeof(Guid), true) { IsPrimaryKey = true },
                new PropertyMetadata("Title", "Title", typeof(string), true),
                new PropertyMetadata("RelatedProductId", "RelatedProductId", typeof(Guid), false),
                new PropertyMetadata("Priority", "Priority", typeof(string), false)
            }
        });
    }

    private static void AssertCreatedTask(string dbPath, Guid productId)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Title, RelatedProductId, Priority FROM Tasks";

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("Follow up for Widget", reader.GetString(0));
        Assert.Equal(productId.ToString(), reader.GetString(1));
        Assert.Equal("High", reader.GetString(2));
        Assert.False(reader.Read());
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