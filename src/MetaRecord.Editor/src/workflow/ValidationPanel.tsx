import { AlertTriangle, CheckCircle2 } from 'lucide-react';
import type { WorkflowValidationIssue } from '../api/types';

interface ValidationPanelProps {
  issues: WorkflowValidationIssue[];
  onSelectNode: (nodeId: string) => void;
}

export function ValidationPanel({ issues, onSelectNode }: ValidationPanelProps) {
  const errors = issues.filter(issue => issue.severity === 'Error').length;

  return (
    <section className="bottom-panel-section validation-panel">
      <div className="bottom-panel-heading">
        <h2>Validation</h2>
        <span>{errors === 0 ? 'Ready' : `${errors} errors`}</span>
      </div>
      {issues.length === 0 ? (
        <div className="empty-state-inline">
          <CheckCircle2 size={18} aria-hidden="true" />
          <span>No validation issues returned yet.</span>
        </div>
      ) : (
        <div className="issue-list">
          {issues.map((issue, index) => (
            <button
              key={`${issue.nodeId ?? 'workflow'}-${issue.field ?? index}`}
              type="button"
              className={`issue-row issue-${issue.severity.toLowerCase()}`}
              onClick={() => issue.nodeId && onSelectNode(issue.nodeId)}
            >
              <AlertTriangle size={16} aria-hidden="true" />
              <span>
                <strong>{issue.severity}</strong>
                {issue.nodeId && <em>{issue.nodeId}</em>}
                {issue.field && <em>{issue.field}</em>}
                <small>{issue.message}</small>
              </span>
            </button>
          ))}
        </div>
      )}
    </section>
  );
}