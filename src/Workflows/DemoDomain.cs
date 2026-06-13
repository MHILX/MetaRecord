namespace MetaRecord.Workflows;

public static class DemoDomain
{
    public const string ObjectName = "Todo";
    public const string TableName = "Todos";
    public const string AuditEntryObjectName = "TodoAuditEntry";
    public const string AuditEntryTableName = "TodoAuditEntries";

    public static class WorkflowNames
    {
        public const string CaptureAuditSnapshot = "Capture todo snapshot";
        public const string RejectInvalidTodoTitle = "Reject empty todo title";
        public const string CreatedLog = "Write log when todo is created";
        public const string CompletedLog = "Write log when todo is completed";
    }
}
