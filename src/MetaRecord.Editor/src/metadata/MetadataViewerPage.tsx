import { Database, FilePlus2, Save } from 'lucide-react';
import { FormEvent, useEffect, useRef, useState } from 'react';
import { ApiError, workflowApi } from '../api/client';
import type { MetadataRecordValues, ObjectMetadata, PropertyMetadata, RelationshipMetadata } from '../api/types';
import { EditorPrimaryNav } from '../navigation/EditorPrimaryNav';
import { MetadataManager, type MetadataSelectionId } from '../workflow/MetadataManager';

type MetadataFormValues = Record<string, string>;

type PageNotice = {
  message: string;
  kind: 'info' | 'error';
};

export function MetadataViewerPage() {
  const [objects, setObjects] = useState<ObjectMetadata[]>([]);
  const [selectedMetadataObjectId, setSelectedMetadataObjectId] = useState<MetadataSelectionId>(null);
  const [formValues, setFormValues] = useState<MetadataFormValues>({});
  const [recordRows, setRecordRows] = useState<MetadataRecordValues[]>([]);
  const [lookupRecordsByObjectId, setLookupRecordsByObjectId] = useState<Record<string, MetadataRecordValues[]>>({});
  const [loadingLookupObjectIds, setLoadingLookupObjectIds] = useState<Record<string, boolean>>({});
  const [rightPanelTab, setRightPanelTab] = useState<'form' | 'edit'>('form');
  const [isLoadingObjects, setIsLoadingObjects] = useState(true);
  const [isLoadingRecords, setIsLoadingRecords] = useState(false);
  const [isSavingRecord, setIsSavingRecord] = useState(false);
  const [saveMessage, setSaveMessage] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [recordLoadError, setRecordLoadError] = useState<string | null>(null);
  const [metadataNotice, setMetadataNotice] = useState<PageNotice | null>(null);
  const selectedObjectIdRef = useRef<MetadataSelectionId>(selectedMetadataObjectId);

  useEffect(() => {
    void loadObjects();
  }, []);

  useEffect(() => {
    selectedObjectIdRef.current = selectedMetadataObjectId;
  }, [selectedMetadataObjectId]);

  useEffect(() => {
    if (!saveMessage)
      return;

    const timeoutId = window.setTimeout(() => setSaveMessage(null), 2500);
    return () => window.clearTimeout(timeoutId);
  }, [saveMessage]);

  useEffect(() => {
    if (!metadataNotice || metadataNotice.kind === 'error')
      return;

    const timeoutId = window.setTimeout(() => setMetadataNotice(null), 2500);
    return () => window.clearTimeout(timeoutId);
  }, [metadataNotice]);

  const selectedObject = selectedMetadataObjectId && selectedMetadataObjectId !== 'new'
    ? objects.find(metadataObject => metadataObject.id === selectedMetadataObjectId) ?? null
    : null;
  const selectedObjectRecordId = selectedObject?.id ?? null;
  const selectedPropertyCount = selectedObject?.properties.length ?? 0;
  const formattedJson = JSON.stringify(formValues, null, 2);

  useEffect(() => {
    if (isLoadingObjects)
      return;

    if (!selectedObject || selectedMetadataObjectId === 'new')
      setRightPanelTab('edit');
  }, [isLoadingObjects, selectedMetadataObjectId, selectedObject]);

  useEffect(() => {
    if (selectedObject) {
      setFormValues(createFormValues(selectedObject));
      setSaveMessage(null);
      setSaveError(null);
      return;
    }

    setFormValues({});
  }, [selectedObject]);

  useEffect(() => {
    if (!selectedObjectRecordId) {
      setRecordRows([]);
      setRecordLoadError(null);
      setIsLoadingRecords(false);
      return;
    }

    setRecordRows([]);
    setRecordLoadError(null);
    void loadRecords(selectedObjectRecordId);
  }, [selectedObjectRecordId]);

  useEffect(() => {
    if (!selectedObject)
      return;

    const targetObjectIds = Array.from(new Set(selectedObject.relationships.map(relationship => relationship.targetObjectId).filter(Boolean)));
    if (targetObjectIds.length === 0)
      return;

    let isCancelled = false;

    for (const targetObjectId of targetObjectIds) {
      if (lookupRecordsByObjectId[targetObjectId] !== undefined || loadingLookupObjectIds[targetObjectId])
        continue;

      setLoadingLookupObjectIds(current => ({
        ...current,
        [targetObjectId]: true
      }));

      void loadLookupRecords(targetObjectId);
    }

    return () => {
      isCancelled = true;
    };

    async function loadLookupRecords(targetObjectId: string) {
      try {
        const records = await workflowApi.listRecords(targetObjectId);
        if (isCancelled)
          return;

        setLookupRecordsByObjectId(current => ({
          ...current,
          [targetObjectId]: records
        }));
      } catch {
        if (isCancelled)
          return;

        setLookupRecordsByObjectId(current => ({
          ...current,
          [targetObjectId]: []
        }));
      } finally {
        if (isCancelled)
          return;

        setLoadingLookupObjectIds(current => ({
          ...current,
          [targetObjectId]: false
        }));
      }
    }
  }, [loadingLookupObjectIds, lookupRecordsByObjectId, selectedObject]);

  async function loadObjects() {
    setIsLoadingObjects(true);

    try {
      const nextObjects = await workflowApi.listObjects();
      setObjects(nextObjects);

      if (selectedMetadataObjectId === null && nextObjects.length > 0) {
        setSelectedMetadataObjectId(nextObjects[0].id);
      } else if (
        selectedMetadataObjectId !== null
        && selectedMetadataObjectId !== 'new'
        && !nextObjects.some(metadataObject => metadataObject.id === selectedMetadataObjectId)
      ) {
        setSelectedMetadataObjectId(nextObjects[0]?.id ?? null);
      }
    } catch (error) {
      showMetadataNotice(getErrorMessage(error, 'Could not load metadata objects.'), 'error');
    } finally {
      setIsLoadingObjects(false);
    }
  }

  async function loadRecords(objectId: string | null) {
    if (!objectId) {
      setRecordRows([]);
      setRecordLoadError(null);
      setIsLoadingRecords(false);
      return;
    }

    setIsLoadingRecords(true);
    setRecordLoadError(null);

    try {
      const nextRows = await workflowApi.listRecords(objectId);
      if (selectedObjectIdRef.current !== objectId)
        return;

      setRecordRows(nextRows);
    } catch (error) {
      if (selectedObjectIdRef.current !== objectId)
        return;

      setRecordRows([]);
      setRecordLoadError(getErrorMessage(error, 'Could not load saved records.'));
    } finally {
      if (selectedObjectIdRef.current === objectId)
        setIsLoadingRecords(false);
    }
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

  function showMetadataNotice(message: string, kind: 'info' | 'error' = 'info') {
    setMetadataNotice({ message, kind });
  }

  function openNewMetadataObject() {
    setSelectedMetadataObjectId('new');
    setRightPanelTab('edit');
  }

  async function handleFormSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!selectedObject)
      return;

    setIsSavingRecord(true);
    setSaveError(null);
    setSaveMessage(null);

    try {
      const result = await workflowApi.saveRecord(selectedObject.id, { values: formValues });
      setSaveMessage(`${result.isNew ? 'Saved' : 'Updated'} record ${result.recordId} in ${selectedObject.tableName}.`);
      await loadRecords(selectedObject.id);
    } catch (error) {
      setSaveError(getErrorMessage(error, 'Could not save the record.'));
    } finally {
      setIsSavingRecord(false);
    }
  }

  return (
    <main className="metadata-page">
      <header className="metadata-page-header">
        <div className="app-title">
          <Database size={21} aria-hidden="true" />
          <div>
            <h1>Metadata Workspace</h1>
            <span>Create and edit metadata objects, then load one to view its rendered form.</span>
          </div>
        </div>

        <EditorPrimaryNav activePage="metadata" />
      </header>

      <section className="metadata-page-layout">
        {metadataNotice && <div className="notice-bar">{metadataNotice.message}</div>}

        <aside className="metadata-page-sidebar">
          <MetadataManager
            metadataObjects={objects}
            isLoading={isLoadingObjects}
            selectedObjectId={selectedMetadataObjectId}
            onSelectedObjectIdChange={setSelectedMetadataObjectId}
            onMetadataObjectsChange={setObjects}
            onRefreshMetadata={loadObjects}
            onNotice={showMetadataNotice}
            showDetails={false}
            showObjectList={true}
            showCreateButton={true}
          />
        </aside>

        <section className="panel metadata-page-content" aria-live="polite">
          <div className="metadata-right-panel-header">
            <button className="secondary-button" type="button" onClick={openNewMetadataObject}>
              <FilePlus2 size={16} aria-hidden="true" />
              New object
            </button>

            <div className="metadata-right-panel-tabs" role="tablist" aria-label="Metadata workspace views">
              <button
                className={`metadata-right-panel-tab ${rightPanelTab === 'form' ? 'active' : ''}`}
                type="button"
                role="tab"
                aria-selected={rightPanelTab === 'form'}
                onClick={() => setRightPanelTab('form')}
              >
                Form view
              </button>
              <button
                className={`metadata-right-panel-tab ${rightPanelTab === 'edit' ? 'active' : ''}`}
                type="button"
                role="tab"
                aria-selected={rightPanelTab === 'edit'}
                onClick={() => setRightPanelTab('edit')}
              >
                Edit object
              </button>
            </div>
          </div>

          <div className="metadata-right-panel-body">
            {rightPanelTab === 'edit' ? (
              <MetadataManager
                metadataObjects={objects}
                isLoading={isLoadingObjects}
                selectedObjectId={selectedMetadataObjectId}
                onSelectedObjectIdChange={setSelectedMetadataObjectId}
                onMetadataObjectsChange={setObjects}
                onRefreshMetadata={loadObjects}
                onNotice={showMetadataNotice}
                showDetails={true}
                showObjectList={false}
                showCreateButton={false}
              />
            ) : selectedObject ? (
              <form className="panel metadata-viewer-stack metadata-form-preview" onSubmit={handleFormSubmit}>
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

                <p className="metadata-help">These inputs are generated from the selected metadata object and seeded with sample values so you can save a record directly to the database.</p>

                <div className="metadata-form-grid">
                  {selectedObject.properties.length === 0 ? (
                    <p className="metadata-help">No fields are defined for this object.</p>
                  ) : (
                    selectedObject.properties.map(property => (
                      (() => {
                        const relationship = selectedObject.relationships.find(item => item.sourcePropertyName === property.name) ?? null;
                        const lookupRecords = relationship ? lookupRecordsByObjectId[relationship.targetObjectId] ?? [] : [];

                        return (
                      <MetadataPropertyField
                        key={`${property.name}-${property.columnName}`}
                        property={property}
                        value={formValues[property.name] ?? ''}
                          relationship={relationship}
                          lookupRecords={lookupRecords}
                          isLookupLoading={relationship ? Boolean(loadingLookupObjectIds[relationship.targetObjectId]) : false}
                          metadataObjects={objects}
                        onChange={updateFormValue}
                      />
                        );
                      })()
                    ))
                  )}
                </div>

                <div className="metadata-form-actions">
                  <button className="secondary-button" type="button" onClick={resetFormValues} disabled={selectedObject.properties.length === 0}>
                    Reset sample values
                  </button>
                  <button className="primary-button" type="submit" disabled={selectedObject.properties.length === 0 || isSavingRecord}>
                    <Save size={16} aria-hidden="true" />
                    {isSavingRecord ? 'Saving...' : 'Save record'}
                  </button>
                </div>

                {(saveMessage || saveError) && (
                  <div className={saveError ? 'metadata-save-status error' : 'metadata-save-status success'}>
                    <strong>{saveError ? 'Save failed' : 'Saved'}</strong>
                    <span>{saveError ?? saveMessage}</span>
                  </div>
                )}
              </section>

              <section className="metadata-viewer-section">
                <div className="panel-heading">
                  <h2>Current values</h2>
                  <span className="muted">Live form state</span>
                </div>

                <pre className="metadata-json">{formattedJson}</pre>
              </section>

              <section className="metadata-viewer-section">
                <div className="panel-heading">
                  <h2>Saved records</h2>
                  <span className="muted">
                    {isLoadingRecords ? 'Loading...' : `${recordRows.length} ${recordRows.length === 1 ? 'row' : 'rows'}`}
                  </span>
                </div>

                {recordLoadError ? (
                  <div className="metadata-save-status error">
                    <strong>Could not load saved records</strong>
                    <span>{recordLoadError}</span>
                  </div>
                ) : selectedObject.properties.length === 0 ? (
                  <p className="metadata-help">No properties are available to display in the table.</p>
                ) : isLoadingRecords ? (
                  <p className="metadata-help">Loading saved records...</p>
                ) : recordRows.length === 0 ? (
                  <p className="metadata-help">No saved records yet.</p>
                ) : (
                  <div className="metadata-record-table-wrapper">
                    <table className="metadata-record-table">
                      <thead>
                        <tr>
                          {selectedObject.properties.map(property => (
                            <th key={property.name}>{property.caption?.trim() || property.name}</th>
                          ))}
                        </tr>
                      </thead>
                      <tbody>
                        {recordRows.map((record, rowIndex) => (
                          <tr key={rowIndex}>
                            {selectedObject.properties.map(property => (
                              <td key={property.name}>{formatRecordValue(record[property.name])}</td>
                            ))}
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </section>
              </form>
            ) : (
              <div className="empty-canvas metadata-page-empty">
                <Database size={34} aria-hidden="true" />
                <strong>No metadata object loaded</strong>
                <span>Select an object from the list to edit its definition and submit a record form.</span>
              </div>
            )}
          </div>
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

function formatRecordValue(value: unknown): string {
  if (value === null || value === undefined)
    return '—';

  if (typeof value === 'string')
    return value;

  if (typeof value === 'number' || typeof value === 'boolean' || typeof value === 'bigint')
    return String(value);

  if (value instanceof Date)
    return value.toISOString();

  if (typeof value === 'object') {
    try {
      return JSON.stringify(value);
    } catch {
      return String(value);
    }
  }

  return String(value);
}

function MetadataPropertyField({
  property,
  value,
  relationship,
  lookupRecords,
  isLookupLoading,
  metadataObjects,
  onChange
}: {
  property: PropertyMetadata;
  value: string;
  relationship?: RelationshipMetadata | null;
  lookupRecords?: MetadataRecordValues[];
  isLookupLoading?: boolean;
  metadataObjects: ObjectMetadata[];
  onChange: (propertyName: string, value: string) => void;
}) {
  const inputId = `metadata-field-${property.name}`;
  const fieldLabel = property.caption?.trim() || property.name;
  const fieldMeta = [property.columnName, property.clrType, property.maxLength ? `Max ${property.maxLength}` : null]
    .filter(Boolean)
    .join(' · ');

  if (relationship) {
    const targetObject = metadataObjects.find(metadataObject => metadataObject.id === relationship.targetObjectId) ?? null;
    const targetDisplayField = normalizeRelationshipDisplayField(relationship.displayPropertyName) ?? getDefaultDisplayField(targetObject);
    const targetKeyField = normalizeRelationshipDisplayField(relationship.targetPropertyName) ?? 'Id';
    const pickerOptions = buildRelationshipPickerOptions(lookupRecords ?? [], targetKeyField, targetDisplayField, targetObject);

    return (
      <label className="metadata-form-field" htmlFor={inputId}>
        <span className="metadata-form-field-label-row">
          <strong>{fieldLabel}</strong>
          <span className="metadata-form-field-badges">
            <span className="status-pill">Lookup</span>
            {property.isRequired && <span className="status-pill">Required</span>}
          </span>
        </span>

        <select
          id={inputId}
          value={value}
          onChange={event => onChange(property.name, event.target.value)}
          disabled={isLookupLoading}
        >
          <option value="">{isLookupLoading ? 'Loading lookup values...' : 'Select a related record'}</option>
          {pickerOptions.map(option => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>

        <small className="metadata-help">
          {relationship.targetObjectName ?? targetObject?.name ?? 'Related object'} · {relationship.targetPropertyName || 'Id'}
        </small>
        {relationship.caption && <small className="metadata-help">{relationship.caption}</small>}
      </label>
    );
  }

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
        onChange={event => onChange(property.name, event.target.value)}
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

  const relationshipsBySourceProperty = new Map(metadata.relationships.map(relationship => [relationship.sourcePropertyName.toLowerCase(), relationship] as const));

  for (const property of metadata.properties) {
    const relationship = relationshipsBySourceProperty.get(property.name.toLowerCase());
    if (relationship) {
      values[property.name] = '';
      continue;
    }

    values[property.name] = createInitialValue(property);
  }

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

type RelationshipPickerOption = {
  value: string;
  label: string;
};

function buildRelationshipPickerOptions(records: MetadataRecordValues[], keyField: string, displayField: string | null, targetObject: ObjectMetadata | null): RelationshipPickerOption[] {
  return records
    .map(record => ({
      value: normalizeRecordPickerValue(record[keyField] ?? record['Id']),
      label: formatRelationshipLabel(record, keyField, displayField, targetObject)
    }))
    .filter(option => option.value.length > 0);
}

function formatRelationshipLabel(record: MetadataRecordValues, keyField: string, displayField: string | null, targetObject: ObjectMetadata | null): string {
  const preferredField = displayField && displayField.trim().length > 0 ? displayField : 'Id';
  const rawLabel = record[preferredField] ?? record[keyField] ?? record['Id'] ?? record[targetObject?.properties[0]?.name ?? 'Id'] ?? preferredField;
  const label = formatRecordValue(rawLabel);
  const idLabel = formatRecordValue(record[keyField] ?? record['Id']);

  if (preferredField === 'Id' || label === idLabel)
    return label;

  return `${label} (${idLabel})`;
}

function normalizeRecordPickerValue(value: unknown): string {
  if (value === null || value === undefined)
    return '';

  return String(value);
}

function getDefaultDisplayField(targetObject: ObjectMetadata | null): string | null {
  if (!targetObject)
    return null;

  return targetObject.properties.find(property => property.name !== 'Id' && property.clrType === 'String')?.name
    ?? targetObject.properties.find(property => property.name !== 'Id')?.name
    ?? 'Id';
}

function normalizeRelationshipDisplayField(displayPropertyName: string | null | undefined): string | null {
  if (!displayPropertyName)
    return null;

  const trimmed = displayPropertyName.trim();
  return trimmed.length > 0 ? trimmed : null;
}
