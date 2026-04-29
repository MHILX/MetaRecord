using System.Text.Json;
using MetaRecord.Data;
using MetaRecord.Models;
using MetaRecord.Services;
using MetaRecord.Workflows.Definitions;
using MetaRecord.Workflows.Persistence;
using MetaRecord.Workflows.Runtime;
using Microsoft.EntityFrameworkCore;

// ============================================================
// MetaRecord Demo: Active Record + Low-Code Workflow Runtime
// ============================================================

await RunDemoAsync();

async Task RunDemoAsync()
{
    Console.WriteLine("=== MetaRecord Demo ===\n");

    Console.WriteLine("0. METADATA INITIALIZATION (from SQLite)");
    await using var metaContext = new MetaRecordDbContext();
    await InitializeMetadataAsync(metaContext);
    Console.WriteLine();

    var repository = new WorkflowRepository(metaContext);
    var store = EntityStore.Current;
    WorkflowRuntime.Configure(store, repository);

    Console.WriteLine("1. SEED WORKFLOW DEFINITIONS");
    var demoWorkflows = await SeedDemoWorkflowsAsync(repository);
    foreach (var workflow in demoWorkflows)
        Console.WriteLine($"   Enabled: {workflow.Name} ({workflow.EventName})");
    Console.WriteLine();

    Console.WriteLine("2. VALID SAVE: BeforeSave + Created");
    var suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
    var product = new Product { Name = $"Widget-{suffix}", Price = 9.99m, Quantity = 100 };
    await product.SaveAsync();
    Console.WriteLine($"   Saved: {product.Name} @ ${product.Price} quantity {product.Quantity}");
    await PrintLatestRunAsync(metaContext, WorkflowEventName.BeforeSave, "BeforeSave validation");
    await PrintLatestRunAsync(metaContext, WorkflowEventName.Created, "Created log");
    Console.WriteLine();

    Console.WriteLine("3. INVALID SAVE: BeforeSave rejection");
    var invalidProduct = new Product { Name = $"Invalid-{suffix}", Price = 0m, Quantity = 1 };
    try
    {
        await invalidProduct.SaveAsync();
    }
    catch (WorkflowSaveRejectedException ex)
    {
        Console.WriteLine($"   Rejected: {ex.Message}");
        foreach (var result in ex.WorkflowResults)
            PrintRunResult(result, "Rejected run");
    }
    Console.WriteLine($"   Product persisted: {Product.Find(invalidProduct.Id) is not null}");
    Console.WriteLine();

    Console.WriteLine("4. UPDATE SAVE: FieldChanged");
    product.Quantity = 5;
    await product.SaveAsync();
    Console.WriteLine($"   Updated quantity: {product.Quantity}");
    await PrintLatestRunAsync(metaContext, WorkflowEventName.FieldChanged, "Quantity low log");
    Console.WriteLine();

    Console.WriteLine("5. METADATA-DRIVEN OBJECT MODEL");
    var metadata = Product.Metadata;
    Console.WriteLine($"   Object: {metadata.Name}");
    Console.WriteLine($"   Table:  {metadata.TableName}");
    Console.WriteLine("   Properties:");
    foreach (var prop in metadata.Properties)
        Console.WriteLine($"     - {prop.Name} ({prop.ClrType.Name}) {(prop.IsRequired ? "[Required]" : "")}");

    Console.WriteLine("\n6. QUERY ALL (from SQLite)");
    var allProducts = Product.All();
    Console.WriteLine($"   Total products in database: {allProducts.Count}");

    Console.WriteLine("\n7. ALL REGISTERED METADATA (from database)");
    foreach (var objMeta in MetadataRegistry.GetAll())
    {
        Console.WriteLine($"   - {objMeta.Name} -> {objMeta.TableName} ({objMeta.Properties.Count} properties)");
    }

    Console.WriteLine("\n=== Demo Complete ===");
}

// ============================================================
// Metadata Initialization - Loads from SQLite Database
// ============================================================
async Task InitializeMetadataAsync(MetaRecordDbContext metaContext)
{
    var seedData = new[]
    {
        new ObjectMetadata
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Product",
            TableName = "Products",
            Properties = new[]
            {
                new PropertyMetadata("Id", "Id", typeof(Guid), true) { IsPrimaryKey = true },
                new PropertyMetadata("Name", "Name", typeof(string), true) { MaxLength = 100, IsUnique = true, Caption = "Product Name" },
                new PropertyMetadata("Price", "Price", typeof(decimal), true) { Caption = "Unit Price" },
                new PropertyMetadata("Quantity", "Quantity", typeof(int), false) { DefaultValue = "0" }
            }
        }
    };

    await MetadataLoader.InitializeAsync(metaContext, seedData);

    MetadataRegistry.LinkType<Product>("Product");

    var store = EntityStore.Current;
    store.EnsureTableExists(Product.Metadata);
    Console.WriteLine($"  [DATA] Entity tables initialized");
}

async Task<IReadOnlyList<WorkflowDefinition>> SeedDemoWorkflowsAsync(WorkflowRepository repository)
{
    var workflows = new[]
    {
        CreateRejectInvalidPriceWorkflow(),
        CreateCreatedLogWorkflow(),
        CreateLowQuantityChangedWorkflow()
    };

    foreach (var workflow in workflows)
        await repository.SaveDefinitionAsync(workflow);

    return workflows;
}

WorkflowDefinition CreateRejectInvalidPriceWorkflow() => new()
{
    Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
    Name = "Reject invalid product price",
    ObjectName = "Product",
    EventName = WorkflowEventName.BeforeSave,
    IsEnabled = true,
    Nodes = new[]
    {
        Node("trigger-1", "trigger.before-save", "{}"),
        ConditionNode("condition-1", "Price", "lessThanOrEqual", "0"),
        Node("reject-1", "action.reject-save", """
        {
          "message": "Price must be greater than zero for {{currentRecord.Name}}."
        }
        """)
    },
    Edges = new[]
    {
        Edge("edge-1", "trigger-1", "success", "condition-1", "input"),
        Edge("edge-2", "condition-1", "true", "reject-1", "input")
    }
};

WorkflowDefinition CreateCreatedLogWorkflow() => new()
{
    Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
    Name = "Write log when product is created",
    ObjectName = "Product",
    EventName = WorkflowEventName.Created,
    IsEnabled = true,
    Nodes = new[]
    {
        Node("trigger-1", "trigger.record-created", "{}"),
        WriteLogNode("log-1", "Created {{currentRecord.Name}} at price {{currentRecord.Price}}.")
    },
    Edges = new[]
    {
        Edge("edge-1", "trigger-1", "success", "log-1", "input")
    }
};

WorkflowDefinition CreateLowQuantityChangedWorkflow() => new()
{
    Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"),
    Name = "Write log when quantity is low",
    ObjectName = "Product",
    EventName = WorkflowEventName.FieldChanged,
    IsEnabled = true,
    Nodes = new[]
    {
        Node("trigger-1", "trigger.field-changed", "{ \"fieldName\": \"Quantity\" }"),
        ConditionNode("condition-1", "Quantity", "lessThan", "10"),
        WriteLogNode("log-1", "Low quantity for {{currentRecord.Name}}: {{currentRecord.Quantity}} remaining.")
    },
    Edges = new[]
    {
        Edge("edge-1", "trigger-1", "success", "condition-1", "input"),
        Edge("edge-2", "condition-1", "true", "log-1", "input")
    }
};

WorkflowNode ConditionNode(string id, string fieldName, string operatorName, string literalValue) => Node(id, "flow.condition", $$"""
{
  "condition": {
    "left": { "source": "currentRecord", "field": "{{fieldName}}" },
    "operator": "{{operatorName}}",
    "right": { "source": "literal", "value": {{literalValue}} }
  }
}
""");

WorkflowNode WriteLogNode(string id, string message) => Node(id, "action.write-log", $$"""
{
  "severity": "Information",
  "message": "{{message}}"
}
""");

WorkflowNode Node(string id, string type, string configJson) => new()
{
    Id = id,
    Type = type,
    Config = Json(configJson)
};

WorkflowEdge Edge(
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

JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

async Task PrintLatestRunAsync(MetaRecordDbContext context, string eventName, string label)
{
    var run = await context.WorkflowRuns
        .AsNoTracking()
        .Include(workflowRun => workflowRun.Steps)
        .Where(workflowRun => workflowRun.EventName == eventName)
        .OrderByDescending(workflowRun => workflowRun.StartedAt)
        .FirstOrDefaultAsync();

    if (run is null)
    {
        Console.WriteLine($"   {label}: no run recorded");
        return;
    }

    Console.WriteLine($"   {label}: {run.Status} ({run.Steps.Count} steps)");
    foreach (var step in run.Steps.OrderBy(step => step.StartedAt))
    {
        Console.WriteLine($"     - {step.NodeId} [{step.NodeType}] {step.Status}");
        if (!string.IsNullOrWhiteSpace(step.OutputJson))
            Console.WriteLine($"       output: {SummarizeOutput(step.OutputJson)}");
        if (!string.IsNullOrWhiteSpace(step.ErrorMessage))
            Console.WriteLine($"       error: {step.ErrorMessage}");
    }
}

void PrintRunResult(WorkflowRunResult result, string label)
{
    Console.WriteLine($"   {label}: {result.Status} ({result.Steps.Count} steps)");
    foreach (var step in result.Steps)
    {
        Console.WriteLine($"     - {step.NodeId} [{step.NodeType}] {step.Status}");
        if (!string.IsNullOrWhiteSpace(step.ErrorMessage))
            Console.WriteLine($"       error: {step.ErrorMessage}");
    }
}

string SummarizeOutput(string outputJson)
{
    using var document = JsonDocument.Parse(outputJson);
    if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("message", out var message))
        return message.GetString() ?? outputJson;

    return outputJson;
}