using System.Text.Json;
using MetaRecord.Models;
using MetaRecord.Workflows.Catalog;
using MetaRecord.Workflows.Definitions;

namespace MetaRecord.Workflows.Validation;

public sealed class WorkflowValidator
{
    private static readonly HashSet<string> SupportedConditionOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "equals",
        "notEquals",
        "greaterThan",
        "greaterThanOrEqual",
        "lessThan",
        "lessThanOrEqual",
        "contains",
        "startsWith",
        "endsWith",
        "isEmpty",
        "isNotEmpty"
    };

    private static readonly HashSet<string> SupportedOperandSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "currentRecord",
        "originalRecord",
        "event",
        "literal",
        "variable"
    };

    private static readonly HashSet<string> SupportedEventFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "WorkflowId",
        "RunId",
        "ObjectName",
        "EventName",
        "RecordId"
    };

    private readonly WorkflowNodeCatalog _catalog;

    public WorkflowValidator(WorkflowNodeCatalog? catalog = null)
    {
        _catalog = catalog ?? WorkflowNodeCatalog.Default;
    }

    public IReadOnlyList<WorkflowValidationIssue> Validate(WorkflowDefinition workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var issues = new List<WorkflowValidationIssue>();
        var workflowMetadata = ValidateWorkflowHeader(workflow, issues);
        var nodesById = BuildNodeMap(workflow, issues);
        var nodeTypesById = ValidateNodeTypes(workflow, nodesById, issues);
        var triggerNodes = ValidateTrigger(workflow, nodeTypesById, issues);

        ValidateEdges(workflow, nodesById, nodeTypesById, issues);
        ValidateNoCycles(workflow, nodesById, issues);
        ValidateReachability(workflow, nodesById, nodeTypesById, triggerNodes, issues);
        ValidateNodeConfigs(workflow, workflowMetadata, nodesById, nodeTypesById, issues);

        return issues;
    }

    private static IObjectMetadata? ValidateWorkflowHeader(
        WorkflowDefinition workflow,
        List<WorkflowValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(workflow.Name))
            issues.Add(Error("Workflow name is required.", field: "name"));

        IObjectMetadata? workflowMetadata = null;
        if (string.IsNullOrWhiteSpace(workflow.ObjectName))
        {
            issues.Add(Error("Workflow object name is required.", field: "objectName"));
        }
        else if (!MetadataRegistry.TryGetMetadata(workflow.ObjectName, out workflowMetadata))
        {
            issues.Add(Error($"Object '{workflow.ObjectName}' does not exist in metadata.", field: "objectName"));
        }

        if (string.IsNullOrWhiteSpace(workflow.EventName))
        {
            issues.Add(Error("Workflow event name is required.", field: "eventName"));
        }
        else if (!WorkflowEventName.IsKnown(workflow.EventName))
        {
            issues.Add(Error($"Event '{workflow.EventName}' is not supported.", field: "eventName"));
        }

        return workflowMetadata;
    }

    private static Dictionary<string, WorkflowNode> BuildNodeMap(
        WorkflowDefinition workflow,
        List<WorkflowValidationIssue> issues)
    {
        var nodesById = new Dictionary<string, WorkflowNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in workflow.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                issues.Add(Error("Node id is required."));
                continue;
            }

            if (!nodesById.TryAdd(node.Id, node))
            {
                issues.Add(Error($"Node id '{node.Id}' is duplicated.", node.Id));
            }
        }

        return nodesById;
    }

    private Dictionary<string, WorkflowNodeType> ValidateNodeTypes(
        WorkflowDefinition workflow,
        Dictionary<string, WorkflowNode> nodesById,
        List<WorkflowValidationIssue> issues)
    {
        var nodeTypesById = new Dictionary<string, WorkflowNodeType>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodesById.Values)
        {
            if (string.IsNullOrWhiteSpace(node.Type))
            {
                issues.Add(Error("Node type is required.", node.Id, "type"));
                continue;
            }

            if (!_catalog.TryGet(node.Type, out var nodeType) || nodeType is null)
            {
                issues.Add(Error($"Node type '{node.Type}' is not registered in the workflow node catalog.", node.Id, "type"));
                continue;
            }

            nodeTypesById[node.Id] = nodeType;

            if (!nodeType.IsAllowedForEvent(workflow.EventName))
            {
                issues.Add(Error(
                    $"Node type '{node.Type}' is not allowed for event '{workflow.EventName}'.",
                    node.Id,
                    "type"));
            }
        }

        return nodeTypesById;
    }

    private static IReadOnlyList<WorkflowNode> ValidateTrigger(
        WorkflowDefinition workflow,
        Dictionary<string, WorkflowNodeType> nodeTypesById,
        List<WorkflowValidationIssue> issues)
    {
        var triggerNodes = workflow.Nodes
            .Where(node => nodeTypesById.TryGetValue(node.Id, out var nodeType) && nodeType.IsTrigger)
            .ToList();

        if (triggerNodes.Count == 0)
        {
            issues.Add(Error("Workflow must contain exactly one trigger node."));
            return triggerNodes;
        }

        if (triggerNodes.Count > 1)
        {
            issues.Add(Error("Workflow must contain only one trigger node."));
        }

        foreach (var triggerNode in triggerNodes)
        {
            var triggerType = nodeTypesById[triggerNode.Id];
            if (!string.Equals(triggerType.TriggerEventName, workflow.EventName, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(Error(
                    $"Trigger node '{triggerNode.Type}' does not match workflow event '{workflow.EventName}'.",
                    triggerNode.Id,
                    "type"));
            }
        }

        return triggerNodes;
    }

    private static void ValidateEdges(
        WorkflowDefinition workflow,
        Dictionary<string, WorkflowNode> nodesById,
        Dictionary<string, WorkflowNodeType> nodeTypesById,
        List<WorkflowValidationIssue> issues)
    {
        var edgeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in workflow.Edges)
        {
            if (string.IsNullOrWhiteSpace(edge.Id))
            {
                issues.Add(Error("Edge id is required."));
            }
            else if (!edgeIds.Add(edge.Id))
            {
                issues.Add(Error($"Edge id '{edge.Id}' is duplicated."));
            }

            var hasSourceNode = nodesById.ContainsKey(edge.FromNodeId);
            var hasTargetNode = nodesById.ContainsKey(edge.ToNodeId);

            if (!hasSourceNode)
                issues.Add(Error($"Edge '{edge.Id}' references missing source node '{edge.FromNodeId}'."));
            if (!hasTargetNode)
                issues.Add(Error($"Edge '{edge.Id}' references missing target node '{edge.ToNodeId}'."));

            if (hasSourceNode && nodeTypesById.TryGetValue(edge.FromNodeId, out var sourceType))
            {
                ValidatePort(sourceType.OutputPorts, edge.FromPort, edge.FromNodeId, "fromPort", "output", issues);
            }

            if (hasTargetNode && nodeTypesById.TryGetValue(edge.ToNodeId, out var targetType))
            {
                ValidatePort(targetType.InputPorts, edge.ToPort, edge.ToNodeId, "toPort", "input", issues);
            }
        }
    }

    private static void ValidatePort(
        IReadOnlyList<NodePortDefinition> ports,
        string portName,
        string nodeId,
        string field,
        string direction,
        List<WorkflowValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            issues.Add(Error($"Edge {direction} port is required.", nodeId, field));
            return;
        }

        if (!ports.Any(port => string.Equals(port.Name, portName, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(Error($"Port '{portName}' is not a valid {direction} port for this node type.", nodeId, field));
        }
    }

    private static void ValidateNoCycles(
        WorkflowDefinition workflow,
        Dictionary<string, WorkflowNode> nodesById,
        List<WorkflowValidationIssue> issues)
    {
        var adjacency = BuildAdjacency(workflow, nodesById);
        var visitStates = new Dictionary<string, VisitState>(StringComparer.OrdinalIgnoreCase);

        foreach (var nodeId in nodesById.Keys)
        {
            if (HasCycle(nodeId, adjacency, visitStates))
            {
                issues.Add(Error("Workflow graph contains a cycle."));
                return;
            }
        }
    }

    private static void ValidateReachability(
        WorkflowDefinition workflow,
        Dictionary<string, WorkflowNode> nodesById,
        Dictionary<string, WorkflowNodeType> nodeTypesById,
        IReadOnlyList<WorkflowNode> triggerNodes,
        List<WorkflowValidationIssue> issues)
    {
        if (triggerNodes.Count != 1)
            return;

        var adjacency = BuildAdjacency(workflow, nodesById);
        var reachableNodeIds = GetReachableNodeIds(triggerNodes[0].Id, adjacency);

        foreach (var node in nodesById.Values)
        {
            if (nodeTypesById.TryGetValue(node.Id, out var nodeType) && nodeType.IsTrigger)
                continue;

            if (!reachableNodeIds.Contains(node.Id))
            {
                issues.Add(Error($"Node '{node.Id}' is not reachable from the trigger.", node.Id));
            }
        }
    }

    private static Dictionary<string, List<string>> BuildAdjacency(
        WorkflowDefinition workflow,
        Dictionary<string, WorkflowNode> nodesById)
    {
        var adjacency = nodesById.Keys.ToDictionary(nodeId => nodeId, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var edge in workflow.Edges)
        {
            if (nodesById.ContainsKey(edge.FromNodeId) && nodesById.ContainsKey(edge.ToNodeId))
            {
                adjacency[edge.FromNodeId].Add(edge.ToNodeId);
            }
        }

        return adjacency;
    }

    private static bool HasCycle(
        string nodeId,
        Dictionary<string, List<string>> adjacency,
        Dictionary<string, VisitState> visitStates)
    {
        if (visitStates.TryGetValue(nodeId, out var visitState))
            return visitState == VisitState.Visiting;

        visitStates[nodeId] = VisitState.Visiting;

        foreach (var targetNodeId in adjacency.GetValueOrDefault(nodeId) ?? Enumerable.Empty<string>())
        {
            if (HasCycle(targetNodeId, adjacency, visitStates))
                return true;
        }

        visitStates[nodeId] = VisitState.Visited;
        return false;
    }

    private static HashSet<string> GetReachableNodeIds(
        string triggerNodeId,
        Dictionary<string, List<string>> adjacency)
    {
        var reachableNodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingNodeIds = new Stack<string>();
        pendingNodeIds.Push(triggerNodeId);

        while (pendingNodeIds.Count > 0)
        {
            var currentNodeId = pendingNodeIds.Pop();
            if (!reachableNodeIds.Add(currentNodeId))
                continue;

            foreach (var targetNodeId in adjacency.GetValueOrDefault(currentNodeId) ?? Enumerable.Empty<string>())
            {
                pendingNodeIds.Push(targetNodeId);
            }
        }

        return reachableNodeIds;
    }

    private static void ValidateNodeConfigs(
        WorkflowDefinition workflow,
        IObjectMetadata? workflowMetadata,
        Dictionary<string, WorkflowNode> nodesById,
        Dictionary<string, WorkflowNodeType> nodeTypesById,
        List<WorkflowValidationIssue> issues)
    {
        foreach (var node in nodesById.Values)
        {
            if (!nodeTypesById.TryGetValue(node.Id, out var nodeType))
                continue;

            foreach (var configField in nodeType.ConfigSchema.Fields)
            {
                if (!TryGetProperty(node.Config, configField.Name, out var configValue))
                {
                    if (configField.IsRequired)
                    {
                        issues.Add(Error(
                            $"Config field '{configField.Name}' is required.",
                            node.Id,
                            $"config.{configField.Name}"));
                    }

                    continue;
                }

                if (IsMissingValue(configValue))
                {
                    if (configField.IsRequired)
                    {
                        issues.Add(Error(
                            $"Config field '{configField.Name}' is required.",
                            node.Id,
                            $"config.{configField.Name}"));
                    }

                    continue;
                }

                ValidateConfigField(workflow, workflowMetadata, node, configField, configValue, issues);
            }
        }
    }

    private static void ValidateConfigField(
        WorkflowDefinition workflow,
        IObjectMetadata? workflowMetadata,
        WorkflowNode node,
        NodeConfigField configField,
        JsonElement configValue,
        List<WorkflowValidationIssue> issues)
    {
        switch (configField.Kind)
        {
            case NodeConfigFieldKind.Text:
            case NodeConfigFieldKind.Template:
                ValidateStringConfigValue(node, configField, configValue, issues);
                break;
            case NodeConfigFieldKind.ObjectName:
                ValidateObjectNameConfigValue(node, configField, configValue, issues);
                break;
            case NodeConfigFieldKind.PropertyName:
                ValidatePropertyNameConfigValue(workflow, workflowMetadata, node, configField, configValue, issues);
                break;
            case NodeConfigFieldKind.Condition:
                ValidateConditionConfigValue(workflowMetadata, node, configField, configValue, issues);
                break;
            case NodeConfigFieldKind.FieldMappings:
                ValidateFieldMappingsConfigValue(node, configField, configValue, issues);
                break;
            default:
                issues.Add(Error($"Config field kind '{configField.Kind}' is not supported.", node.Id, $"config.{configField.Name}"));
                break;
        }
    }

    private static void ValidateStringConfigValue(
        WorkflowNode node,
        NodeConfigField configField,
        JsonElement configValue,
        List<WorkflowValidationIssue> issues)
    {
        if (configValue.ValueKind != JsonValueKind.String)
        {
            issues.Add(Error(
                $"Config field '{configField.Name}' must be a string.",
                node.Id,
                $"config.{configField.Name}"));
        }
    }

    private static void ValidateObjectNameConfigValue(
        WorkflowNode node,
        NodeConfigField configField,
        JsonElement configValue,
        List<WorkflowValidationIssue> issues)
    {
        if (configValue.ValueKind != JsonValueKind.String)
        {
            issues.Add(Error(
                $"Config field '{configField.Name}' must be an object name string.",
                node.Id,
                $"config.{configField.Name}"));
            return;
        }

        var objectName = configValue.GetString();
        if (string.IsNullOrWhiteSpace(objectName))
        {
            issues.Add(Error(
                $"Config field '{configField.Name}' must specify an object name.",
                node.Id,
                $"config.{configField.Name}"));
            return;
        }

        if (!MetadataRegistry.TryGetMetadata(objectName, out _))
        {
            issues.Add(Error(
                $"Object '{objectName}' does not exist in metadata.",
                node.Id,
                $"config.{configField.Name}"));
        }
    }

    private static void ValidatePropertyNameConfigValue(
        WorkflowDefinition workflow,
        IObjectMetadata? workflowMetadata,
        WorkflowNode node,
        NodeConfigField configField,
        JsonElement configValue,
        List<WorkflowValidationIssue> issues)
    {
        if (configValue.ValueKind != JsonValueKind.String)
        {
            issues.Add(Error(
                $"Config field '{configField.Name}' must be a property name string.",
                node.Id,
                $"config.{configField.Name}"));
            return;
        }

        var propertyName = configValue.GetString();
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            issues.Add(Error(
                $"Config field '{configField.Name}' must specify a property name.",
                node.Id,
                $"config.{configField.Name}"));
            return;
        }

        var objectMetadata = ResolveObjectMetadata(workflow, workflowMetadata, node, configField, issues);
        if (objectMetadata is null)
            return;

        if (!HasProperty(objectMetadata, propertyName))
        {
            issues.Add(Error(
                $"Field '{propertyName}' does not exist on object '{objectMetadata.Name}'.",
                node.Id,
                $"config.{configField.Name}"));
        }
    }

    private static IObjectMetadata? ResolveObjectMetadata(
        WorkflowDefinition workflow,
        IObjectMetadata? workflowMetadata,
        WorkflowNode node,
        NodeConfigField configField,
        List<WorkflowValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(configField.ObjectNameField))
            return workflowMetadata;

        if (!TryGetString(node.Config, configField.ObjectNameField, out var objectName))
            return null;

        if (MetadataRegistry.TryGetMetadata(objectName, out var objectMetadata))
            return objectMetadata;

        issues.Add(Error(
            $"Object '{objectName}' does not exist in metadata.",
            node.Id,
            $"config.{configField.ObjectNameField}"));
        return null;
    }

    private static void ValidateConditionConfigValue(
        IObjectMetadata? workflowMetadata,
        WorkflowNode node,
        NodeConfigField configField,
        JsonElement configValue,
        List<WorkflowValidationIssue> issues)
    {
        if (configValue.ValueKind != JsonValueKind.Object)
        {
            issues.Add(Error(
                $"Config field '{configField.Name}' must be a condition object.",
                node.Id,
                $"config.{configField.Name}"));
            return;
        }

        if (!TryGetProperty(configValue, "left", out var leftOperand))
        {
            issues.Add(Error("Condition must include a left operand.", node.Id, $"config.{configField.Name}.left"));
        }
        else
        {
            ValidateConditionOperand(workflowMetadata, node, leftOperand, $"config.{configField.Name}.left", issues);
        }

        if (!TryGetProperty(configValue, "operator", out var operatorValue) ||
            operatorValue.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(operatorValue.GetString()))
        {
            issues.Add(Error("Condition must include an operator.", node.Id, $"config.{configField.Name}.operator"));
        }
        else if (!SupportedConditionOperators.Contains(operatorValue.GetString()!))
        {
            issues.Add(Error(
                $"Condition operator '{operatorValue.GetString()}' is not supported.",
                node.Id,
                $"config.{configField.Name}.operator"));
        }

        var requiresRightOperand = operatorValue.ValueKind != JsonValueKind.String ||
            !string.Equals(operatorValue.GetString(), "isEmpty", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(operatorValue.GetString(), "isNotEmpty", StringComparison.OrdinalIgnoreCase);

        if (requiresRightOperand)
        {
            if (!TryGetProperty(configValue, "right", out var rightOperand))
            {
                issues.Add(Error("Condition must include a right operand.", node.Id, $"config.{configField.Name}.right"));
            }
            else
            {
                ValidateConditionOperand(workflowMetadata, node, rightOperand, $"config.{configField.Name}.right", issues);
            }
        }
    }

    private static void ValidateConditionOperand(
        IObjectMetadata? workflowMetadata,
        WorkflowNode node,
        JsonElement operand,
        string fieldPath,
        List<WorkflowValidationIssue> issues)
    {
        if (operand.ValueKind != JsonValueKind.Object)
        {
            issues.Add(Error("Condition operand must be an object.", node.Id, fieldPath));
            return;
        }

        if (!TryGetString(operand, "source", out var source))
        {
            issues.Add(Error("Condition operand must include a source.", node.Id, $"{fieldPath}.source"));
            return;
        }

        if (!SupportedOperandSources.Contains(source))
        {
            issues.Add(Error($"Condition operand source '{source}' is not supported.", node.Id, $"{fieldPath}.source"));
            return;
        }

        if (string.Equals(source, "currentRecord", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, "originalRecord", StringComparison.OrdinalIgnoreCase))
        {
            ValidateRecordOperand(workflowMetadata, node, operand, fieldPath, source, issues);
            return;
        }

        if (string.Equals(source, "event", StringComparison.OrdinalIgnoreCase))
        {
            ValidateEventOperand(node, operand, fieldPath, issues);
            return;
        }

        if (string.Equals(source, "variable", StringComparison.OrdinalIgnoreCase) &&
            !TryGetString(operand, "name", out _))
        {
            issues.Add(Error("Variable operands must include a variable name.", node.Id, $"{fieldPath}.name"));
        }
    }

    private static void ValidateRecordOperand(
        IObjectMetadata? workflowMetadata,
        WorkflowNode node,
        JsonElement operand,
        string fieldPath,
        string source,
        List<WorkflowValidationIssue> issues)
    {
        if (!TryGetString(operand, "field", out var propertyName))
        {
            issues.Add(Error($"{source} operands must include a field.", node.Id, $"{fieldPath}.field"));
            return;
        }

        if (workflowMetadata is not null && !HasProperty(workflowMetadata, propertyName))
        {
            issues.Add(Error(
                $"Field '{propertyName}' does not exist on object '{workflowMetadata.Name}'.",
                node.Id,
                $"{fieldPath}.field"));
        }
    }

    private static void ValidateEventOperand(
        WorkflowNode node,
        JsonElement operand,
        string fieldPath,
        List<WorkflowValidationIssue> issues)
    {
        if (!TryGetString(operand, "field", out var eventField))
        {
            issues.Add(Error("Event operands must include a field.", node.Id, $"{fieldPath}.field"));
            return;
        }

        if (!SupportedEventFields.Contains(eventField))
        {
            issues.Add(Error($"Event field '{eventField}' is not supported.", node.Id, $"{fieldPath}.field"));
        }
    }

    private static void ValidateFieldMappingsConfigValue(
        WorkflowNode node,
        NodeConfigField configField,
        JsonElement configValue,
        List<WorkflowValidationIssue> issues)
    {
        if (configValue.ValueKind != JsonValueKind.Object)
        {
            issues.Add(Error(
                $"Config field '{configField.Name}' must be an object containing target field mappings.",
                node.Id,
                $"config.{configField.Name}"));
            return;
        }

        if (string.IsNullOrWhiteSpace(configField.ObjectNameField) ||
            !TryGetString(node.Config, configField.ObjectNameField, out var objectName) ||
            !MetadataRegistry.TryGetMetadata(objectName, out var objectMetadata) ||
            objectMetadata is null)
        {
            return;
        }

        foreach (var mapping in configValue.EnumerateObject())
        {
            if (!HasProperty(objectMetadata, mapping.Name))
            {
                issues.Add(Error(
                    $"Mapped field '{mapping.Name}' does not exist on object '{objectMetadata.Name}'.",
                    node.Id,
                    $"config.{configField.Name}.{mapping.Name}"));
            }
        }
    }

    private static bool HasProperty(IObjectMetadata metadata, string propertyName) =>
        metadata.Properties.Any(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement propertyValue)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            propertyValue = default;
            return false;
        }

        return element.TryGetProperty(propertyName, out propertyValue);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;

        if (!TryGetProperty(element, propertyName, out var propertyValue) || propertyValue.ValueKind != JsonValueKind.String)
            return false;

        var stringValue = propertyValue.GetString();
        if (string.IsNullOrWhiteSpace(stringValue))
            return false;

        value = stringValue;
        return true;
    }

    private static bool IsMissingValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined => true,
        JsonValueKind.Null => true,
        JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()),
        _ => false
    };

    private static WorkflowValidationIssue Error(string message, string? nodeId = null, string? field = null) =>
        new(WorkflowValidationSeverity.Error, message, nodeId, field);

    private enum VisitState
    {
        Visiting,
        Visited
    }
}