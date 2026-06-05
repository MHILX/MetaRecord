using MetaRecord.Models;

namespace MetaRecord.Workflows;

public static class DemoMetadataSeeder
{
    public static IReadOnlyList<IObjectMetadata> CreateDemoMetadata() => new IObjectMetadata[]
    {
        CreateProductMetadata(),
        CreateWorkflowAuditEntryMetadata()
    };

    private static IObjectMetadata CreateProductMetadata() => new ObjectMetadata
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
    };

    private static IObjectMetadata CreateWorkflowAuditEntryMetadata() => new ObjectMetadata
    {
        Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Name = "WorkflowAuditEntry",
        TableName = "WorkflowAuditEntries",
        Properties = new[]
        {
            new PropertyMetadata("Id", "Id", typeof(Guid), true) { IsPrimaryKey = true },
            new PropertyMetadata("ProductName", "ProductName", typeof(string), true) { MaxLength = 100, Caption = "Product Name" },
            new PropertyMetadata("ProductPrice", "ProductPrice", typeof(decimal), true) { Caption = "Product Price" },
            new PropertyMetadata("Quantity", "Quantity", typeof(int), true) { Caption = "Quantity" },
            new PropertyMetadata("WorkflowId", "WorkflowId", typeof(Guid), true) { Caption = "Workflow Id" },
            new PropertyMetadata("EventName", "EventName", typeof(string), true) { MaxLength = 100, Caption = "Event Name" },
            new PropertyMetadata("Note", "Note", typeof(string), true) { MaxLength = 250, Caption = "Note" }
        }
    };
}