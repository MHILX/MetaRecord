import { Database, FilePlus2, Plus, RefreshCcw, Save, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { ApiError, workflowApi } from '../api/client';
import type {
  MetadataValidationIssue,
  MetadataValidationResponse,
  ObjectMetadata,
  ObjectMetadataUpsertRequest,
  PropertyMetadataUpsertRequest
} from '../api/types';

interface MetadataManagerProps {
  metadataObjects: ObjectMetadata[];
  isLoading: boolean;
  onMetadataObjectsChange: (metadataObjects: ObjectMetadata[]) => void;
  onRefreshMetadata: () => Promise<void>;
  onNotice: (message: string, kind?: 'info' | 'error') => void;
}

type MetadataSelectionId = string | 'new' | null;

const supportedClrTypes = ['Guid', 'String', 'Int32', 'Int64', 'Decimal', 'Double', 'Boolean', 'DateTime'] as const;

export function MetadataManager({
  metadataObjects,
  isLoading,
  onMetadataObjectsChange,
  onRefreshMetadata,
  onNotice
}: MetadataManagerProps) {
  const [selectedObjectId, setSelectedObjectId] = useState<MetadataSelectionId>(null);
  const [draft, setDraft] = useState<ObjectMetadataUpsertRequest>(createNewMetadataDraft);
  const [validationIssues, setValidationIssues] = useState<MetadataValidationIssue[]>([]);
  const [isSaving, setIsSaving] = useState(false);

  useEffect(() => {
    if (isLoading)
      return;

    if (metadataObjects.length === 0) {
      if (selectedObjectId !== 'new')
        openNewObject();
      return;
    }

    if (selectedObjectId === null) {
      openObject(metadataObjects[0]);
      return;
    }

    if (selectedObjectId === 'new')
      return;

    const selectedObject = metadataObjects.find(metadataObject => metadataObject.id === selectedObjectId);
    if (selectedObject) {
      setDraft(toMetadataDraft(selectedObject));
      setValidationIssues([]);
      return;
    }

    openObject(metadataObjects[0]);
  }, [isLoading, metadataObjects, selectedObjectId]);

  function openObject(metadataObject: ObjectMetadata) {
    setSelectedObjectId(metadataObject.id);
    setDraft(toMetadataDraft(metadataObject));
    setValidationIssues([]);
  }

  function openNewObject() {
    setSelectedObjectId('new');
    setDraft(createNewMetadataDraft());
    setValidationIssues([]);
  }

  function updateDraft(updater: (current: ObjectMetadataUpsertRequest) => ObjectMetadataUpsertRequest) {
    setDraft(current => updater(current));
  }

  function updateObjectField(field: 'name' | 'tableName', value: string) {
    updateDraft(current => ({
      ...current,
      [field]: value
    }));
  }

  function updateProperty(index: number, updater: (property: PropertyMetadataUpsertRequest) => PropertyMetadataUpsertRequest) {
    updateDraft(current => ({
      ...current,
      properties: current.properties.map((property, propertyIndex) => propertyIndex === index ? updater(property) : property)
    }));
  }

  function addProperty() {
    updateDraft(current => ({
      ...current,
      properties: [...current.properties, createPropertyDraft(`NewField${current.properties.length + 1}`)]
    }));
  }

  function removeProperty(index: number) {
    if (index === 0)
      return;

    updateDraft(current => ({
      ...current,
      properties: current.properties.filter((_, propertyIndex) => propertyIndex !== index)
    }));
  }

  async function refreshMetadataObjects() {
    try {
      await onRefreshMetadata();
      onNotice('Metadata objects refreshed.');
    } catch (error) {
      onNotice(getErrorMessage(error, 'Could not refresh metadata objects.'), 'error');
    }
  }

  async function validateDraft(): Promise<MetadataValidationResponse | null> {
    try {
      const validation = await workflowApi.validateObject(draft);
      setValidationIssues(validation.issues);
      return validation;
    } catch (error) {
      const validation = getValidationDetails(error);
      if (validation) {
        setValidationIssues(validation.issues);
        return validation;
      }

      throw error;
    }
  }

  async function saveDraft() {
    setIsSaving(true);
    try {
      const validation = await validateDraft();
      if (!validation)
        return;

      if (!validation.isValid) {
        onNotice('Validation found issues.', 'error');
        return;
      }

      const savedObject = draft.id
        ? await workflowApi.updateObject(draft.id, draft)
        : await workflowApi.createObject(draft);

      const nextObjects = draft.id
        ? metadataObjects.map(metadataObject => metadataObject.id === savedObject.id ? savedObject : metadataObject)
        : [...metadataObjects, savedObject];

      onMetadataObjectsChange(nextObjects);
      setSelectedObjectId(savedObject.id);
      setDraft(toMetadataDraft(savedObject));
      setValidationIssues([]);
      onNotice('Metadata object saved.');
    } catch (error) {
      const validation = getValidationDetails(error);
      if (validation)
        setValidationIssues(validation.issues);

      onNotice(getErrorMessage(error, 'Metadata save failed.'), 'error');
    } finally {
      setIsSaving(false);
    }
  }

  async function deleteDraft() {
    if (!draft.id || selectedObjectId === 'new')
      return;

    const confirmDelete = window.confirm(`Delete metadata object "${draft.name}"? This cannot be undone.`);
    if (!confirmDelete)
      return;

    setIsSaving(true);
    try {
      await workflowApi.deleteObject(draft.id);

      const remainingObjects = metadataObjects.filter(metadataObject => metadataObject.id !== draft.id);
      onMetadataObjectsChange(remainingObjects);

      if (remainingObjects.length > 0) {
        openObject(remainingObjects[0]);
      } else {
        openNewObject();
      }

      onNotice('Metadata object deleted.');
    } catch (error) {
      onNotice(getErrorMessage(error, 'Metadata delete failed.'), 'error');
    } finally {
      setIsSaving(false);
    }
  }

  if (isLoading) {
    return (
      <section className="panel metadata-panel">
        <div className="panel-heading">
          <h2>Metadata</h2>
          <Database size={17} aria-hidden="true" />
        </div>
        <p className="muted">Loading metadata objects...</p>
      </section>
    );
  }

  return (
    <section className="panel metadata-panel">
      <div className="panel-heading">
        <h2>Metadata</h2>
        <div className="metadata-heading-actions">
          <button className="secondary-button" type="button" onClick={openNewObject}>
            <FilePlus2 size={16} aria-hidden="true" />
            New
          </button>
          <button className="icon-button" type="button" onClick={refreshMetadataObjects} title="Refresh metadata objects">
            <RefreshCcw size={16} aria-hidden="true" />
          </button>
        </div>
      </div>

      <div className="metadata-object-list">
        <button
          className={`workflow-list-item ${selectedObjectId === 'new' ? 'selected' : ''}`}
          type="button"
          onClick={openNewObject}
        >
          <Database size={16} aria-hidden="true" />
          <span>
            <strong>New object</strong>
            <small>Unsaved draft</small>
          </span>
          <em>Draft</em>
        </button>

        {metadataObjects.map(metadataObject => (
          <button
            className={`workflow-list-item ${selectedObjectId === metadataObject.id ? 'selected' : ''}`}
            key={metadataObject.id}
            type="button"
            onClick={() => openObject(metadataObject)}
          >
            <Database size={16} aria-hidden="true" />
            <span>
              <strong>{metadataObject.name}</strong>
              <small>{metadataObject.tableName}</small>
            </span>
            <em>{metadataObject.properties.length} props</em>
          </button>
        ))}
      </div>

      <div className="metadata-object-summary">
        <span>{selectedObjectId === 'new' ? 'Draft object' : 'Saved object'}</span>
        <strong>{draft.name || 'Untitled object'}</strong>
        <small>{draft.tableName || 'Table name'}</small>
      </div>

      <div className="metadata-form">
        <label className="field-control">
          <span>Object name</span>
          <input
            value={draft.name}
            onChange={event => updateObjectField('name', event.target.value)}
            placeholder="CustomRecord"
          />
        </label>

        <label className="field-control">
          <span>Table name</span>
          <input
            value={draft.tableName}
            onChange={event => updateObjectField('tableName', event.target.value)}
            placeholder="CustomRecords"
          />
        </label>

        <p className="metadata-help">The Id property is required and must stay mapped to a Guid primary key.</p>

        <div className="metadata-property-list">
          {draft.properties.map((property, index) => (
            <MetadataPropertyCard
              key={`${property.name}-${index}`}
              property={property}
              index={index}
              onChange={updater => updateProperty(index, updater)}
              onDelete={() => removeProperty(index)}
              canDelete={index > 0}
            />
          ))}
        </div>

        <button className="secondary-button" type="button" onClick={addProperty}>
          <Plus size={16} aria-hidden="true" />
          Add property
        </button>

        {validationIssues.length > 0 && (
          <div className="issue-list metadata-issues">
            {validationIssues.map((issue, index) => (
              <div className={`issue-row ${issue.severity === 'Error' ? 'issue-error' : ''}`} key={`${issue.field ?? 'metadata'}-${index}`}>
                <span>
                  <em>{issue.severity}</em>
                  <strong>{issue.field ?? 'object'}</strong>
                  <small>{issue.message}</small>
                </span>
              </div>
            ))}
          </div>
        )}

        <div className="metadata-actions">
          <button className="secondary-button" type="button" onClick={validateDraft} disabled={isSaving}>
            Validate
          </button>
          <button className="primary-button" type="button" onClick={saveDraft} disabled={isSaving}>
            <Save size={16} aria-hidden="true" />
            Save
          </button>
          <button
            className="secondary-button"
            type="button"
            onClick={deleteDraft}
            disabled={isSaving || selectedObjectId === 'new' || !draft.id}
          >
            <Trash2 size={16} aria-hidden="true" />
            Delete
          </button>
        </div>
      </div>
    </section>
  );
}

interface MetadataPropertyCardProps {
  property: PropertyMetadataUpsertRequest;
  index: number;
  canDelete: boolean;
  onChange: (updater: (property: PropertyMetadataUpsertRequest) => PropertyMetadataUpsertRequest) => void;
  onDelete: () => void;
}

function MetadataPropertyCard({ property, index, canDelete, onChange, onDelete }: MetadataPropertyCardProps) {
  const isIdProperty = index === 0;

  function updatePropertyField(field: keyof PropertyMetadataUpsertRequest, value: string | boolean | number | null) {
    onChange(current => ({
      ...current,
      [field]: value
    }));
  }

  return (
    <article className="metadata-property-card">
      <div className="metadata-property-card-header">
        <strong>{isIdProperty ? 'Id property' : property.name || `Property ${index + 1}`}</strong>
        <button className="icon-button" type="button" onClick={onDelete} disabled={!canDelete} title={canDelete ? 'Remove property' : 'The Id property cannot be removed'}>
          <Trash2 size={14} aria-hidden="true" />
        </button>
      </div>

      {isIdProperty && <p className="metadata-help">The Id property is fixed for runtime compatibility.</p>}

      <div className="metadata-property-grid">
        <label className="field-control">
          <span>Name</span>
          <input value={property.name} onChange={event => updatePropertyField('name', event.target.value)} disabled={isIdProperty} />
        </label>

        <label className="field-control">
          <span>Column name</span>
          <input value={property.columnName} onChange={event => updatePropertyField('columnName', event.target.value)} disabled={isIdProperty} />
        </label>

        <label className="field-control">
          <span>CLR type</span>
          <select value={property.clrType} onChange={event => updatePropertyField('clrType', event.target.value)} disabled={isIdProperty}>
            {supportedClrTypes.map(clrType => (
              <option key={clrType} value={clrType}>{clrType}</option>
            ))}
          </select>
        </label>

        <label className="field-control">
          <span>Max length</span>
          <input
            type="number"
            min="1"
            value={property.maxLength ?? ''}
            onChange={event => updatePropertyField('maxLength', event.target.value ? Number.parseInt(event.target.value, 10) : null)}
            disabled={isIdProperty}
          />
        </label>

        <label className="field-control">
          <span>Default value</span>
          <input
            value={property.defaultValue ?? ''}
            onChange={event => updatePropertyField('defaultValue', event.target.value)}
            placeholder="Optional"
          />
        </label>

        <label className="field-control">
          <span>Caption</span>
          <input
            value={property.caption ?? ''}
            onChange={event => updatePropertyField('caption', event.target.value)}
            placeholder="Optional"
          />
        </label>

        <label className="checkbox-field">
          <input type="checkbox" checked={property.isRequired} onChange={event => updatePropertyField('isRequired', event.target.checked)} disabled={isIdProperty} />
          <span>Required</span>
        </label>

        <label className="checkbox-field">
          <input type="checkbox" checked={property.isUnique} onChange={event => updatePropertyField('isUnique', event.target.checked)} disabled={isIdProperty} />
          <span>Unique</span>
        </label>

        <label className="checkbox-field">
          <input type="checkbox" checked={property.isPrimaryKey} onChange={event => updatePropertyField('isPrimaryKey', event.target.checked)} disabled={isIdProperty} />
          <span>Primary key</span>
        </label>
      </div>
    </article>
  );
}

function createNewMetadataDraft(): ObjectMetadataUpsertRequest {
  return {
    id: null,
    name: 'NewDomainObject',
    tableName: 'NewDomainObjects',
    properties: [createIdPropertyDraft()]
  };
}

function createIdPropertyDraft(): PropertyMetadataUpsertRequest {
  return {
    name: 'Id',
    columnName: 'Id',
    clrType: 'Guid',
    isRequired: true,
    maxLength: null,
    isUnique: true,
    isPrimaryKey: true,
    defaultValue: null,
    caption: 'Identifier'
  };
}

function createPropertyDraft(name: string): PropertyMetadataUpsertRequest {
  return {
    name,
    columnName: name,
    clrType: 'String',
    isRequired: false,
    maxLength: null,
    isUnique: false,
    isPrimaryKey: false,
    defaultValue: null,
    caption: null
  };
}

function toMetadataDraft(metadataObject: ObjectMetadata): ObjectMetadataUpsertRequest {
  return {
    id: metadataObject.id,
    name: metadataObject.name,
    tableName: metadataObject.tableName,
    properties: metadataObject.properties.map(property => ({ ...property }))
  };
}

function getValidationDetails(error: unknown): MetadataValidationResponse | null {
  if (!(error instanceof ApiError))
    return null;

  const details = error.details as Partial<MetadataValidationResponse> | undefined;
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