using System.Text.Json;
using MetaRecord.Models;
using MetaRecord.Workflows.Definitions;
using MetaRecord.Workflows.Validation;

namespace MetaRecord.Core.Tests.Workflows;

public sealed class WorkflowValidatorTests : IDisposable
{
    public WorkflowValidatorTests()
    {
        RegisterTestMetadata();
    }

    public void Dispose()
    {
        MetadataRegistry.Clear();
    }

    [Fact]
    public void Validate_accepts_valid_before_save_workflow()
    {
        var workflow = CreateBeforeSaveWorkflow();

        var issues = Validate(workflow);

        AssertNoErrors(issues);
    }

    [Fact]
    public void Validate_accepts_valid_field_changed_workflow()
    {
        var workflow = CreateFieldChangedWorkflow();

        var issues = Validate(workflow);

        AssertNoErrors(issues);
    }

    [Fact]
    public void Validate_rejects_missing_metadata_object()
    {
        var workflow = CreateBeforeSaveWorkflow(objectName: "MissingTodo");

        var issues = Validate(workflow);

        AssertContainsError(issues, "Object 'MissingTodo' does not exist in metadata", field: "objectName");
    }

    [Fact]
    public void Validate_rejects_missing_property_reference()
    {
        var workflow = CreateBeforeSaveWorkflow(conditionField: "MissingField");

        var issues = Validate(workflow);

        AssertContainsError(issues, "Field 'MissingField' does not exist on object 'Todo'", "condition-1", "config.condition.left.field");
    }

    [Fact]
    public void Validate_rejects_unknown_node_type()
    {
        var workflow = new WorkflowDefinition
        {
            Name = "Unknown node type",
            ObjectName = "Todo",
            EventName = WorkflowEventName.BeforeSave,
            Nodes = new[]
            {
                Node("trigger-1", "trigger.before-save", "{}"),
                Node("unknown-1", "action.not-registered", "{}")
            },
            Edges = new[]
            {
                Edge("edge-1", "trigger-1", "success", "unknown-1", "input")
            }
        };

        var issues = Validate(workflow);
        AssertContainsError(issues, "Node type 'action.not-registered' is not registered", "unknown-1", "type");
    }

    [Fact]
    public void Validate_rejects_trigger_event_mismatch()
    {
        var workflow = new WorkflowDefinition
        {
            Name = "Trigger mismatch",
            ObjectName = "Todo",
            EventName = WorkflowEventName.Created,
            Nodes = new[]
            {
                Node("trigger-1", "trigger.before-save", "{}"),
                Node("log-1", "action.write-log", """
                {
                  "severity": "Information",
                                    "message": "Created {{currentRecord.Title}}"
                }
                """)
            },
            Edges = new[]
            {
                Edge("edge-1", "trigger-1", "success", "log-1", "input")
            }
        };

        var issues = Validate(workflow);
        AssertContainsError(issues, "Trigger node 'trigger.before-save' does not match workflow event 'Created'", "trigger-1", "type");
    }

    [Fact]
    public void Validate_rejects_invalid_port()
    {
        var workflow = CreateBeforeSaveWorkflow(edgeFromPort: "done");

        var issues = Validate(workflow);
        AssertContainsError(issues, "Port 'done' is not a valid output port", "trigger-1", "fromPort");
    }

    [Fact]
    public void Validate_rejects_graph_cycles()
    {
        var workflow = new WorkflowDefinition
        {
            Name = "Cycle",
            ObjectName = "Todo",
            EventName = WorkflowEventName.FieldChanged,
            Nodes = new[]
            {
                Node("trigger-1", "trigger.field-changed", "{ \"fieldName\": \"Quantity\" }"),
                ConditionNode("condition-1", "Quantity"),
                WriteLogNode("log-1")
            },
            Edges = new[]
            {
                Edge("edge-1", "trigger-1", "success", "condition-1", "input"),
                Edge("edge-2", "condition-1", "true", "log-1", "input"),
                Edge("edge-3", "log-1", "success", "condition-1", "input")
            }
        };

        var issues = Validate(workflow);
        AssertContainsError(issues, "Workflow graph contains a cycle");
    }

    [Fact]
    public void Validate_rejects_unreachable_nodes()
    {
        var workflow = CreateFieldChangedWorkflow(addUnreachableNode: true);

        var issues = Validate(workflow);
        AssertContainsError(issues, "Node 'unreachable-log' is not reachable from the trigger", "unreachable-log");
    }

    [Fact]
    public void Validate_rejects_before_only_action_in_after_event_workflow()
    {
        var workflow = new WorkflowDefinition
        {
            Name = "Set field after save",
            ObjectName = "Todo",
            EventName = WorkflowEventName.FieldChanged,
            Nodes = new[]
            {
                                Node("trigger-1", "trigger.field-changed", "{ \"fieldName\": \"Status\" }"),
                Node("set-field-1", "action.set-field", """
                {
                                    "fieldName": "Title",
                  "value": "Updated"
                }
                """)
            },
            Edges = new[]
            {
                Edge("edge-1", "trigger-1", "success", "set-field-1", "input")
            }
        };

        var issues = Validate(workflow);
        AssertContainsError(issues, "Node type 'action.set-field' is not allowed for event 'FieldChanged'", "set-field-1", "type");
    }

    private static IReadOnlyList<WorkflowValidationIssue> Validate(WorkflowDefinition workflow) =>
        new WorkflowValidator().Validate(workflow);

    private static WorkflowDefinition CreateBeforeSaveWorkflow(
        string objectName = "Todo",
        string conditionField = "Title",
        string edgeFromPort = "success")
    {
        return new WorkflowDefinition
        {
            Name = "Reject empty todo title",
            ObjectName = objectName,
            EventName = WorkflowEventName.BeforeSave,
            Nodes = new[]
            {
                Node("trigger-1", "trigger.before-save", "{}"),
                ConditionNode("condition-1", conditionField),
                Node("reject-1", "action.reject-save", """
                {
                                    "message": "Todo title is required."
                }
                """)
            },
            Edges = new[]
            {
                Edge("edge-1", "trigger-1", edgeFromPort, "condition-1", "input"),
                Edge("edge-2", "condition-1", "true", "reject-1", "input")
            }
        };
    }

    private static WorkflowDefinition CreateFieldChangedWorkflow(bool addUnreachableNode = false)
    {
        var nodes = new List<WorkflowNode>
        {
            Node("trigger-1", "trigger.field-changed", "{ \"fieldName\": \"Status\" }"),
            ConditionNode("condition-1", "Status"),
            WriteLogNode("log-1")
        };

        if (addUnreachableNode)
            nodes.Add(WriteLogNode("unreachable-log"));

        return new WorkflowDefinition
        {
            Name = "Log todo status changes",
            ObjectName = "Todo",
            EventName = WorkflowEventName.FieldChanged,
            Nodes = nodes,
            Edges = new[]
            {
                Edge("edge-1", "trigger-1", "success", "condition-1", "input"),
                Edge("edge-2", "condition-1", "true", "log-1", "input")
            }
        };
    }

    private static WorkflowNode ConditionNode(string id, string fieldName) => Node(id, "flow.condition", $$"""
    {
      "condition": {
        "left": { "source": "currentRecord", "field": "{{fieldName}}" },
                "operator": "equals",
                "right": { "source": "literal", "value": "Done" }
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

    private static void AssertNoErrors(IReadOnlyList<WorkflowValidationIssue> issues)
    {
        Assert.DoesNotContain(issues, issue => issue.Severity == WorkflowValidationSeverity.Error);
    }

    private static void AssertContainsError(
        IReadOnlyList<WorkflowValidationIssue> issues,
        string messageFragment,
        string? nodeId = null,
        string? field = null)
    {
        Assert.Contains(issues, issue =>
            issue.Severity == WorkflowValidationSeverity.Error &&
            issue.Message.Contains(messageFragment, StringComparison.OrdinalIgnoreCase) &&
            (nodeId is null || string.Equals(issue.NodeId, nodeId, StringComparison.OrdinalIgnoreCase)) &&
            (field is null || string.Equals(issue.Field, field, StringComparison.OrdinalIgnoreCase)));
    }
}