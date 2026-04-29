import { History, ListTree } from 'lucide-react';
import type { WorkflowRunDetail, WorkflowRunSummary } from '../api/types';

interface RunHistoryPanelProps {
  runs: WorkflowRunSummary[];
  selectedRun?: WorkflowRunDetail | null;
  onSelectRun: (runId: string) => void;
}

export function RunHistoryPanel({ runs, selectedRun, onSelectRun }: RunHistoryPanelProps) {
  return (
    <section className="bottom-panel-section run-history-panel">
      <div className="bottom-panel-heading">
        <h2>Runs</h2>
        <span>{runs.length}</span>
      </div>
      <div className="run-history-grid">
        <div className="run-list">
          {runs.length === 0 && (
            <div className="empty-state-inline">
              <History size={18} aria-hidden="true" />
              <span>No runs recorded.</span>
            </div>
          )}
          {runs.map(run => (
            <button key={run.id} className="run-row" type="button" onClick={() => onSelectRun(run.id)}>
              <span>
                <strong>{run.status}</strong>
                <small>{new Date(run.startedAt).toLocaleString()}</small>
              </span>
              <em>{run.durationMs ?? 0} ms</em>
            </button>
          ))}
        </div>
        <div className="run-detail">
          {!selectedRun ? (
            <div className="empty-state-inline">
              <ListTree size={18} aria-hidden="true" />
              <span>Select a run to inspect steps.</span>
            </div>
          ) : (
            <>
              <div className="run-detail-header">
                <strong>{selectedRun.status}</strong>
                {selectedRun.errorMessage && <span>{selectedRun.errorMessage}</span>}
              </div>
              <div className="step-list">
                {selectedRun.steps.map((step, index) => (
                  <details key={`${step.nodeId}-${index}`}>
                    <summary>
                      <span>{step.nodeId}</span>
                      <em>{step.status}</em>
                    </summary>
                    <dl>
                      <dt>Type</dt>
                      <dd>{step.nodeType}</dd>
                      {step.errorMessage && (
                        <>
                          <dt>Error</dt>
                          <dd>{step.errorMessage}</dd>
                        </>
                      )}
                      {step.outputJson && (
                        <>
                          <dt>Output</dt>
                          <dd><code>{step.outputJson}</code></dd>
                        </>
                      )}
                    </dl>
                  </details>
                ))}
              </div>
            </>
          )}
        </div>
      </div>
    </section>
  );
}