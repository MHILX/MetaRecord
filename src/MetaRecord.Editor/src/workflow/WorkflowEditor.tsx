import { ChevronLeft, ChevronRight, CheckCircle2, Database, Maximize2, Power, PowerOff, Save, Trash2, Workflow as WorkflowIcon, X } from 'lucide-react';
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
import { MetadataManager, type MetadataSelectionId } from './MetadataManager';
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

type LeftRailTab = 'workflows' | 'metadata' | 'palette';

type BottomRailTab = 'validation' | 'test' | 'history';

interface WorkflowEditorProps {
  onOpenMetadataViewer?: () => void;
}

const leftRailTabs: Array<{ id: LeftRailTab; label: string }> = [
  { id: 'workflows', label: 'Workflows' },
  { id: 'metadata', label: 'Metadata' },
  { id: 'palette', label: 'Node Palette' }
];

const bottomRailTabs: Array<{ id: BottomRailTab; label: string }> = [
  { id: 'validation', label: 'Validation' },
  { id: 'test', label: 'Test Run' },
  { id: 'history', label: 'Run History' }
];

function getLeftRailTabLabel(tab: LeftRailTab): string {
  return leftRailTabs.find(candidate => candidate.id === tab)?.label ?? 'Panel';
}

function getBottomRailTabLabel(tab: BottomRailTab): string {
  return bottomRailTabs.find(candidate => candidate.id === tab)?.label ?? 'Panel';
}

export function WorkflowEditor({ onOpenMetadataViewer }: WorkflowEditorProps = {}) {
  const preferredDemoWorkflowName = demoDomain.preferredWorkflowName;
  const [metadataObjects, setMetadataObjects] = useState<ObjectMetadata[]>([]);
  const [selectedMetadataObjectId, setSelectedMetadataObjectId] = useState<MetadataSelectionId>(null);
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
  const [leftRailTab, setLeftRailTab] = useState<LeftRailTab>('workflows');
  const [bottomRailTab, setBottomRailTab] = useState<BottomRailTab>('validation');
  const [isInspectorCollapsed, setIsInspectorCollapsed] = useState(false);
  const [isLeftRailDialogOpen, setIsLeftRailDialogOpen] = useState(false);
  const [isBottomRailDialogOpen, setIsBottomRailDialogOpen] = useState(false);

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

  useEffect(() => {
    if (!isLeftRailDialogOpen && !isBottomRailDialogOpen)
      return;

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape')
      {
        setIsLeftRailDialogOpen(false);
        setIsBottomRailDialogOpen(false);
      }
    }

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isLeftRailDialogOpen, isBottomRailDialogOpen]);

  function showNotice(message: string, kind: NoticeKind = 'info') {
    setNotice({ message, kind });
  }

  function openMetadataDialog() {
    setIsBottomRailDialogOpen(false);
    setIsLeftRailDialogOpen(true);
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

        {selectedWorkflow && (
          <div className="workflow-toolbar-fields">
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
            <span>{selectedWorkflow.eventName}</span>
            <span className={selectedWorkflow.isEnabled ? 'status-pill enabled' : 'status-pill disabled'}>
              {selectedWorkflow.isEnabled ? 'Enabled' : selectedWorkflowIsSaved ? 'Disabled' : 'Draft'}
            </span>
          </div>
        )}

        <div className="toolbar-actions">
          <button className="secondary-button" type="button" onClick={onOpenMetadataViewer}>
            <Database size={16} aria-hidden="true" />
            Metadata Viewer
          </button>
          <button
            className="secondary-button"
            type="button"
            onClick={() => setIsInspectorCollapsed(current => !current)}
            aria-pressed={isInspectorCollapsed}
            aria-controls="property-inspector"
          >
            {isInspectorCollapsed ? <ChevronLeft size={16} aria-hidden="true" /> : <ChevronRight size={16} aria-hidden="true" />}
            {isInspectorCollapsed ? 'Show Inspector' : 'Hide Inspector'}
          </button>
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
      </header>

      {notice && <div className="notice-bar">{notice.message}</div>}

      <div className={isInspectorCollapsed ? 'editor-layout inspector-collapsed' : 'editor-layout'}>
        <aside className="left-rail">
          <div className="left-rail-toolbar">
            <div className="rail-tabs left-rail-tabs" role="tablist" aria-label="Editor panels">
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

            {leftRailTab !== 'workflows' && (
              <button
                className="secondary-button left-rail-popout-button"
                type="button"
                onClick={openMetadataDialog}
                aria-haspopup="dialog"
                aria-expanded={isLeftRailDialogOpen}
              >
                <Maximize2 size={16} aria-hidden="true" />
                Expand
              </button>
            )}
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

            <div className={leftRailTab === 'metadata' ? 'left-rail-panel active' : 'left-rail-panel'} role="tabpanel">
              <MetadataManager
                metadataObjects={metadataObjects}
                isLoading={isLoading}
                selectedObjectId={selectedMetadataObjectId}
                onSelectedObjectIdChange={setSelectedMetadataObjectId}
                onMetadataObjectsChange={setMetadataObjects}
                onRefreshMetadata={reloadMetadataObjects}
                onNotice={showNotice}
                showDetails={false}
                showObjectList={true}
                onOpenEditor={openMetadataDialog}
              />
            </div>

            <div className={leftRailTab === 'palette' ? 'left-rail-panel active' : 'left-rail-panel'} role="tabpanel">
              <NodePalette workflow={selectedWorkflow} nodeTypes={nodeTypes} onAddNode={addNode} />
            </div>
          </div>
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

        <aside className="right-rail" aria-hidden={isInspectorCollapsed}>
          {!isInspectorCollapsed && (
            <PropertyInspector
              workflow={selectedWorkflow}
              nodeTypes={nodeTypes}
              metadataObjects={metadataObjects}
              validationIssues={validationIssues}
              selectedNodeId={selectedNodeId}
              onWorkflowChange={setSelectedWorkflow}
            />
          )}
        </aside>
      </div>

      {isLeftRailDialogOpen && leftRailTab !== 'workflows' && (
        <div className="panel-dialog-overlay" role="presentation" onClick={() => setIsLeftRailDialogOpen(false)}>
          <section
            className={leftRailTab === 'metadata' ? 'panel-dialog panel-dialog-metadata' : 'panel-dialog'}
            role="dialog"
            aria-modal="true"
            aria-label={leftRailTab === 'metadata' ? 'Metadata details' : 'Expanded left rail'}
            onClick={event => event.stopPropagation()}
          >
            <div className="panel-dialog-header">
              {leftRailTab !== 'metadata' && (
                <div>
                  <span>Expanded left rail</span>
                  <h2>{getLeftRailTabLabel(leftRailTab)}</h2>
                </div>
              )}
              <button className="icon-button" type="button" onClick={() => setIsLeftRailDialogOpen(false)} aria-label="Close popout">
                <X size={16} aria-hidden="true" />
              </button>
            </div>

            <div className="panel-dialog-body">
              {leftRailTab === 'metadata' && (
                <MetadataManager
                  metadataObjects={metadataObjects}
                  isLoading={isLoading}
                  selectedObjectId={selectedMetadataObjectId}
                  onSelectedObjectIdChange={setSelectedMetadataObjectId}
                  onMetadataObjectsChange={setMetadataObjects}
                  onRefreshMetadata={reloadMetadataObjects}
                  onNotice={showNotice}
                  showObjectList={false}
                />
              )}

              {leftRailTab === 'palette' && (
                <NodePalette workflow={selectedWorkflow} nodeTypes={nodeTypes} onAddNode={addNode} />
              )}
            </div>
          </section>
        </div>
      )}

      <footer className="bottom-rail">
        <div className="bottom-rail-toolbar">
          <div className="rail-tabs bottom-rail-tabs" role="tablist" aria-label="Run panels">
            {bottomRailTabs.map(tab => (
              <button
                key={tab.id}
                id={`bottom-rail-tab-${tab.id}`}
                type="button"
                role="tab"
                aria-selected={bottomRailTab === tab.id}
                aria-controls={`bottom-rail-panel-${tab.id}`}
                className={bottomRailTab === tab.id ? 'rail-tab active' : 'rail-tab'}
                onClick={() => setBottomRailTab(tab.id)}
              >
                {tab.label}
              </button>
            ))}
          </div>

          <button
            className="secondary-button bottom-rail-popout-button"
            type="button"
            onClick={() => {
              setIsLeftRailDialogOpen(false);
              setIsBottomRailDialogOpen(true);
            }}
            aria-haspopup="dialog"
            aria-expanded={isBottomRailDialogOpen}
          >
            <Maximize2 size={16} aria-hidden="true" />
            Expand
          </button>
        </div>

        <div className="bottom-rail-panels">
          <div
            id="bottom-rail-panel-validation"
            role="tabpanel"
            aria-labelledby="bottom-rail-tab-validation"
            aria-hidden={bottomRailTab !== 'validation'}
            className={bottomRailTab === 'validation' ? 'bottom-rail-panel active' : 'bottom-rail-panel'}
          >
            <ValidationPanel issues={validationIssues} onSelectNode={setSelectedNodeId} />
          </div>

          <div
            id="bottom-rail-panel-test"
            role="tabpanel"
            aria-labelledby="bottom-rail-tab-test"
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
            id="bottom-rail-panel-history"
            role="tabpanel"
            aria-labelledby="bottom-rail-tab-history"
            aria-hidden={bottomRailTab !== 'history'}
            className={bottomRailTab === 'history' ? 'bottom-rail-panel active' : 'bottom-rail-panel'}
          >
            <RunHistoryPanel runs={runs} selectedRun={selectedRun} onSelectRun={selectRun} />
          </div>
        </div>
      </footer>

      {isBottomRailDialogOpen && (
        <div className="panel-dialog-overlay" role="presentation" onClick={() => setIsBottomRailDialogOpen(false)}>
          <section
            className="panel-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="bottom-rail-dialog-title"
            onClick={event => event.stopPropagation()}
          >
            <div className="panel-dialog-header">
              <div>
                <span>Expanded bottom rail</span>
                <h2 id="bottom-rail-dialog-title">{getBottomRailTabLabel(bottomRailTab)}</h2>
              </div>
              <button className="icon-button" type="button" onClick={() => setIsBottomRailDialogOpen(false)} aria-label="Close popout">
                <X size={16} aria-hidden="true" />
              </button>
            </div>

            <div className="rail-tabs bottom-rail-tabs panel-dialog-tabs" role="tablist" aria-label="Expanded run panels">
              {bottomRailTabs.map(tab => (
                <button
                  key={tab.id}
                  id={`bottom-rail-dialog-tab-${tab.id}`}
                  type="button"
                  role="tab"
                  aria-selected={bottomRailTab === tab.id}
                  aria-controls={`bottom-rail-dialog-panel-${tab.id}`}
                  className={bottomRailTab === tab.id ? 'rail-tab active' : 'rail-tab'}
                  onClick={() => setBottomRailTab(tab.id)}
                >
                  {tab.label}
                </button>
              ))}
            </div>

            <div className="panel-dialog-body">
              {bottomRailTab === 'validation' && (
                <ValidationPanel issues={validationIssues} onSelectNode={setSelectedNodeId} />
              )}

              {bottomRailTab === 'test' && (
                <TestRunPanel
                  workflow={selectedWorkflow}
                  metadataObjects={metadataObjects}
                  values={testInputValues}
                  onValuesChange={setTestInputValues}
                  testResult={testResult}
                  isRunning={isRunning}
                  onRun={runTest}
                />
              )}

              {bottomRailTab === 'history' && (
                <RunHistoryPanel runs={runs} selectedRun={selectedRun} onSelectRun={selectRun} />
              )}
            </div>
          </section>
        </div>
      )}
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