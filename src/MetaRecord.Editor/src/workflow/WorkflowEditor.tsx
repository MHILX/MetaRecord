import { CheckCircle2, Power, PowerOff, Save, Workflow as WorkflowIcon } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { ApiError, workflowApi } from '../api/client';
import type {
  ObjectMetadata,
  WorkflowDefinition,
  WorkflowNodeType,
  WorkflowRunDetail,
  WorkflowRunSummary,
  WorkflowTestRunResponse,
  WorkflowValidationIssue,
  WorkflowValidationResponse
} from '../api/types';
import { NodePalette } from './NodePalette';
import { MetadataManager } from './MetadataManager';
import { PropertyInspector } from './PropertyInspector';
import { RunHistoryPanel } from './RunHistoryPanel';
import { TestRunPanel } from './TestRunPanel';
import { ValidationPanel } from './ValidationPanel';
import { WorkflowCanvas } from './WorkflowCanvas';
import { WorkflowList } from './WorkflowList';
import { demoDomain } from './demoDomain';
import { createNodeDraft, createWorkflowDraft } from './workflowModel';

type NoticeKind = 'info' | 'error';

type NoticeState = {
  message: string;
  kind: NoticeKind;
};

export function WorkflowEditor() {
  const preferredDemoWorkflowName = demoDomain.preferredWorkflowName;
  const [metadataObjects, setMetadataObjects] = useState<ObjectMetadata[]>([]);
  const [nodeTypes, setNodeTypes] = useState<WorkflowNodeType[]>([]);
  const [workflows, setWorkflows] = useState<WorkflowDefinition[]>([]);
  const [savedWorkflowIds, setSavedWorkflowIds] = useState<Set<string>>(new Set());
  const [selectedWorkflow, setSelectedWorkflow] = useState<WorkflowDefinition | null>(null);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [validationIssues, setValidationIssues] = useState<WorkflowValidationIssue[]>([]);
  const [runs, setRuns] = useState<WorkflowRunSummary[]>([]);
  const [selectedRun, setSelectedRun] = useState<WorkflowRunDetail | null>(null);
  const [testResult, setTestResult] = useState<WorkflowTestRunResponse | null>(null);
  const [notice, setNotice] = useState<NoticeState | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [isRunning, setIsRunning] = useState(false);

  const selectedWorkflowIsSaved = useMemo(
    () => Boolean(selectedWorkflow && savedWorkflowIds.has(selectedWorkflow.id)),
    [savedWorkflowIds, selectedWorkflow]
  );

  useEffect(() => {
    void loadInitialData();
  }, []);

  useEffect(() => {
    if (!notice || notice.kind === 'error')
      return;

    const timeoutId = window.setTimeout(() => setNotice(null), 2500);
    return () => window.clearTimeout(timeoutId);
  }, [notice]);

  function showNotice(message: string, kind: NoticeKind = 'info') {
    setNotice({ message, kind });
  }

  async function loadInitialData() {
    setIsLoading(true);
    try {
      const [objects, catalog, savedWorkflows] = await Promise.all([
        workflowApi.listObjects(),
        workflowApi.listNodeTypes(),
        workflowApi.listWorkflows()
      ]);
      setMetadataObjects(objects);
      setNodeTypes(catalog);
      setWorkflows(savedWorkflows);
      setSavedWorkflowIds(new Set(savedWorkflows.map(workflow => workflow.id)));
      if (!selectedWorkflow && savedWorkflows.length > 0) {
        const preferredWorkflow = savedWorkflows.find(workflow => workflow.name === preferredDemoWorkflowName) ?? savedWorkflows[0];
        openWorkflow(preferredWorkflow);
      }
      setNotice(null);
    } catch (error) {
      showNotice(getErrorMessage(error, 'Could not load editor data. Start the MetaRecord.Web API and refresh.'), 'error');
    } finally {
      setIsLoading(false);
    }
  }

  async function reloadMetadataObjects() {
    const objects = await workflowApi.listObjects();
    setMetadataObjects(objects);
  }

  async function reloadWorkflows(keepSelected?: WorkflowDefinition) {
    const savedWorkflows = await workflowApi.listWorkflows();
    setWorkflows(savedWorkflows);
    setSavedWorkflowIds(new Set(savedWorkflows.map(workflow => workflow.id)));
    if (keepSelected) {
      setSelectedWorkflow(keepSelected);
    }
  }

  function openWorkflow(workflow: WorkflowDefinition) {
    setSelectedWorkflow(workflow);
    setSelectedNodeId(workflow.nodes[0]?.id ?? null);
    setValidationIssues([]);
    setTestResult(null);
    setSelectedRun(null);
    void loadRuns(workflow.id);
  }

  function createWorkflow(name: string, objectName: string, eventName: WorkflowDefinition['eventName']) {
    try {
      const workflow = createWorkflowDraft(name, objectName, eventName, metadataObjects, nodeTypes);
      setSelectedWorkflow(workflow);
      setSelectedNodeId(workflow.nodes[0]?.id ?? null);
      setValidationIssues([]);
      setRuns([]);
      setSelectedRun(null);
      setTestResult(null);
      showNotice('Draft created. Save it to persist and validate on the server.');
    } catch (error) {
      showNotice(getErrorMessage(error, 'Could not create workflow.'), 'error');
    }
  }

  function addNode(nodeType: WorkflowNodeType) {
    if (!selectedWorkflow)
      return;

    const node = createNodeDraft(nodeType, selectedWorkflow, metadataObjects);
    setSelectedWorkflow({
      ...selectedWorkflow,
      nodes: [...selectedWorkflow.nodes, node]
    });
    setSelectedNodeId(node.id);
    showNotice(`${nodeType.displayName} added.`);
  }

  async function saveSelectedWorkflow(): Promise<WorkflowDefinition | null> {
    if (!selectedWorkflow)
      return null;

    return persistWorkflow(selectedWorkflow);
  }

  async function persistWorkflow(workflow: WorkflowDefinition): Promise<WorkflowDefinition | null> {
    setIsSaving(true);
    try {
      const savedWorkflow = savedWorkflowIds.has(workflow.id)
        ? await workflowApi.updateWorkflow(workflow)
        : await workflowApi.createWorkflow(workflow);

      setSelectedWorkflow(savedWorkflow);
      setValidationIssues([]);
      await reloadWorkflows(savedWorkflow);
      showNotice('Workflow saved.');
      return savedWorkflow;
    } catch (error) {
      const validation = getValidationDetails(error);
      if (validation)
        setValidationIssues(validation.issues);

      showNotice(getErrorMessage(error, 'Workflow save failed.'), 'error');
      return null;
    } finally {
      setIsSaving(false);
    }
  }

  async function validateSelectedWorkflow() {
    const workflow = selectedWorkflowIsSaved ? selectedWorkflow : await saveSelectedWorkflow();
    if (!workflow)
      return;

    try {
      const validation = await workflowApi.validateWorkflow(workflow.id);
      setValidationIssues(validation.issues);
      showNotice(validation.isValid ? 'Workflow is valid.' : 'Validation found issues.');
    } catch (error) {
      showNotice(getErrorMessage(error, 'Validation failed.'), 'error');
    }
  }

  async function enableSelectedWorkflow() {
    const workflow = selectedWorkflowIsSaved ? selectedWorkflow : await saveSelectedWorkflow();
    if (!workflow)
      return;

    try {
      const enabled = await workflowApi.enableWorkflow(workflow.id);
      setSelectedWorkflow(enabled);
      await reloadWorkflows(enabled);
      showNotice('Workflow enabled.');
    } catch (error) {
      const validation = getValidationDetails(error);
      if (validation)
        setValidationIssues(validation.issues);
      showNotice(getErrorMessage(error, 'Enable failed.'), 'error');
    }
  }

  async function disableSelectedWorkflow() {
    if (!selectedWorkflow || !selectedWorkflowIsSaved)
      return;

    try {
      const disabled = await workflowApi.disableWorkflow(selectedWorkflow.id);
      setSelectedWorkflow(disabled);
      await reloadWorkflows(disabled);
      showNotice('Workflow disabled.');
    } catch (error) {
      showNotice(getErrorMessage(error, 'Disable failed.'), 'error');
    }
  }

  async function runTest(currentRecord: Record<string, unknown>) {
    const workflow = selectedWorkflowIsSaved ? selectedWorkflow : await saveSelectedWorkflow();
    if (!workflow)
      return;

    setIsRunning(true);
    try {
      const result = await workflowApi.testRun(workflow.id, { currentRecord });
      setTestResult(result);
      await loadRuns(workflow.id);
      showNotice(`Test run ${result.status.toLowerCase()}.`);
    } catch (error) {
      showNotice(getErrorMessage(error, 'Test run failed.'), 'error');
    } finally {
      setIsRunning(false);
    }
  }

  async function loadRuns(workflowId: string) {
    try {
      const nextRuns = await workflowApi.listRuns(workflowId);
      setRuns(nextRuns);
    } catch {
      setRuns([]);
    }
  }

  async function selectRun(runId: string) {
    try {
      setSelectedRun(await workflowApi.getRun(runId));
    } catch (error) {
      showNotice(getErrorMessage(error, 'Could not load run details.'), 'error');
    }
  }

  return (
    <main className="editor-shell">
      <header className="editor-toolbar">
        <div className="app-title">
          <WorkflowIcon size={21} aria-hidden="true" />
          <div>
            <h1>Workflow Editor</h1>
            <span>{isLoading ? 'Loading API data' : `${metadataObjects.length} objects · ${nodeTypes.length} node types`}</span>
          </div>
        </div>

        {selectedWorkflow && (
          <div className="workflow-toolbar-fields">
            <input
              value={selectedWorkflow.name}
              onChange={event => setSelectedWorkflow({ ...selectedWorkflow, name: event.target.value })}
              aria-label="Workflow name"
            />
            <span>{selectedWorkflow.objectName}</span>
            <span>{selectedWorkflow.eventName}</span>
            <span className={selectedWorkflow.isEnabled ? 'status-pill enabled' : 'status-pill disabled'}>
              {selectedWorkflow.isEnabled ? 'Enabled' : selectedWorkflowIsSaved ? 'Disabled' : 'Draft'}
            </span>
          </div>
        )}

        <div className="toolbar-actions">
          <button className="secondary-button" type="button" onClick={saveSelectedWorkflow} disabled={!selectedWorkflow || isSaving}>
            <Save size={16} aria-hidden="true" />
            Save
          </button>
          <button className="secondary-button" type="button" onClick={validateSelectedWorkflow} disabled={!selectedWorkflow || isSaving}>
            <CheckCircle2 size={16} aria-hidden="true" />
            Validate
          </button>
          <button className="secondary-button" type="button" onClick={enableSelectedWorkflow} disabled={!selectedWorkflow || isSaving}>
            <Power size={16} aria-hidden="true" />
            Enable
          </button>
          <button className="secondary-button" type="button" onClick={disableSelectedWorkflow} disabled={!selectedWorkflow || !selectedWorkflowIsSaved || isSaving}>
            <PowerOff size={16} aria-hidden="true" />
            Disable
          </button>
        </div>
      </header>

      {notice && <div className="notice-bar">{notice.message}</div>}

      <div className="editor-layout">
        <aside className="left-rail">
          <WorkflowList
            workflows={workflows}
            metadataObjects={metadataObjects}
            selectedWorkflowId={selectedWorkflow?.id}
            onOpenWorkflow={openWorkflow}
            onCreateWorkflow={createWorkflow}
            onRefresh={loadInitialData}
          />
          <MetadataManager
            metadataObjects={metadataObjects}
            isLoading={isLoading}
            onMetadataObjectsChange={setMetadataObjects}
            onRefreshMetadata={reloadMetadataObjects}
            onNotice={showNotice}
          />
          <NodePalette workflow={selectedWorkflow} nodeTypes={nodeTypes} onAddNode={addNode} />
        </aside>

        <section className="canvas-column">
          {selectedWorkflow ? (
            <WorkflowCanvas
              workflow={selectedWorkflow}
              nodeTypes={nodeTypes}
              validationIssues={validationIssues}
              selectedNodeId={selectedNodeId}
              onSelectNode={setSelectedNodeId}
              onWorkflowChange={setSelectedWorkflow}
              onNotice={showNotice}
            />
          ) : (
            <div className="empty-canvas">
              <WorkflowIcon size={34} aria-hidden="true" />
              <strong>No workflow selected</strong>
              <span>Create or open a workflow from the left rail.</span>
            </div>
          )}
        </section>

        <aside className="right-rail">
          <PropertyInspector
            workflow={selectedWorkflow}
            nodeTypes={nodeTypes}
            metadataObjects={metadataObjects}
            validationIssues={validationIssues}
            selectedNodeId={selectedNodeId}
            onWorkflowChange={setSelectedWorkflow}
          />
        </aside>
      </div>

      <footer className="bottom-rail">
        <ValidationPanel issues={validationIssues} onSelectNode={setSelectedNodeId} />
        <TestRunPanel
          workflow={selectedWorkflow}
          metadataObjects={metadataObjects}
          testResult={testResult}
          isRunning={isRunning}
          onRun={runTest}
        />
        <RunHistoryPanel runs={runs} selectedRun={selectedRun} onSelectRun={selectRun} />
      </footer>
    </main>
  );
}

function getValidationDetails(error: unknown): WorkflowValidationResponse | null {
  if (!(error instanceof ApiError))
    return null;

  const details = error.details as Partial<WorkflowValidationResponse> | undefined;
  if (!details || !Array.isArray(details.issues))
    return null;

  return {
    isValid: Boolean(details.isValid),
    issues: details.issues
  };
}

function getErrorMessage(error: unknown, fallback: string) {
  if (error instanceof ApiError) {
    const validation = getValidationDetails(error);
    if (validation)
      return validation.isValid ? fallback : `Validation blocked the request with ${validation.issues.length} issue(s).`;

    return `${fallback} ${error.message}`;
  }

  if (error instanceof Error)
    return `${fallback} ${error.message}`;

  return fallback;
}