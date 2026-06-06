import type {
  MetadataValidationResponse,
  ObjectMetadataUpsertRequest,
  ObjectMetadata,
  WorkflowDefinition,
  WorkflowNodeType,
  WorkflowRunDetail,
  WorkflowRunSummary,
  WorkflowTestRunRequest,
  WorkflowTestRunResponse,
  WorkflowValidationResponse
} from './types';

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? '';

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(options?.headers ?? {})
    }
  });

  if (!response.ok) {
    let details: unknown;
    try {
      details = await response.json();
    } catch {
      details = await response.text();
    }

    const error = new ApiError(response.status, response.statusText, details);
    throw error;
  }

  if (response.status === 204)
    return undefined as T;

  return response.json() as Promise<T>;
}

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    statusText: string,
    public readonly details: unknown
  ) {
    super(`Request failed with ${status} ${statusText}`);
  }
}

export const workflowApi = {
  listObjects: () => request<ObjectMetadata[]>('/api/metadata/objects'),
  getObject: (name: string) => request<ObjectMetadata>(`/api/metadata/objects/${encodeURIComponent(name)}`),
  getObjectById: (id: string) => request<ObjectMetadata>(`/api/metadata/objects/${id}`),
  validateObject: (requestBody: ObjectMetadataUpsertRequest) => request<MetadataValidationResponse>('/api/metadata/objects/validate', {
    method: 'POST',
    body: JSON.stringify(requestBody)
  }),
  createObject: (requestBody: ObjectMetadataUpsertRequest) => request<ObjectMetadata>('/api/metadata/objects', {
    method: 'POST',
    body: JSON.stringify(requestBody)
  }),
  updateObject: (id: string, requestBody: ObjectMetadataUpsertRequest) => request<ObjectMetadata>(`/api/metadata/objects/${id}`, {
    method: 'PUT',
    body: JSON.stringify(requestBody)
  }),
  deleteObject: (id: string) => request<void>(`/api/metadata/objects/${id}`, {
    method: 'DELETE'
  }),
  listNodeTypes: () => request<WorkflowNodeType[]>('/api/workflow-node-types'),
  listWorkflows: () => request<WorkflowDefinition[]>('/api/workflows'),
  getWorkflow: (id: string) => request<WorkflowDefinition>(`/api/workflows/${id}`),
  createWorkflow: (workflow: WorkflowDefinition) => request<WorkflowDefinition>('/api/workflows', {
    method: 'POST',
    body: JSON.stringify(workflow)
  }),
  updateWorkflow: (workflow: WorkflowDefinition) => request<WorkflowDefinition>(`/api/workflows/${workflow.id}`, {
    method: 'PUT',
    body: JSON.stringify(workflow)
  }),
  validateWorkflow: (id: string) => request<WorkflowValidationResponse>(`/api/workflows/${id}/validate`, {
    method: 'POST',
    body: JSON.stringify({})
  }),
  enableWorkflow: (id: string) => request<WorkflowDefinition>(`/api/workflows/${id}/enable`, {
    method: 'POST',
    body: JSON.stringify({})
  }),
  disableWorkflow: (id: string) => request<WorkflowDefinition>(`/api/workflows/${id}/disable`, {
    method: 'POST',
    body: JSON.stringify({})
  }),
  testRun: (id: string, testRun: WorkflowTestRunRequest) => request<WorkflowTestRunResponse>(`/api/workflows/${id}/test-run`, {
    method: 'POST',
    body: JSON.stringify(testRun)
  }),
  listRuns: (workflowId: string) => request<WorkflowRunSummary[]>(`/api/workflows/${workflowId}/runs`),
  getRun: (runId: string) => request<WorkflowRunDetail>(`/api/workflow-runs/${runId}`)
};