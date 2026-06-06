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
    [InlineData("equals", "{ \"source\": \"currentRecord\", \"field\": \"Priority\" }", "{ \"source\": \"literal\", \"value\": 5 }", "true")]
    [InlineData("notEquals", "{ \"source\": \"currentRecord\", \"field\": \"Priority\" }", "{ \"source\": \"literal\", \"value\": 3 }", "true")]
    [InlineData("greaterThan", "{ \"source\": \"currentRecord\", \"field\": \"Priority\" }", "{ \"source\": \"literal\", \"value\": 3 }", "true")]
    [InlineData("greaterThanOrEqual", "{ \"source\": \"currentRecord\", \"field\": \"Priority\" }", "{ \"source\": \"literal\", \"value\": 5 }", "true")]
    [InlineData("lessThan", "{ \"source\": \"currentRecord\", \"field\": \"Priority\" }", "{ \"source\": \"literal\", \"value\": 8 }", "true")]
    [InlineData("lessThanOrEqual", "{ \"source\": \"currentRecord\", \"field\": \"Priority\" }", "{ \"source\": \"literal\", \"value\": 5 }", "true")]
    [InlineData("contains", "{ \"source\": \"currentRecord\", \"field\": \"Title\" }", "{ \"source\": \"literal\", \"value\": \"odo\" }", "true")]
    [InlineData("startsWith", "{ \"source\": \"currentRecord\", \"field\": \"Title\" }", "{ \"source\": \"literal\", \"value\": \"Tod\" }", "true")]
    [InlineData("endsWith", "{ \"source\": \"currentRecord\", \"field\": \"Title\" }", "{ \"source\": \"literal\", \"value\": \"item\" }", "true")]
    [InlineData("isEmpty", "{ \"source\": \"currentRecord\", \"field\": \"Description\" }", null, "true")]
    [InlineData("isNotEmpty", "{ \"source\": \"currentRecord\", \"field\": \"Title\" }", null, "true")]
    [InlineData("greaterThan", "{ \"source\": \"currentRecord\", \"field\": \"Priority\" }", "{ \"source\": \"literal\", \"value\": 8 }", "false")]
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
              "fieldName": "Priority",
          "value": "12"
        }
        """), context);

        Assert.Equal(WorkflowRunStatus.Succeeded, result.Status);
        Assert.Contains("success", result.SelectedOutputPorts);
            Assert.Equal(12, context.CurrentRecord["Priority"]);
    }

    [Fact]
    public async Task SetFieldNodeExecutor_rejects_after_save_event()
    {
        var executor = new SetFieldNodeExecutor();

        var result = await executor.ExecuteAsync(Node("set-1", "action.set-field", """
        {
          "fieldName": "Title",
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
             "message": "Todo priority {{currentRecord.Priority}} is invalid for {{currentRecord.Title}}."
        }
        """), CreateContext(WorkflowEventName.BeforeSave));

        Assert.Equal(WorkflowRunStatus.Canceled, result.Status);
        Assert.Equal(WorkflowExecutionSignal.Reject, result.Signal);
          Assert.Equal("Todo priority 5 is invalid for Todo item.", result.ErrorMessage);
    }

    [Fact]
    public async Task StopNodeExecutor_returns_stop_signal_with_resolved_reason()
    {
        var executor = new StopNodeExecutor();

        var result = await executor.ExecuteAsync(Node("stop-1", "flow.stop", """
        {
             "reason": "No more work for {{currentRecord.Title}}."
        }
        """), CreateContext(WorkflowEventName.Created));

        Assert.Equal(WorkflowRunStatus.Succeeded, result.Status);
        Assert.Equal(WorkflowExecutionSignal.Stop, result.Signal);
        Assert.NotNull(result.OutputJson);
          Assert.Contains("No more work for Todo item.", result.OutputJson);
    }

    [Fact]
    public async Task WriteLogNodeExecutor_outputs_resolved_log_message()
    {
        var executor = new WriteLogNodeExecutor();

        var result = await executor.ExecuteAsync(WriteLogNode("log-1", "Created {{currentRecord.Title}}."), CreateContext(WorkflowEventName.Created));

        Assert.Equal(WorkflowRunStatus.Succeeded, result.Status);
        Assert.Contains("success", result.SelectedOutputPorts);
        Assert.NotNull(result.OutputJson);
        using var document = JsonDocument.Parse(result.OutputJson);
        Assert.Equal("Information", document.RootElement.GetProperty("severity").GetString());
            Assert.Equal("Created Todo item.", document.RootElement.GetProperty("message").GetString());
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
        var todoId = Guid.NewGuid();

        try
        {
            var store = new EntityStore(dbPath);
            var executor = new CreateRecordNodeExecutor(store);

                        var result = await executor.ExecuteAsync(Node("create-1", "action.create-record", """
                        {
                            "targetObjectName": "Task",
                            "fieldMappings": {
                                "Title": "Follow up for {{currentRecord.Title}}",
                                "RelatedTodoId": "{{currentRecord.Id}}",
                                "Priority": "High"
                            }
                        }
                        """), CreateContext(WorkflowEventName.Created, todoId));

            Assert.Equal(WorkflowRunStatus.Succeeded, result.Status);
            Assert.Contains("success", result.SelectedOutputPorts);
            AssertCreatedTask(dbPath, todoId);
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

    private static WorkflowExecutionContext CreateContext(string eventName, Guid? todoId = null)
    {
        var id = todoId ?? Guid.NewGuid();
        return new WorkflowExecutionContext
        {
            WorkflowId = Guid.NewGuid(),
            WorkflowVersion = 1,
            ObjectName = "Todo",
            EventName = eventName,
            RecordId = id.ToString(),
            CurrentRecord = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = id,
                ["Title"] = "Todo item",
                ["Description"] = "",
                ["Status"] = "Open",
                ["Priority"] = 5
            },
            OriginalRecord = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Priority"] = 12
            },
            ChangedFields = new[] { "Priority" }
        };
    }

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
                new PropertyMetadata("Status", "Status", typeof(string), false),
                new PropertyMetadata("Priority", "Priority", typeof(int), false),
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
                new PropertyMetadata("RelatedTodoId", "RelatedTodoId", typeof(Guid), false),
                new PropertyMetadata("Priority", "Priority", typeof(string), false)
            }
        });
    }

    private static void AssertCreatedTask(string dbPath, Guid todoId)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Title, RelatedTodoId, Priority FROM Tasks";

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("Follow up for Todo item", reader.GetString(0));
        Assert.Equal(todoId.ToString(), reader.GetString(1));
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