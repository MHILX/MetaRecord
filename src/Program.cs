using System.Text.Json;
using MetaRecord.Data;
using MetaRecord.Models;
using MetaRecord.Services;
using MetaRecord.Workflows;
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
    var demoWorkflows = await DemoWorkflowSeeder.SeedAsync(repository);
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
    await MetadataLoader.InitializeAsync(metaContext, DemoMetadataSeeder.CreateDemoMetadata());

    MetadataRegistry.LinkType<Product>("Product");

    var store = EntityStore.Current;
    foreach (var metadata in MetadataRegistry.GetAll())
        store.EnsureTableExists(metadata);
    Console.WriteLine($"  [DATA] Entity tables initialized");
}

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