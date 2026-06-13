using MetaRecord.Models;

namespace MetaRecord.Workflows;

public static class DemoMetadataSeeder
{
    public static IReadOnlyList<IObjectMetadata> CreateDemoMetadata() => new IObjectMetadata[]
    {
        CreateTodoMetadata(),
        CreateTodoAuditEntryMetadata()
    };

    private static IObjectMetadata CreateTodoMetadata() => new ObjectMetadata
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Name = DemoDomain.ObjectName,
        TableName = DemoDomain.TableName,
        Properties = new[]
        {
            new PropertyMetadata("Id", "Id", typeof(Guid), true) { IsPrimaryKey = true },
            new PropertyMetadata("Title", "Title", typeof(string), true) { MaxLength = 150, Caption = "Todo Title" },
            new PropertyMetadata("Description", "Description", typeof(string), false) { MaxLength = 500, Caption = "Description" },
            new PropertyMetadata("Status", "Status", typeof(string), true) { MaxLength = 40, DefaultValue = "Open", Caption = "Status" },
            new PropertyMetadata("Priority", "Priority", typeof(int), false) { DefaultValue = "3", Caption = "Priority" }
        }
    };

    private static IObjectMetadata CreateTodoAuditEntryMetadata() => new ObjectMetadata
    {
        Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Name = DemoDomain.AuditEntryObjectName,
        TableName = DemoDomain.AuditEntryTableName,
        Properties = new[]
        {
            new PropertyMetadata("Id", "Id", typeof(Guid), true) { IsPrimaryKey = true },
            new PropertyMetadata("TodoTitle", "TodoTitle", typeof(string), true) { MaxLength = 150, Caption = "Todo Title" },
            new PropertyMetadata("TodoStatus", "TodoStatus", typeof(string), true) { MaxLength = 40, Caption = "Todo Status" },
            new PropertyMetadata("TodoPriority", "TodoPriority", typeof(int), true) { Caption = "Priority" },
            new PropertyMetadata("WorkflowId", "WorkflowId", typeof(Guid), true) { Caption = "Workflow Id" },
            new PropertyMetadata("EventName", "EventName", typeof(string), true) { MaxLength = 100, Caption = "Event Name" },
            new PropertyMetadata("Note", "Note", typeof(string), true) { MaxLength = 250, Caption = "Note" }
        }
    };
}