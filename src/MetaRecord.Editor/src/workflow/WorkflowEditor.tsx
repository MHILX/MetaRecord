import { CheckCircle2, Power, PowerOff, Save, Trash2, Workflow as WorkflowIcon } from 'lucide-react';
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
import { EditorPrimaryNav } from '../navigation/EditorPrimaryNav';
import { NodePalette } from './NodePalette';
import { PropertyInspector } from './PropertyInspector';
import { RunHistoryPanel } from './RunHistoryPanel';
import { TestRunPanel } from './TestRunPanel';
import { ValidationPanel } from './ValidationPanel';
import { WorkflowCanvas } from './WorkflowCanvas';
import { WorkflowList } from './WorkflowList';
import { demoDomain } from './demoDomain';
import { createNodeDraft, createSampleRecordValues, createWorkflowDraft, getObject } from './workflowModel';

type NoticeKind = 'info' | 'error';

type NoticeState = {
  message: string;
  kind: NoticeKind;
};

type SidebarTab = 'workflow' | 'runs' | 'inspector';
type LeftRailTab = 'workflows' | 'palette';
type BottomRailTab = 'validation' | 'test' | 'history';

const sidebarTabs: Array<{ id: SidebarTab; label: string }> = [
  { id: 'workflow', label: 'Workflow' },
  { id: 'runs', label: 'Runs' },
  { id: 'inspector', label: 'Inspector' }
];

const leftRailTabs: Array<{ id: LeftRailTab; label: string }> = [
  { id: 'workflows', label: 'Workflows' },
  { id: 'palette', label: 'Node Palette' }
];

const bottomRailTabs: Array<{ id: BottomRailTab; label: string }> = [
  { id: 'validation', label: 'Validation' },
  { id: 'test', label: 'Test Run' },
  { id: 'history', label: 'Run History' }
];

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
  const [testInputValues, setTestInputValues] = useState<Record<string, string>>({});
  const [notice, setNotice] = useState<NoticeState | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [isRunning, setIsRunning] = useState(false);
  const [sidebarTab, setSidebarTab] = useState<SidebarTab>('workflow');
  const [leftRailTab, setLeftRailTab] = useState<LeftRailTab>('workflows');
  const [bottomRailTab, setBottomRailTab] = useState<BottomRailTab>('validation');

  const selectedWorkflowIsSaved = useMemo(
    () => Boolean(selectedWorkflow && savedWorkflowIds.has(selectedWorkflow.id)),
    [savedWorkflowIds, selectedWorkflow]
  );

  const selectedWorkflowObjectName = useMemo(() => {
    if (!selectedWorkflow)
      return '';

    return getObject(metadataObjects, selectedWorkflow.objectName)?.name ?? selectedWorkflow.objectName;
  }, [metadataObjects, selectedWorkflow?.objectName]);

  const workflowObjectNames = useMemo(() => {
    const objectNames = metadataObjects.map(metadataObject => metadataObject.name);

    if (selectedWorkflowObjectName && !objectNames.includes(selectedWorkflowObjectName))
      return [selectedWorkflowObjectName, ...objectNames];

    return objectNames;
  }, [metadataObjects, selectedWorkflowObjectName]);

  useEffect(() => {
    void loadInitialData();
  }, []);

  useEffect(() => {
    if (!notice || notice.kind === 'error')
      return;

    const timeoutId = window.setTimeout(() => setNotice(null), 2500);
    return () => window.clearTimeout(timeoutId);
  }, [notice]);

  useEffect(() => {
    if (!selectedWorkflow) {
      setTestInputValues({});
      return;
    }

    const metadata = getObject(metadataObjects, selectedWorkflow.objectName);
    setTestInputValues(metadata ? createSampleRecordValues(metadata) : {});
  }, [metadataObjects, selectedWorkflow?.objectName]);

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

  async function reloadWorkflows(keepSelected?: WorkflowDefinition) {
    const savedWorkflows = await workflowApi.listWorkflows();
    setWorkflows(savedWorkflows);
    setSavedWorkflowIds(new Set(savedWorkflows.map(workflow => workflow.id)));
    if (keepSelected)
      setSelectedWorkflow(keepSelected);
  }

  function openWorkflow(workflow: WorkflowDefinition) {
    setSidebarTab('workflow');
    setSelectedWorkflow(workflow);
    setSelectedNodeId(workflow.nodes[0]?.id ?? null);
    setValidationIssues([]);
    setTestResult(null);
    setSelectedRun(null);
    void loadRuns(workflow.id);
  }

  function clearSelectedWorkflow() {
    setSelectedWorkflow(null);
    setSelectedNodeId(null);
    setValidationIssues([]);
    setRuns([]);
    setSelectedRun(null);
    setTestResult(null);
    setTestInputValues({});
  }

  function createWorkflow(name: string, objectName: string, eventName: WorkflowDefinition['eventName']) {
    try {
      setSidebarTab('workflow');
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

  async function deleteSelectedWorkflow() {
    if (!selectedWorkflow)
      return;

    const confirmMessage = selectedWorkflowIsSaved
      ? `Delete workflow "${selectedWorkflow.name}"? This will also remove its run history.`
      : `Discard unsaved workflow "${selectedWorkflow.name}"?`;

    if (!window.confirm(confirmMessage))
      return;

    if (!selectedWorkflowIsSaved) {
      clearSelectedWorkflow();
      showNotice('Workflow discarded.');
      return;
    }

    setIsSaving(true);
    try {
      await workflowApi.deleteWorkflow(selectedWorkflow.id);

      const remainingWorkflows = await workflowApi.listWorkflows();
      setWorkflows(remainingWorkflows);
      setSavedWorkflowIds(new Set(remainingWorkflows.map(workflow => workflow.id)));
      clearSelectedWorkflow();
      showNotice('Workflow deleted.');
    } catch (error) {
      showNotice(getErrorMessage(error, 'Delete failed.'), 'error');
    } finally {
      setIsSaving(false);
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

        <EditorPrimaryNav activePage="workflow" />
      </header>

      {notice && <div className="notice-bar">{notice.message}</div>}

      <div className="editor-layout">
        <aside className="workflow-sidebar">
          <div className="workflow-sidebar-tabs rail-tabs" role="tablist" aria-label="Workflow sections">
            {sidebarTabs.map(tab => (
              <button
                key={tab.id}
                id={`workflow-sidebar-tab-${tab.id}`}
                type="button"
                role="tab"
                aria-selected={sidebarTab === tab.id}
                aria-controls={`workflow-sidebar-panel-${tab.id}`}
                className={sidebarTab === tab.id ? 'rail-tab active' : 'rail-tab'}
                onClick={() => setSidebarTab(tab.id)}
              >
                {tab.label}
              </button>
            ))}
          </div>

          <div className="workflow-sidebar-content">
            {sidebarTab === 'workflow' && (
              <section
                id="workflow-sidebar-panel-workflow"
                className="workflow-sidebar-panel workflow-workflow-panel"
                role="tabpanel"
                aria-labelledby="workflow-sidebar-tab-workflow"
              >
                <div className="left-rail">
                  <div className="left-rail-toolbar">
                    <div className="rail-tabs left-rail-tabs" role="tablist" aria-label="Workflow panels">
                      {leftRailTabs.map(tab => (
                        <button
                          key={tab.id}
                          type="button"
                          role="tab"
                          aria-selected={leftRailTab === tab.id}
                          className={leftRailTab === tab.id ? 'rail-tab active' : 'rail-tab'}
                          onClick={() => setLeftRailTab(tab.id)}
                        >
                          {tab.label}
                        </button>
                      ))}
                    </div>
                  </div>

                  <div className="left-rail-panel-stack">
                    <div className={leftRailTab === 'workflows' ? 'left-rail-panel active' : 'left-rail-panel'} role="tabpanel">
                      <WorkflowList
                        workflows={workflows}
                        metadataObjects={metadataObjects}
                        selectedWorkflowId={selectedWorkflow?.id}
                        onOpenWorkflow={openWorkflow}
                        onCreateWorkflow={createWorkflow}
                        onRefresh={loadInitialData}
                      />
                    </div>

                    <div className={leftRailTab === 'palette' ? 'left-rail-panel active' : 'left-rail-panel'} role="tabpanel">
                      <NodePalette workflow={selectedWorkflow} nodeTypes={nodeTypes} onAddNode={addNode} />
                    </div>
                  </div>
                </div>
              </section>
            )}

            {sidebarTab === 'runs' && (
              <section
                id="workflow-sidebar-panel-runs"
                className="workflow-sidebar-panel workflow-runs-panel"
                role="tabpanel"
                aria-labelledby="workflow-sidebar-tab-runs"
              >
                <div className="bottom-rail-toolbar">
                  <div className="rail-tabs bottom-rail-tabs" role="tablist" aria-label="Run panels">
                    {bottomRailTabs.map(tab => (
                      <button
                        key={tab.id}
                        id={`workflow-runs-tab-${tab.id}`}
                        type="button"
                        role="tab"
                        aria-selected={bottomRailTab === tab.id}
                        aria-controls={`workflow-runs-panel-${tab.id}`}
                        className={bottomRailTab === tab.id ? 'rail-tab active' : 'rail-tab'}
                        onClick={() => setBottomRailTab(tab.id)}
                      >
                        {tab.label}
                      </button>
                    ))}
                  </div>
                </div>

                <div className="bottom-rail-panels">
                  <div
                    id="workflow-runs-panel-validation"
                    role="tabpanel"
                    aria-labelledby="workflow-runs-tab-validation"
                    aria-hidden={bottomRailTab !== 'validation'}
                    className={bottomRailTab === 'validation' ? 'bottom-rail-panel active' : 'bottom-rail-panel'}
                  >
                    <ValidationPanel issues={validationIssues} onSelectNode={setSelectedNodeId} />
                  </div>

                  <div
                    id="workflow-runs-panel-test"
                    role="tabpanel"
                    aria-labelledby="workflow-runs-tab-test"
                    aria-hidden={bottomRailTab !== 'test'}
                    className={bottomRailTab === 'test' ? 'bottom-rail-panel active' : 'bottom-rail-panel'}
                  >
                    <TestRunPanel
                      workflow={selectedWorkflow}
                      metadataObjects={metadataObjects}
                      values={testInputValues}
                      onValuesChange={setTestInputValues}
                      testResult={testResult}
                      isRunning={isRunning}
                      onRun={runTest}
                    />
                  </div>

                  <div
                    id="workflow-runs-panel-history"
                    role="tabpanel"
                    aria-labelledby="workflow-runs-tab-history"
                    aria-hidden={bottomRailTab !== 'history'}
                    className={bottomRailTab === 'history' ? 'bottom-rail-panel active' : 'bottom-rail-panel'}
                  >
                    <RunHistoryPanel runs={runs} selectedRun={selectedRun} onSelectRun={selectRun} />
                  </div>
                </div>
              </section>
            )}

            {sidebarTab === 'inspector' && (
              <section
                id="workflow-sidebar-panel-inspector"
                className="workflow-sidebar-panel workflow-inspector-panel"
                role="tabpanel"
                aria-labelledby="workflow-sidebar-tab-inspector"
              >
                <PropertyInspector
                  workflow={selectedWorkflow}
                  nodeTypes={nodeTypes}
                  metadataObjects={metadataObjects}
                  validationIssues={validationIssues}
                  selectedNodeId={selectedNodeId}
                  onWorkflowChange={setSelectedWorkflow}
                />
              </section>
            )}
          </div>
        </aside>

        <section className="canvas-column">
          <section className="panel canvas-workflow-toolbar">
            {selectedWorkflow ? (
              <>
                <div className="canvas-workflow-toolbar-meta">
                  <div>
                    <strong>Workflow Controls</strong>
                    <span>{selectedWorkflowObjectName} · {selectedWorkflow.eventName}</span>
                  </div>
                  <span className={selectedWorkflow.isEnabled ? 'status-pill enabled' : 'status-pill disabled'}>
                    {selectedWorkflow.isEnabled ? 'Enabled' : selectedWorkflowIsSaved ? 'Disabled' : 'Draft'}
                  </span>
                </div>

                <div className="workflow-toolbar-fields canvas-workflow-toolbar-fields">
                  <input
                    value={selectedWorkflow.name}
                    onChange={event => setSelectedWorkflow({ ...selectedWorkflow, name: event.target.value })}
                    aria-label="Workflow name"
                  />
                  <select
                    value={selectedWorkflowObjectName}
                    onChange={event => {
                      setSelectedWorkflow({ ...selectedWorkflow, objectName: event.target.value });
                      setValidationIssues([]);
                      setSelectedRun(null);
                      setTestResult(null);
                    }}
                    aria-label="Workflow object"
                    disabled={metadataObjects.length === 0}
                  >
                    {workflowObjectNames.map(objectName => (
                      <option key={objectName} value={objectName}>
                        {objectName}
                      </option>
                    ))}
                  </select>
                </div>

                <div className="toolbar-actions canvas-workflow-toolbar-actions">
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
                  <button className="secondary-button destructive-button" type="button" onClick={deleteSelectedWorkflow} disabled={!selectedWorkflow || isSaving}>
                    <Trash2 size={16} aria-hidden="true" />
                    Delete
                  </button>
                </div>
              </>
            ) : (
              <div className="canvas-workflow-toolbar-empty">
                <strong>No workflow selected</strong>
                <span>Create or open a workflow from the left rail to edit it here.</span>
              </div>
            )}
          </section>

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
              <strong>No workflow canvas</strong>
              <span>Select a workflow to start editing the graph.</span>
            </div>
          )}
        </section>
      </div>
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