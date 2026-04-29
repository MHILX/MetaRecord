import { FlaskConical, Play } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import type { ObjectMetadata, WorkflowDefinition, WorkflowTestRunResponse } from '../api/types';
import { getObject } from './workflowModel';

interface TestRunPanelProps {
  workflow?: WorkflowDefinition | null;
  metadataObjects: ObjectMetadata[];
  testResult?: WorkflowTestRunResponse | null;
  isRunning: boolean;
  onRun: (currentRecord: Record<string, unknown>) => void;
}

export function TestRunPanel({ workflow, metadataObjects, testResult, isRunning, onRun }: TestRunPanelProps) {
  const metadata = workflow ? getObject(metadataObjects, workflow.objectName) : undefined;
  const initialValues = useMemo(() => metadata ? createSampleRecord(metadata) : {}, [metadata]);
  const [values, setValues] = useState<Record<string, string>>({});

  useEffect(() => {
    setValues(Object.fromEntries(Object.entries(initialValues).map(([key, value]) => [key, String(value ?? '')])));
  }, [initialValues]);

  function run() {
    if (!metadata)
      return;

    const currentRecord: Record<string, unknown> = {};
    for (const property of metadata.properties) {
      currentRecord[property.name] = coerceValue(values[property.name], property.clrType);
    }
    onRun(currentRecord);
  }

  return (
    <section className="bottom-panel-section test-run-panel">
      <div className="bottom-panel-heading">
        <h2>Test Input</h2>
        <button className="secondary-button" type="button" onClick={run} disabled={!workflow || !metadata || isRunning}>
          <Play size={15} aria-hidden="true" />
          {isRunning ? 'Running' : 'Run'}
        </button>
      </div>
      {!workflow || !metadata ? (
        <div className="empty-state-inline">
          <FlaskConical size={18} aria-hidden="true" />
          <span>Open a workflow to create test input.</span>
        </div>
      ) : (
        <div className="test-run-grid">
          <div className="test-input-fields">
            {metadata.properties.map(property => (
              <label key={property.name}>
                <span>{property.name}</span>
                <input
                  value={values[property.name] ?? ''}
                  onChange={event => setValues(current => ({ ...current, [property.name]: event.target.value }))}
                />
              </label>
            ))}
          </div>
          <div className="test-result">
            {!testResult ? (
              <p className="muted">Run the workflow to see status and node output.</p>
            ) : (
              <>
                <div className="run-detail-header">
                  <strong>{testResult.status}</strong>
                  {testResult.errorMessage && <span>{testResult.errorMessage}</span>}
                </div>
                <div className="step-list compact">
                  {testResult.steps.map((step, index) => (
                    <details key={`${step.nodeId}-${index}`}>
                      <summary>
                        <span>{step.nodeId}</span>
                        <em>{step.status}</em>
                      </summary>
                      {step.outputJson && <code>{step.outputJson}</code>}
                      {step.errorMessage && <p>{step.errorMessage}</p>}
                    </details>
                  ))}
                </div>
              </>
            )}
          </div>
        </div>
      )}
    </section>
  );
}

function createSampleRecord(metadata: ObjectMetadata): Record<string, unknown> {
  const values: Record<string, unknown> = {};
  for (const property of metadata.properties) {
    if (property.clrType === 'Guid')
      values[property.name] = crypto.randomUUID();
    else if (property.clrType === 'Decimal')
      values[property.name] = '9.99';
    else if (property.clrType === 'Int32')
      values[property.name] = '5';
    else if (property.clrType === 'Boolean')
      values[property.name] = 'true';
    else
      values[property.name] = `Sample ${metadata.name}`;
  }
  return values;
}

function coerceValue(value: string | undefined, clrType: string) {
  if (clrType === 'Guid' || clrType === 'String')
    return value ?? '';
  if (clrType === 'Decimal' || clrType === 'Double' || clrType === 'Single')
    return Number(value ?? 0);
  if (clrType === 'Int32' || clrType === 'Int64')
    return Number.parseInt(value ?? '0', 10);
  if (clrType === 'Boolean')
    return String(value).toLowerCase() === 'true';
  return value ?? '';
}