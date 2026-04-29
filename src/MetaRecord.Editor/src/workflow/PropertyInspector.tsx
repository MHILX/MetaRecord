import { Settings2 } from 'lucide-react';
import type {
  NodeConfigField,
  ObjectMetadata,
  WorkflowConfigValue,
  WorkflowConfigObject,
  WorkflowDefinition,
  WorkflowNode,
  WorkflowNodeType,
  WorkflowValidationIssue
} from '../api/types';
import { getConfigObject, getNodeIssues, getNodeType, getObject, updateNode } from './workflowModel';

interface PropertyInspectorProps {
  workflow?: WorkflowDefinition | null;
  nodeTypes: WorkflowNodeType[];
  metadataObjects: ObjectMetadata[];
  validationIssues: WorkflowValidationIssue[];
  selectedNodeId?: string | null;
  onWorkflowChange: (workflow: WorkflowDefinition) => void;
}

const conditionOperators = [
  'equals',
  'notEquals',
  'greaterThan',
  'greaterThanOrEqual',
  'lessThan',
  'lessThanOrEqual',
  'contains',
  'startsWith',
  'endsWith',
  'isEmpty',
  'isNotEmpty'
];

export function PropertyInspector({
  workflow,
  nodeTypes,
  metadataObjects,
  validationIssues,
  selectedNodeId,
  onWorkflowChange
}: PropertyInspectorProps) {
  if (!workflow) {
    return (
      <section className="panel inspector-panel">
        <h2>Inspector</h2>
        <p className="muted">Open or create a workflow to configure nodes.</p>
      </section>
    );
  }

  const selectedNode = workflow.nodes.find(node => node.id === selectedNodeId) ?? workflow.nodes[0];
  const selectedNodeType = selectedNode ? getNodeType(nodeTypes, selectedNode.type) : undefined;
  const issues = selectedNode ? getNodeIssues(validationIssues, selectedNode.id) : [];

  if (!selectedNode || !selectedNodeType) {
    return (
      <section className="panel inspector-panel">
        <h2>Inspector</h2>
        <p className="muted">Select a node to edit its configuration.</p>
      </section>
    );
  }

  function changeNode(updater: (node: WorkflowNode) => WorkflowNode) {
    if (!workflow || !selectedNode)
      return;

    onWorkflowChange(updateNode(workflow, selectedNode.id, updater));
  }

  function setConfigField(fieldName: string, value: WorkflowConfigValue) {
    changeNode(node => ({
      ...node,
      config: {
        ...node.config,
        [fieldName]: value
      }
    }));
  }

  return (
    <section className="panel inspector-panel">
      <div className="panel-heading">
        <h2>Inspector</h2>
        <Settings2 size={17} aria-hidden="true" />
      </div>

      <div className="inspector-node-header">
        <span>{selectedNodeType.category}</span>
        <strong>{selectedNodeType.displayName}</strong>
        <small>{selectedNode.type}</small>
      </div>

      <label className="field-control">
        <span>Label</span>
        <input
          value={selectedNode.label ?? ''}
          onChange={event => changeNode(node => ({ ...node, label: event.target.value }))}
          placeholder={selectedNodeType.displayName}
        />
      </label>

      {selectedNodeType.configSchema.fields.length === 0 && (
        <p className="muted">This node has no configurable fields.</p>
      )}

      {selectedNodeType.configSchema.fields.map(field => (
        <ConfigFieldEditor
          key={field.name}
          field={field}
          workflow={workflow}
          node={selectedNode}
          metadataObjects={metadataObjects}
          onChange={setConfigField}
        />
      ))}

      {issues.length > 0 && (
        <div className="node-issues">
          {issues.map((issue, index) => (
            <p key={`${issue.field ?? 'node'}-${index}`}>{issue.message}</p>
          ))}
        </div>
      )}
    </section>
  );
}

interface ConfigFieldEditorProps {
  field: NodeConfigField;
  workflow: WorkflowDefinition;
  node: WorkflowNode;
  metadataObjects: ObjectMetadata[];
  onChange: (fieldName: string, value: WorkflowConfigValue) => void;
}

function ConfigFieldEditor({ field, workflow, node, metadataObjects, onChange }: ConfigFieldEditorProps) {
  const value = node.config[field.name];
  const objectName = getObjectNameForField(field, workflow, node.config);
  const metadata = getObject(metadataObjects, objectName);

  if (field.kind === 'Text') {
    return (
      <label className="field-control">
        <span>{field.label}</span>
        <input value={stringValue(value)} onChange={event => onChange(field.name, event.target.value)} />
      </label>
    );
  }

  if (field.kind === 'Template') {
    return (
      <label className="field-control">
        <span>{field.label}</span>
        <textarea value={stringValue(value)} onChange={event => onChange(field.name, event.target.value)} rows={4} />
      </label>
    );
  }

  if (field.kind === 'ObjectName') {
    return (
      <label className="field-control">
        <span>{field.label}</span>
        <select value={stringValue(value) || metadataObjects[0]?.name || ''} onChange={event => onChange(field.name, event.target.value)}>
          {metadataObjects.map(metadataObject => (
            <option key={metadataObject.name} value={metadataObject.name}>{metadataObject.name}</option>
          ))}
        </select>
      </label>
    );
  }

  if (field.kind === 'PropertyName') {
    return (
      <label className="field-control">
        <span>{field.label}</span>
        <select value={stringValue(value)} onChange={event => onChange(field.name, event.target.value)}>
          {(metadata?.properties ?? []).map(property => (
            <option key={property.name} value={property.name}>{property.name}</option>
          ))}
        </select>
      </label>
    );
  }

  if (field.kind === 'Condition') {
    return (
      <ConditionEditor
        field={field}
        workflow={workflow}
        metadataObjects={metadataObjects}
        value={getConfigObject(value)}
        onChange={onChange}
      />
    );
  }

  if (field.kind === 'FieldMappings') {
    return (
      <FieldMappingsEditor
        field={field}
        workflow={workflow}
        nodeConfig={node.config}
        metadataObjects={metadataObjects}
        value={getConfigObject(value)}
        onChange={onChange}
      />
    );
  }

  return null;
}

interface ConditionEditorProps {
  field: NodeConfigField;
  workflow: WorkflowDefinition;
  metadataObjects: ObjectMetadata[];
  value: WorkflowConfigObject;
  onChange: (fieldName: string, value: WorkflowConfigValue) => void;
}

function ConditionEditor({ field, workflow, metadataObjects, value, onChange }: ConditionEditorProps) {
  const left = getConfigObject(value.left);
  const right = getConfigObject(value.right);
  const operator = stringValue(value.operator || 'equals');
  const metadata = getObject(metadataObjects, workflow.objectName);
  const fieldName = stringValue(left.field || metadata?.properties[0]?.name || '');

  function updateCondition(next: WorkflowConfigObject) {
    onChange(field.name, {
      left: { source: 'currentRecord', field: fieldName, ...left },
      operator,
      right: { source: 'literal', value: '', ...right },
      ...next
    });
  }

  return (
    <div className="field-control condition-editor">
      <span>{field.label}</span>
      <div className="condition-grid">
        <select
          value={fieldName}
          onChange={event => updateCondition({ left: { source: 'currentRecord', field: event.target.value } })}
        >
          {(metadata?.properties ?? []).map(property => (
            <option key={property.name} value={property.name}>{property.name}</option>
          ))}
        </select>
        <select value={operator} onChange={event => updateCondition({ operator: event.target.value })}>
          {conditionOperators.map(conditionOperator => (
            <option key={conditionOperator} value={conditionOperator}>{conditionOperator}</option>
          ))}
        </select>
        {!operator.toLowerCase().includes('empty') && (
          <input
            value={stringValue(right.value)}
            onChange={event => updateCondition({ right: { source: 'literal', value: event.target.value } })}
            placeholder="Value"
          />
        )}
      </div>
    </div>
  );
}

interface FieldMappingsEditorProps {
  field: NodeConfigField;
  workflow: WorkflowDefinition;
  nodeConfig: WorkflowConfigObject;
  metadataObjects: ObjectMetadata[];
  value: WorkflowConfigObject;
  onChange: (fieldName: string, value: WorkflowConfigValue) => void;
}

function FieldMappingsEditor({ field, workflow, nodeConfig, metadataObjects, value, onChange }: FieldMappingsEditorProps) {
  const objectName = getObjectNameForField(field, workflow, nodeConfig);
  const metadata = getObject(metadataObjects, objectName);

  function updateMapping(propertyName: string, template: string) {
    onChange(field.name, {
      ...value,
      [propertyName]: template
    });
  }

  return (
    <div className="field-control mapping-editor">
      <span>{field.label}</span>
      {(metadata?.properties ?? []).filter(property => !property.isPrimaryKey).map(property => (
        <label key={property.name}>
          <span>{property.name}</span>
          <input
            value={stringValue(value[property.name])}
            onChange={event => updateMapping(property.name, event.target.value)}
            placeholder={`Value for ${property.name}`}
          />
        </label>
      ))}
    </div>
  );
}

function getObjectNameForField(field: NodeConfigField, workflow: WorkflowDefinition, config: WorkflowConfigObject): string {
  if (!field.objectNameField)
    return workflow.objectName;

  const configured = config[field.objectNameField];
  return typeof configured === 'string' ? configured : workflow.objectName;
}

function stringValue(value: unknown): string {
  if (value === null || value === undefined)
    return '';
  return String(value);
}