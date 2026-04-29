import type {
  NodeConfigField,
  ObjectMetadata,
  PropertyMetadata,
  WorkflowConfigObject,
  WorkflowConfigValue,
  WorkflowDefinition,
  WorkflowEdge,
  WorkflowEventName,
  WorkflowNode,
  WorkflowNodeType,
  WorkflowValidationIssue
} from '../api/types';

export const workflowEvents: WorkflowEventName[] = ['BeforeSave', 'Created', 'Updated', 'FieldChanged', 'Manual'];

export function createWorkflowDraft(
  name: string,
  objectName: string,
  eventName: WorkflowEventName,
  metadataObjects: ObjectMetadata[],
  nodeTypes: WorkflowNodeType[]
): WorkflowDefinition {
  const triggerType = nodeTypes.find(nodeType => nodeType.triggerEventName === eventName) ??
    nodeTypes.find(nodeType => nodeType.type === 'trigger.manual');

  if (!triggerType)
    throw new Error('The node catalog does not include a trigger node.');

  return {
    id: crypto.randomUUID(),
    name: name.trim() || `New ${eventName} workflow`,
    objectName,
    eventName,
    isEnabled: false,
    version: 1,
    nodes: [
      {
        id: 'trigger-1',
        type: triggerType.type,
        label: triggerType.displayName,
        position: { x: 80, y: 140 },
        config: createDefaultConfig(triggerType, objectName, metadataObjects)
      }
    ],
    edges: []
  };
}

export function createNodeDraft(
  nodeType: WorkflowNodeType,
  workflow: WorkflowDefinition,
  metadataObjects: ObjectMetadata[]
): WorkflowNode {
  const index = workflow.nodes.length + 1;
  return {
    id: `${nodeType.type.replace(/[^a-z0-9]+/gi, '-')}-${crypto.randomUUID().slice(0, 8)}`,
    type: nodeType.type,
    label: nodeType.displayName,
    position: { x: 320 + (index % 3) * 180, y: 100 + Math.floor(index / 3) * 140 },
    config: createDefaultConfig(nodeType, workflow.objectName, metadataObjects)
  };
}

export function createDefaultConfig(
  nodeType: WorkflowNodeType,
  workflowObjectName: string,
  metadataObjects: ObjectMetadata[]
): WorkflowConfigObject {
  const config: WorkflowConfigObject = {};

  for (const field of nodeType.configSchema.fields) {
    config[field.name] = createDefaultFieldValue(field, config, workflowObjectName, metadataObjects);
  }

  if (nodeType.type === 'action.write-log') {
    config.severity = config.severity || 'Information';
    config.message = config.message || 'Workflow ran for {{currentRecord.Name}}.';
  }

  if (nodeType.type === 'flow.stop') {
    config.reason = config.reason || 'Stopped by workflow.';
  }

  return config;
}

export function isNodeAllowedForWorkflow(nodeType: WorkflowNodeType, eventName: WorkflowEventName): boolean {
  if (nodeType.isTrigger)
    return false;

  if (nodeType.timing === 'Any')
    return true;
  if (nodeType.timing === 'BeforeOnly')
    return eventName === 'BeforeSave';
  if (nodeType.timing === 'AfterOnly')
    return eventName === 'Created' || eventName === 'Updated' || eventName === 'FieldChanged';
  if (nodeType.timing === 'ManualOnly')
    return eventName === 'Manual';

  return false;
}

export function getObject(metadataObjects: ObjectMetadata[], objectName: string): ObjectMetadata | undefined {
  return metadataObjects.find(metadata => metadata.name.toLowerCase() === objectName.toLowerCase());
}

export function getProperty(metadataObjects: ObjectMetadata[], objectName: string, propertyName: string): PropertyMetadata | undefined {
  return getObject(metadataObjects, objectName)?.properties.find(property => property.name.toLowerCase() === propertyName.toLowerCase());
}

export function getConfigObject(value: unknown): WorkflowConfigObject {
  if (value && typeof value === 'object' && !Array.isArray(value))
    return value as WorkflowConfigObject;

  return {};
}

export function getNodeType(nodeTypes: WorkflowNodeType[], type: string): WorkflowNodeType | undefined {
  return nodeTypes.find(nodeType => nodeType.type.toLowerCase() === type.toLowerCase());
}

export function getNodeIssues(issues: WorkflowValidationIssue[], nodeId: string): WorkflowValidationIssue[] {
  return issues.filter(issue => issue.nodeId?.toLowerCase() === nodeId.toLowerCase());
}

export function updateNode(workflow: WorkflowDefinition, nodeId: string, updater: (node: WorkflowNode) => WorkflowNode): WorkflowDefinition {
  return {
    ...workflow,
    nodes: workflow.nodes.map(node => node.id === nodeId ? updater(node) : node)
  };
}

export function updateWorkflowEdges(workflow: WorkflowDefinition, edges: WorkflowEdge[]): WorkflowDefinition {
  return { ...workflow, edges };
}

export function workflowHasEdge(workflow: WorkflowDefinition, edge: Omit<WorkflowEdge, 'id'>): boolean {
  return workflow.edges.some(existing =>
    existing.fromNodeId === edge.fromNodeId &&
    existing.fromPort === edge.fromPort &&
    existing.toNodeId === edge.toNodeId &&
    existing.toPort === edge.toPort);
}

function createDefaultFieldValue(
  field: NodeConfigField,
  currentConfig: WorkflowConfigObject,
  workflowObjectName: string,
  metadataObjects: ObjectMetadata[]
): WorkflowConfigValue {
  if (field.kind === 'Text')
    return field.name === 'severity' ? 'Information' : '';
  if (field.kind === 'Template')
    return '';
  if (field.kind === 'ObjectName')
    return metadataObjects[0]?.name ?? workflowObjectName;
  if (field.kind === 'PropertyName') {
    const objectName = getObjectNameForField(field, currentConfig, workflowObjectName);
    return firstEditableProperty(metadataObjects, objectName)?.name ?? '';
  }
  if (field.kind === 'Condition') {
    const propertyName = firstEditableProperty(metadataObjects, workflowObjectName)?.name ?? 'Name';
    return {
      left: { source: 'currentRecord', field: propertyName },
      operator: 'equals',
      right: { source: 'literal', value: '' }
    };
  }
  if (field.kind === 'FieldMappings')
    return {} as WorkflowConfigObject;

  return '';
}

function getObjectNameForField(field: NodeConfigField, currentConfig: WorkflowConfigObject, workflowObjectName: string): string {
  if (!field.objectNameField)
    return workflowObjectName;

  const configuredObjectName = currentConfig[field.objectNameField];
  return typeof configuredObjectName === 'string' ? configuredObjectName : workflowObjectName;
}

function firstEditableProperty(metadataObjects: ObjectMetadata[], objectName: string): PropertyMetadata | undefined {
  const metadata = getObject(metadataObjects, objectName);
  return metadata?.properties.find(property => !property.isPrimaryKey) ?? metadata?.properties[0];
}