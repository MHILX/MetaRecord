using MetaRecord.Workflows.Definitions;

namespace MetaRecord.Workflows.Catalog;

public sealed class WorkflowNodeCatalog
{
    private readonly Dictionary<string, WorkflowNodeType> _types;

    public static WorkflowNodeCatalog Default { get; } = CreateDefault();

    public WorkflowNodeCatalog(IEnumerable<WorkflowNodeType> nodeTypes)
    {
        _types = nodeTypes.ToDictionary(nodeType => nodeType.Type, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<WorkflowNodeType> All => _types.Values.ToList();

    public bool TryGet(string type, out WorkflowNodeType? nodeType) => _types.TryGetValue(type, out nodeType);

    private static WorkflowNodeCatalog CreateDefault()
    {
        return new WorkflowNodeCatalog(new[]
        {
            Trigger("trigger.record-created", WorkflowEventName.Created, "Record Created", "Runs after a record is inserted."),
            Trigger("trigger.record-updated", WorkflowEventName.Updated, "Record Updated", "Runs after a record is updated."),
            Trigger("trigger.before-save", WorkflowEventName.BeforeSave, "Before Save", "Runs before a record is inserted or updated."),
            Trigger("trigger.field-changed", WorkflowEventName.FieldChanged, "Field Changed", "Runs after a configured field changes.",
                new NodeConfigSchema
                {
                    Fields = new[]
                    {
                        Required("fieldName", "Field", NodeConfigFieldKind.PropertyName)
                    }
                }),
            Trigger("trigger.manual", WorkflowEventName.Manual, "Manual Run", "Runs from an explicit test or manual invocation."),
            new WorkflowNodeType
            {
                Type = "flow.condition",
                Category = WorkflowNodeCategory.Flow,
                DisplayName = "Condition",
                Description = "Branches based on a structured condition.",
                InputPorts = new[] { Port("input", "Input") },
                OutputPorts = new[] { Port("true", "True"), Port("false", "False") },
                ConfigSchema = new NodeConfigSchema
                {
                    Fields = new[]
                    {
                        Required("condition", "Condition", NodeConfigFieldKind.Condition)
                    }
                }
            },
            new WorkflowNodeType
            {
                Type = "flow.stop",
                Category = WorkflowNodeCategory.Flow,
                DisplayName = "Stop",
                Description = "Stops the current workflow branch.",
                InputPorts = new[] { Port("input", "Input") },
                ConfigSchema = new NodeConfigSchema
                {
                    Fields = new[]
                    {
                        Optional("reason", "Reason", NodeConfigFieldKind.Template)
                    }
                }
            },
            new WorkflowNodeType
            {
                Type = "action.set-field",
                Category = WorkflowNodeCategory.Action,
                DisplayName = "Set Field",
                Description = "Sets a field on the current record before it is saved.",
                Timing = WorkflowNodeTiming.BeforeOnly,
                InputPorts = new[] { Port("input", "Input") },
                OutputPorts = new[] { Port("success", "Success") },
                ConfigSchema = new NodeConfigSchema
                {
                    Fields = new[]
                    {
                        Required("fieldName", "Field", NodeConfigFieldKind.PropertyName),
                        Required("value", "Value", NodeConfigFieldKind.Template)
                    }
                }
            },
            new WorkflowNodeType
            {
                Type = "action.reject-save",
                Category = WorkflowNodeCategory.Action,
                DisplayName = "Reject Save",
                Description = "Rejects the current before-save operation with a message.",
                Timing = WorkflowNodeTiming.BeforeOnly,
                InputPorts = new[] { Port("input", "Input") },
                ConfigSchema = new NodeConfigSchema
                {
                    Fields = new[]
                    {
                        Required("message", "Message", NodeConfigFieldKind.Template)
                    }
                }
            },
            new WorkflowNodeType
            {
                Type = "action.create-record",
                Category = WorkflowNodeCategory.Action,
                DisplayName = "Create Record",
                Description = "Creates another metadata-backed record.",
                InputPorts = new[] { Port("input", "Input") },
                OutputPorts = new[] { Port("success", "Success") },
                ConfigSchema = new NodeConfigSchema
                {
                    Fields = new[]
                    {
                        Required("targetObjectName", "Target Object", NodeConfigFieldKind.ObjectName),
                        Required("fieldMappings", "Field Mappings", NodeConfigFieldKind.FieldMappings, "targetObjectName")
                    }
                }
            },
            new WorkflowNodeType
            {
                Type = "action.write-log",
                Category = WorkflowNodeCategory.Action,
                DisplayName = "Write Log",
                Description = "Writes a structured workflow log message.",
                InputPorts = new[] { Port("input", "Input") },
                OutputPorts = new[] { Port("success", "Success") },
                ConfigSchema = new NodeConfigSchema
                {
                    Fields = new[]
                    {
                        Required("severity", "Severity", NodeConfigFieldKind.Text),
                        Required("message", "Message", NodeConfigFieldKind.Template)
                    }
                }
            }
        });
    }

    private static WorkflowNodeType Trigger(
        string type,
        string eventName,
        string displayName,
        string description,
        NodeConfigSchema? configSchema = null)
    {
        return new WorkflowNodeType
        {
            Type = type,
            Category = WorkflowNodeCategory.Trigger,
            DisplayName = displayName,
            Description = description,
            TriggerEventName = eventName,
            Timing = GetTriggerTiming(eventName),
            OutputPorts = new[] { Port("success", "Success") },
            ConfigSchema = configSchema ?? NodeConfigSchema.Empty
        };
    }

    private static WorkflowNodeTiming GetTriggerTiming(string eventName)
    {
        if (WorkflowEventName.IsBeforeEvent(eventName))
            return WorkflowNodeTiming.BeforeOnly;
        if (WorkflowEventName.IsAfterEvent(eventName))
            return WorkflowNodeTiming.AfterOnly;
        return WorkflowNodeTiming.ManualOnly;
    }

    private static NodePortDefinition Port(string name, string displayName) => new()
    {
        Name = name,
        DisplayName = displayName
    };

    private static NodeConfigField Required(
        string name,
        string label,
        NodeConfigFieldKind kind,
        string? objectNameField = null) => new()
    {
        Name = name,
        Label = label,
        Kind = kind,
        IsRequired = true,
        ObjectNameField = objectNameField
    };

    private static NodeConfigField Optional(
        string name,
        string label,
        NodeConfigFieldKind kind,
        string? objectNameField = null) => new()
    {
        Name = name,
        Label = label,
        Kind = kind,
        ObjectNameField = objectNameField
    };
}