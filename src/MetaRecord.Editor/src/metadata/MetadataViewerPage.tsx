import { ArrowLeft, Database, RefreshCcw, Search } from 'lucide-react';
import { FormEvent, useEffect, useState } from 'react';
import { ApiError, workflowApi } from '../api/client';
import type { ObjectMetadata, PropertyMetadata } from '../api/types';

interface MetadataViewerPageProps {
  onOpenWorkflowEditor: () => void;
}

type MetadataFormValues = Record<string, string>;

const guidPattern = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}$/;

export function MetadataViewerPage({ onOpenWorkflowEditor }: MetadataViewerPageProps) {
  const [objects, setObjects] = useState<ObjectMetadata[]>([]);
  const [lookupValue, setLookupValue] = useState('');
  const [selectedObject, setSelectedObject] = useState<ObjectMetadata | null>(null);
  const [formValues, setFormValues] = useState<MetadataFormValues>({});
  const [isLoadingObjects, setIsLoadingObjects] = useState(true);
  const [isLoadingObject, setIsLoadingObject] = useState(false);
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [objectError, setObjectError] = useState<string | null>(null);

  useEffect(() => {
    void loadObjects();
  }, []);

  useEffect(() => {
    if (selectedObject) {
      setFormValues(createFormValues(selectedObject));
      return;
    }

    setFormValues({});
  }, [selectedObject]);

  const selectedPropertyCount = selectedObject?.properties.length ?? 0;
  const formattedJson = JSON.stringify(formValues, null, 2);

  async function loadObjects() {
    setIsLoadingObjects(true);
    setCatalogError(null);

    try {
      const nextObjects = await workflowApi.listObjects();
      setObjects(nextObjects);

      if (selectedObject) {
        const refreshedSelection = nextObjects.find(metadataObject => metadataObject.id === selectedObject.id) ?? null;
        setSelectedObject(refreshedSelection);
      }

      if (lookupValue.trim().length === 0) {
        setLookupValue(nextObjects[0]?.name ?? '');
      }
    } catch (error) {
      setCatalogError(getErrorMessage(error, 'Could not load metadata objects.'));
    } finally {
      setIsLoadingObjects(false);
    }
  }

  async function loadObject(identifier: string) {
    const trimmedIdentifier = identifier.trim();
    if (trimmedIdentifier.length === 0) {
      setObjectError('Enter a metadata object name or GUID.');
      setSelectedObject(null);
      return;
    }

    setIsLoadingObject(true);
    setObjectError(null);

    try {
      const nextObject = guidPattern.test(trimmedIdentifier)
        ? await workflowApi.getObjectById(trimmedIdentifier)
        : await workflowApi.getObject(trimmedIdentifier);

      setSelectedObject(nextObject);
      setLookupValue(nextObject.name);
    } catch (error) {
      setSelectedObject(null);
      setObjectError(getErrorMessage(error, `Could not load metadata object "${trimmedIdentifier}".`));
    } finally {
      setIsLoadingObject(false);
    }
  }

  function handleLookupSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void loadObject(lookupValue);
  }

  function openCatalogObject(metadataObject: ObjectMetadata) {
    setLookupValue(metadataObject.name);
    setSelectedObject(metadataObject);
    setObjectError(null);
  }

  function updateFormValue(propertyName: string, value: string) {
    setFormValues(current => ({
      ...current,
      [propertyName]: value
    }));
  }

  function resetFormValues() {
    if (selectedObject)
      setFormValues(createFormValues(selectedObject));
  }

  function clearSelection() {
    setLookupValue('');
    setSelectedObject(null);
    setObjectError(null);
  }

  return (
    <main className="metadata-page">
      <header className="metadata-page-header">
        <div className="app-title">
          <Database size={21} aria-hidden="true" />
          <div>
            <h1>Metadata Viewer</h1>
            <span>Load a metadata object by name or GUID and render its properties as a form.</span>
          </div>
        </div>

        <div className="toolbar-actions">
          <button className="secondary-button" type="button" onClick={onOpenWorkflowEditor}>
            <ArrowLeft size={16} aria-hidden="true" />
            Workflow Editor
          </button>
          <button className="secondary-button" type="button" onClick={() => void loadObjects()} disabled={isLoadingObjects}>
            <RefreshCcw size={16} aria-hidden="true" />
            Refresh list
          </button>
        </div>
      </header>

      <section className="metadata-page-layout">
        <aside className="panel metadata-page-sidebar">
          <div className="panel-heading">
            <h2>Load object</h2>
            <span className="status-pill">{objects.length} available</span>
          </div>

          <form className="metadata-load-form" onSubmit={handleLookupSubmit}>
            <label className="field-control">
              <span>Object name or GUID</span>
              <input
                value={lookupValue}
                onChange={event => setLookupValue(event.target.value)}
                placeholder="Product or 5f8d1d19-..."
                autoComplete="off"
              />
            </label>

            <div className="metadata-load-actions">
              <button className="primary-button" type="submit" disabled={isLoadingObject || lookupValue.trim().length === 0}>
                <Search size={16} aria-hidden="true" />
                Load
              </button>
              <button className="secondary-button" type="button" onClick={clearSelection}>
                Clear
              </button>
            </div>
          </form>

          <p className="metadata-help">
            If the value looks like a GUID, the page loads by id; otherwise it loads by name.
          </p>

          {(catalogError || objectError) && (
            <div className="metadata-issues issue-list">
              {catalogError && (
                <div className="issue-row issue-error">
                  <span>
                    <em>Error</em>
                    <strong>catalog</strong>
                    <small>{catalogError}</small>
                  </span>
                </div>
              )}
              {objectError && (
                <div className="issue-row issue-error">
                  <span>
                    <em>Error</em>
                    <strong>load</strong>
                    <small>{objectError}</small>
                  </span>
                </div>
              )}
            </div>
          )}

          <div className="metadata-object-list metadata-catalog-list">
            {isLoadingObjects ? (
              <p className="metadata-help">Loading metadata objects...</p>
            ) : objects.length === 0 ? (
              <p className="metadata-help">No metadata objects are available yet.</p>
            ) : (
              objects.map(metadataObject => (
                <button
                  className={`workflow-list-item ${selectedObject?.id === metadataObject.id ? 'selected' : ''}`}
                  key={metadataObject.id}
                  type="button"
                  onClick={() => openCatalogObject(metadataObject)}
                >
                  <Database size={16} aria-hidden="true" />
                  <span>
                    <strong>{metadataObject.name}</strong>
                    <small>{metadataObject.tableName}</small>
                  </span>
                  <em>{metadataObject.properties.length} props</em>
                </button>
              ))
            )}
          </div>
        </aside>

        <section className="panel metadata-page-content" aria-live="polite">
          {selectedObject ? (
            <form className="metadata-viewer-stack metadata-form-preview" onSubmit={event => event.preventDefault()}>
              <div className="metadata-viewer-summary-grid">
                <div className="metadata-object-summary">
                  <span>Object id</span>
                  <strong>{selectedObject.id}</strong>
                  <small>Primary identifier returned by the API</small>
                </div>
                <div className="metadata-object-summary">
                  <span>Object name</span>
                  <strong>{selectedObject.name}</strong>
                  <small>Used for lookups and workflow configuration</small>
                </div>
                <div className="metadata-object-summary">
                  <span>Table name</span>
                  <strong>{selectedObject.tableName}</strong>
                  <small>Database table mapped by the runtime</small>
                </div>
              </div>

              <section className="metadata-viewer-section">
                <div className="panel-heading">
                  <h2>Generated Form</h2>
                  <span className="status-pill">{selectedPropertyCount} {selectedPropertyCount === 1 ? 'field' : 'fields'}</span>
                </div>

                <p className="metadata-help">These inputs are generated from the selected metadata object and seeded with sample values so you can see the form layout.</p>

                <div className="metadata-form-grid">
                  {selectedObject.properties.length === 0 ? (
                    <p className="metadata-help">No fields are defined for this object.</p>
                  ) : (
                    selectedObject.properties.map(property => (
                      <MetadataPropertyField
                        key={`${property.name}-${property.columnName}`}
                        property={property}
                        value={formValues[property.name] ?? ''}
                        onChange={updateFormValue}
                      />
                    ))
                  )}
                </div>

                <div className="metadata-form-actions">
                  <button className="secondary-button" type="button" onClick={resetFormValues} disabled={selectedObject.properties.length === 0}>
                    Reset sample values
                  </button>
                  <button className="primary-button" type="submit" disabled={selectedObject.properties.length === 0}>
                    Preview form submit
                  </button>
                </div>
              </section>

              <section className="metadata-viewer-section">
                <div className="panel-heading">
                  <h2>Current values</h2>
                  <span className="muted">Live form state</span>
                </div>

                <pre className="metadata-json">{formattedJson}</pre>
              </section>
            </form>
          ) : (
            <div className="empty-canvas metadata-page-empty">
              <Database size={34} aria-hidden="true" />
              <strong>No metadata object loaded</strong>
              <span>Select an object from the list or load one by name or GUID.</span>
            </div>
          )}
        </section>
      </section>
    </main>
  );
}

function getErrorMessage(error: unknown, fallback: string) {
  if (error instanceof ApiError)
    return `${fallback} ${error.message}`;

  return fallback;
}

function MetadataPropertyField({
  property,
  value,
  onChange
}: {
  property: PropertyMetadata;
  value: string;
  onChange: (propertyName: string, value: string) => void;
}) {
  const inputId = `metadata-field-${property.name}`;
  const fieldLabel = property.caption?.trim() || property.name;
  const fieldMeta = [property.columnName, property.clrType, property.maxLength ? `Max ${property.maxLength}` : null]
    .filter(Boolean)
    .join(' · ');

  if (property.clrType === 'Boolean') {
    return (
      <label className="metadata-form-field metadata-form-field-boolean" htmlFor={inputId}>
        <span className="metadata-form-field-label-row">
          <strong>{fieldLabel}</strong>
          <span className="metadata-form-field-badges">
            {property.isRequired && <span className="status-pill">Required</span>}
            {property.isPrimaryKey && <span className="status-pill">Key</span>}
          </span>
        </span>

        <div className="checkbox-field metadata-form-checkbox">
          <input
            id={inputId}
            type="checkbox"
            checked={value === 'true'}
            onChange={event => onChange(property.name, event.target.checked ? 'true' : 'false')}
            disabled={property.isPrimaryKey}
          />
          <span>{fieldMeta}</span>
        </div>

        {property.defaultValue && <small className="metadata-help">Default: {property.defaultValue}</small>}
      </label>
    );
  }

  const inputType = getInputType(property.clrType);
  const inputValue = inputType === 'datetime-local' ? normalizeDateTimeValue(value) : value;

  return (
    <label className="metadata-form-field" htmlFor={inputId}>
      <span className="metadata-form-field-label-row">
        <strong>{fieldLabel}</strong>
        <span className="metadata-form-field-badges">
          {property.isRequired && <span className="status-pill">Required</span>}
          {property.isUnique && <span className="status-pill">Unique</span>}
          {property.isPrimaryKey && <span className="status-pill">Key</span>}
        </span>
      </span>

      <input
        id={inputId}
        type={inputType}
        value={inputValue}
        step={inputType === 'number' && (property.clrType === 'Decimal' || property.clrType === 'Double') ? 'any' : undefined}
        min={inputType === 'number' && (property.clrType === 'Int32' || property.clrType === 'Int64') ? '0' : undefined}
        maxLength={property.maxLength ?? undefined}
        onChange={event => onChange(property.name, inputType === 'datetime-local' ? event.target.value : event.target.value)}
        disabled={property.isPrimaryKey}
      />

      <small className="metadata-help">{fieldMeta}</small>
      {property.defaultValue && <small className="metadata-help">Default: {property.defaultValue}</small>}
    </label>
  );
}

function getInputType(clrType: string): 'text' | 'number' | 'datetime-local' {
  if (clrType === 'Int32' || clrType === 'Int64' || clrType === 'Decimal' || clrType === 'Double')
    return 'number';

  if (clrType === 'DateTime')
    return 'datetime-local';

  return 'text';
}

function createFormValues(metadata: ObjectMetadata): MetadataFormValues {
  const values: MetadataFormValues = {};

  for (const property of metadata.properties)
    values[property.name] = createInitialValue(property);

  return values;
}

function createInitialValue(property: PropertyMetadata): string {
  if (property.defaultValue)
    return property.defaultValue;

  switch (property.clrType) {
    case 'Guid':
      return crypto.randomUUID();
    case 'Boolean':
      return 'false';
    case 'Int32':
    case 'Int64':
      return '0';
    case 'Decimal':
    case 'Double':
      return '0.00';
    case 'DateTime':
      return toLocalDateTimeValue(new Date());
    default:
      return `Sample ${property.name}`;
  }
}

function toLocalDateTimeValue(date: Date): string {
  const year = date.getFullYear();
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');
  const hours = `${date.getHours()}`.padStart(2, '0');
  const minutes = `${date.getMinutes()}`.padStart(2, '0');

  return `${year}-${month}-${day}T${hours}:${minutes}`;
}

function normalizeDateTimeValue(value: string): string {
  if (!value)
    return '';

  if (value.includes('T'))
    return value.slice(0, 16);

  const parsedDate = new Date(value);
  if (Number.isNaN(parsedDate.getTime()))
    return '';

  return toLocalDateTimeValue(parsedDate);
}