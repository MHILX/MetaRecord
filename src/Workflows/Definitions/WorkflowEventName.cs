namespace MetaRecord.Workflows.Definitions;

/// <summary>
/// Stable workflow event names used by definitions, validation, and runtime dispatch.
/// </summary>
public static class WorkflowEventName
{
    public const string BeforeSave = "BeforeSave";
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string FieldChanged = "FieldChanged";
    public const string Manual = "Manual";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        BeforeSave,
        Created,
        Updated,
        FieldChanged,
        Manual
    };

    public static bool IsKnown(string eventName) => All.Contains(eventName);

    public static bool IsBeforeEvent(string eventName) =>
        string.Equals(eventName, BeforeSave, StringComparison.OrdinalIgnoreCase);

    public static bool IsAfterEvent(string eventName) =>
        string.Equals(eventName, Created, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventName, Updated, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventName, FieldChanged, StringComparison.OrdinalIgnoreCase);

    public static bool IsManualEvent(string eventName) =>
        string.Equals(eventName, Manual, StringComparison.OrdinalIgnoreCase);
}