using System.Text.Json;
using MetaRecord.Workflows.Definitions;
using MetaRecord.Workflows.Persistence;

namespace MetaRecord.Workflows;

public static class DemoWorkflowSeeder
{
    public static IReadOnlyList<WorkflowDefinition> CreateDemoWorkflows() => new[]
    {
        CreateHeroWorkflow(),
        CreateRejectInvalidPriceWorkflow(),
        CreateCreatedLogWorkflow(),
        CreateLowQuantityChangedWorkflow()
    };

    public static async Task<IReadOnlyList<WorkflowDefinition>> SeedAsync(
        WorkflowRepository repository,
        CancellationToken cancellationToken = default)
    {
        var workflows = CreateDemoWorkflows();

        foreach (var workflow in workflows)
            await repository.SaveDefinitionAsync(workflow, cancellationToken);

        return workflows;
    }

    private static WorkflowDefinition CreateHeroWorkflow() => new()
    {
        Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000010"),
        Name = "Capture product audit snapshot",
        ObjectName = "Product",
        EventName = WorkflowEventName.Manual,
        IsEnabled = true,
        Nodes = new[]
        {
            Node("trigger-1", "trigger.manual", JsonSerializer.Serialize(new { }), 80, 140),
            Node("create-audit-1", "action.create-record", JsonSerializer.Serialize(new
            {
                targetObjectName = "WorkflowAuditEntry",
                fieldMappings = new
                {
                    ProductName = "{{currentRecord.Name}}",
                    ProductPrice = "{{currentRecord.Price}}",
                    Quantity = "{{currentRecord.Quantity}}",
                    WorkflowId = "{{event.WorkflowId}}",
                    EventName = "{{event.EventName}}",
                    Note = "Manual audit snapshot for {{currentRecord.Name}}"
                }
            }), 380, 140),
            WriteLogNode("log-1", "Audit snapshot created for {{currentRecord.Name}}.", 700, 140),
            StopNode("stop-1", "Audit snapshot workflow complete for {{currentRecord.Name}}.", 1020, 140)
        },
        Edges = new[]
        {
            Edge("edge-1", "trigger-1", "success", "create-audit-1", "input"),
            Edge("edge-2", "create-audit-1", "success", "log-1", "input"),
            Edge("edge-3", "log-1", "success", "stop-1", "input")
        }
    };

    private static WorkflowDefinition CreateRejectInvalidPriceWorkflow() => new()
    {
        Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
        Name = "Reject invalid product price",
        ObjectName = "Product",
        EventName = WorkflowEventName.BeforeSave,
        IsEnabled = true,
        Nodes = new[]
        {
            Node("trigger-1", "trigger.before-save", JsonSerializer.Serialize(new { }), 80, 140),
                        ConditionNode("condition-1", "Price", "lessThanOrEqual", "0", 380, 140),
            Node("reject-1", "action.reject-save", JsonSerializer.Serialize(new { message = "Price must be greater than zero for {{currentRecord.Name}}." }), 680, 140)
        },
        Edges = new[]
        {
            Edge("edge-1", "trigger-1", "success", "condition-1", "input"),
            Edge("edge-2", "condition-1", "true", "reject-1", "input")
        }
    };

    private static WorkflowDefinition CreateCreatedLogWorkflow() => new()
    {
        Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
        Name = "Write log when product is created",
        ObjectName = "Product",
        EventName = WorkflowEventName.Created,
        IsEnabled = true,
        Nodes = new[]
        {
            Node("trigger-1", "trigger.record-created", JsonSerializer.Serialize(new { }), 80, 140),
            WriteLogNode("log-1", "Created {{currentRecord.Name}} at price {{currentRecord.Price}}.", 380, 140)
        },
        Edges = new[]
        {
            Edge("edge-1", "trigger-1", "success", "log-1", "input")
        }
    };

    private static WorkflowDefinition CreateLowQuantityChangedWorkflow() => new()
    {
        Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"),
        Name = "Write log when quantity is low",
        ObjectName = "Product",
        EventName = WorkflowEventName.FieldChanged,
        IsEnabled = true,
        Nodes = new[]
        {
            Node("trigger-1", "trigger.field-changed", JsonSerializer.Serialize(new { fieldName = "Quantity" }), 80, 140),
            ConditionNode("condition-1", "Quantity", "lessThan", "10", 380, 140),
            WriteLogNode("log-1", "Low quantity for {{currentRecord.Name}}: {{currentRecord.Quantity}} remaining.", 680, 140)
        },
        Edges = new[]
        {
            Edge("edge-1", "trigger-1", "success", "condition-1", "input"),
            Edge("edge-2", "condition-1", "true", "log-1", "input")
        }
    };

    private static WorkflowNode ConditionNode(string id, string fieldName, string operatorName, string literalValue, double x, double y) => Node(id, "flow.condition", JsonSerializer.Serialize(new
    {
        condition = new
        {
            left = new
            {
                source = "currentRecord",
                field = fieldName
            },
            @operator = operatorName,
            right = new
            {
                source = "literal",
                value = JsonDocument.Parse(literalValue).RootElement
            }
        }
    }), x, y);

    private static WorkflowNode WriteLogNode(string id, string message, double x, double y) => Node(id, "action.write-log", JsonSerializer.Serialize(new
    {
        severity = "Information",
        message
    }), x, y);

    private static WorkflowNode StopNode(string id, string reason, double x, double y) => Node(id, "flow.stop", JsonSerializer.Serialize(new
    {
        reason
    }), x, y);

    private static WorkflowNode Node(string id, string type, string configJson, double x, double y) => new()
    {
        Id = id,
        Type = type,
        Position = new WorkflowPosition
        {
            X = x,
            Y = y
        },
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
}