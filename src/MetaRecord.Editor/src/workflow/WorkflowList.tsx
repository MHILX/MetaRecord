import { FilePlus2, FolderOpen, RefreshCcw } from 'lucide-react';
import type { ObjectMetadata, WorkflowDefinition, WorkflowEventName } from '../api/types';
import { workflowEvents } from './workflowModel';

interface WorkflowListProps {
  workflows: WorkflowDefinition[];
  metadataObjects: ObjectMetadata[];
  selectedWorkflowId?: string | null;
  onOpenWorkflow: (workflow: WorkflowDefinition) => void;
  onCreateWorkflow: (name: string, objectName: string, eventName: WorkflowEventName) => void;
  onRefresh: () => void;
}

export function WorkflowList({
  workflows,
  metadataObjects,
  selectedWorkflowId,
  onOpenWorkflow,
  onCreateWorkflow,
  onRefresh
}: WorkflowListProps) {
  const defaultObjectName = metadataObjects[0]?.name ?? '';

  function handleCreate(formData: FormData) {
    const name = String(formData.get('name') ?? '').trim();
    const objectName = String(formData.get('objectName') ?? defaultObjectName);
    const eventName = String(formData.get('eventName') ?? 'Manual') as WorkflowEventName;
    onCreateWorkflow(name, objectName, eventName);
  }

  return (
    <section className="panel workflow-list-panel">
      <div className="panel-heading">
        <h2>Workflows</h2>
        <button className="icon-button" type="button" onClick={onRefresh} title="Refresh workflows">
          <RefreshCcw size={16} aria-hidden="true" />
        </button>
      </div>

      <form
        className="create-workflow-form"
        onSubmit={event => {
          event.preventDefault();
          handleCreate(new FormData(event.currentTarget));
          event.currentTarget.reset();
        }}
      >
        <label>
          <span>Name</span>
          <input name="name" placeholder="Workflow name" />
        </label>
        <label>
          <span>Object</span>
          <select name="objectName" defaultValue={defaultObjectName}>
            {metadataObjects.map(metadata => (
              <option key={metadata.name} value={metadata.name}>{metadata.name}</option>
            ))}
          </select>
        </label>
        <label>
          <span>Event</span>
          <select name="eventName" defaultValue="Manual">
            {workflowEvents.map(eventName => (
              <option key={eventName} value={eventName}>{eventName}</option>
            ))}
          </select>
        </label>
        <button className="primary-button" type="submit" disabled={metadataObjects.length === 0}>
          <FilePlus2 size={16} aria-hidden="true" />
          Create
        </button>
      </form>

      <div className="workflow-list">
        {workflows.length === 0 && <p className="muted">No saved workflows yet.</p>}
        {workflows.map(workflow => (
          <button
            className={`workflow-list-item ${workflow.id === selectedWorkflowId ? 'selected' : ''}`}
            key={workflow.id}
            type="button"
            onClick={() => onOpenWorkflow(workflow)}
          >
            <FolderOpen size={16} aria-hidden="true" />
            <span>
              <strong>{workflow.name}</strong>
              <small>{workflow.objectName} · {workflow.eventName}</small>
            </span>
            <em>{workflow.isEnabled ? 'On' : 'Off'}</em>
          </button>
        ))}
      </div>
    </section>
  );
}