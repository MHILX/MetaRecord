export type WorkflowEventName = 'BeforeSave' | 'Created' | 'Updated' | 'FieldChanged' | 'Manual';

export type WorkflowNodeCategory = 'Trigger' | 'Flow' | 'Action';

export type WorkflowNodeTiming = 'Any' | 'BeforeOnly' | 'AfterOnly' | 'ManualOnly';

export type NodeConfigFieldKind = 'Text' | 'Template' | 'ObjectName' | 'PropertyName' | 'Condition' | 'FieldMappings';

export type WorkflowRunStatus = 'Succeeded' | 'Failed' | 'Canceled' | 'Skipped';

export type WorkflowValidationSeverity = 'Info' | 'Warning' | 'Error';

export interface PropertyMetadata {
  name: string;
  columnName: string;
  clrType: string;
  isRequired: boolean;
  maxLength?: number | null;
  isUnique: boolean;
  isPrimaryKey: boolean;
  defaultValue?: string | null;
  caption?: string | null;
}

export interface ObjectMetadata {
  id: string;
  name: string;
  tableName: string;
  properties: PropertyMetadata[];
}

export interface NodePortDefinition {
  name: string;
  displayName: string;
}

export interface NodeConfigField {
  name: string;
  label: string;
  kind: NodeConfigFieldKind;
  isRequired: boolean;
  objectNameField?: string | null;
}

export interface NodeConfigSchema {
  fields: NodeConfigField[];
}

export interface WorkflowNodeType {
  type: string;
  category: WorkflowNodeCategory;
  displayName: string;
  description: string;
  timing: WorkflowNodeTiming;
  triggerEventName?: WorkflowEventName | null;
  inputPorts: NodePortDefinition[];
  outputPorts: NodePortDefinition[];
  configSchema: NodeConfigSchema;
  isTrigger: boolean;
}

export interface WorkflowPosition {
  x: number;
  y: number;
}

export type WorkflowConfigValue = string | number | boolean | null | WorkflowConfigObject | WorkflowConfigArray;
export interface WorkflowConfigObject { [key: string]: WorkflowConfigValue; }
export type WorkflowConfigArray = WorkflowConfigValue[];

export interface WorkflowNode {
  id: string;
  type: string;
  label?: string | null;
  position: WorkflowPosition;
  config: WorkflowConfigObject;
}

export interface WorkflowEdge {
  id: string;
  fromNodeId: string;
  fromPort: string;
  toNodeId: string;
  toPort: string;
}

export interface WorkflowDefinition {
  id: string;
  name: string;
  objectName: string;
  eventName: WorkflowEventName;
  isEnabled: boolean;
  version: number;
  nodes: WorkflowNode[];
  edges: WorkflowEdge[];
}

export interface WorkflowValidationIssue {
  severity: WorkflowValidationSeverity;
  message: string;
  nodeId?: string | null;
  field?: string | null;
}

export interface WorkflowValidationResponse {
  isValid: boolean;
  issues: WorkflowValidationIssue[];
}

export interface WorkflowRunSummary {
  id: string;
  workflowId: string;
  workflowVersion: number;
  objectName: string;
  eventName: WorkflowEventName;
  recordId?: string | null;
  status: WorkflowRunStatus;
  startedAt: string;
  completedAt?: string | null;
  durationMs?: number | null;
  errorMessage?: string | null;
}

export interface WorkflowRunStep {
  id?: string;
  nodeId: string;
  nodeType: string;
  nodeLabel?: string | null;
  status: WorkflowRunStatus;
  startedAt?: string;
  completedAt?: string | null;
  durationMs?: number | null;
  inputJson?: string | null;
  outputJson?: string | null;
  errorMessage?: string | null;
}

export interface WorkflowRunDetail extends WorkflowRunSummary {
  steps: WorkflowRunStep[];
}

export interface WorkflowTestRunResponse extends WorkflowRunSummary {
  runId: string;
  isRejected: boolean;
  steps: WorkflowRunStep[];
}

export interface WorkflowTestRunRequest {
  recordId?: string | null;
  currentRecord: Record<string, unknown>;
  originalRecord?: Record<string, unknown>;
  changedFields?: string[];
}