import { Database, FilePlus2, Plus, RefreshCcw, Save, Trash2, X } from 'lucide-react';
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
  selectedObjectId: MetadataSelectionId;
  onSelectedObjectIdChange: (selectedObjectId: MetadataSelectionId) => void;
  onMetadataObjectsChange: (metadataObjects: ObjectMetadata[]) => void;
  onRefreshMetadata: () => Promise<void>;
  onNotice: (message: string, kind?: 'info' | 'error') => void;
  showDetails?: boolean;
  showObjectList?: boolean;
  showCreateButton?: boolean;
  onOpenEditor?: () => void;
}

export type MetadataSelectionId = string | 'new' | null;

const supportedClrTypes = ['Guid', 'String', 'Int32', 'Int64', 'Decimal', 'Double', 'Boolean', 'DateTime'] as const;

export function MetadataManager({
  metadataObjects,
  isLoading,
  selectedObjectId,
  onSelectedObjectIdChange,
  onMetadataObjectsChange,
  onRefreshMetadata,
  onNotice,
  showDetails = true,
  showObjectList = true,
  showCreateButton = true,
  onOpenEditor
}: MetadataManagerProps) {
  const [draft, setDraft] = useState<ObjectMetadataUpsertRequest>(createNewMetadataDraft);
  const [validationIssues, setValidationIssues] = useState<MetadataValidationIssue[]>([]);
  const [isSaving, setIsSaving] = useState(false);
  const [selectedObjectSnapshot, setSelectedObjectSnapshot] = useState<ObjectMetadata | null>(null);
  const isCompact = showDetails === false;
  const shouldShowObjectList = showObjectList !== false;
  const shouldShowCreateButton = showCreateButton !== false;

  useEffect(() => {
    if (isLoading)
      return;

    if (metadataObjects.length === 0) {
      if (selectedObjectId !== 'new')
        onSelectedObjectIdChange('new');
      else {
        setDraft(createNewMetadataDraft());
        setValidationIssues([]);
      }
      return;
    }

    if (selectedObjectId === null) {
      onSelectedObjectIdChange(metadataObjects[0].id);
      return;
    }

    if (selectedObjectId === 'new') {
      setDraft(createNewMetadataDraft());
      setValidationIssues([]);
      setSelectedObjectSnapshot(null);
      return;
    }

    const selectedObject = metadataObjects.find(metadataObject => metadataObject.id === selectedObjectId);
    if (selectedObject) {
      setDraft(toMetadataDraft(selectedObject));
      setValidationIssues([]);
      setSelectedObjectSnapshot(selectedObject);
      return;
    }

    onSelectedObjectIdChange(metadataObjects[0].id);
  }, [isLoading, metadataObjects, onSelectedObjectIdChange, selectedObjectId]);

  function openObject(metadataObject: ObjectMetadata) {
    onSelectedObjectIdChange(metadataObject.id);
    setDraft(toMetadataDraft(metadataObject));
    setValidationIssues([]);
    setSelectedObjectSnapshot(metadataObject);
    if (isCompact)
      onOpenEditor?.();
  }

  function openNewObject() {
    onSelectedObjectIdChange('new');
    setDraft(createNewMetadataDraft());
    setValidationIssues([]);
    setSelectedObjectSnapshot(null);
    if (isCompact)
      onOpenEditor?.();
  }

  function discardDraft() {
    if (selectedObjectId === 'new' || metadataObjects.length === 0) {
      openNewObject();
      return;
    }

    const selectedObject = metadataObjects.find(metadataObject => metadataObject.id === selectedObjectId);
    if (selectedObject) {
      setDraft(toMetadataDraft(selectedObject));
      setValidationIssues([]);
      setSelectedObjectSnapshot(selectedObject);
      return;
    }

    openObject(metadataObjects[0]);
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
      onSelectedObjectIdChange(savedObject.id);
      setDraft(toMetadataDraft(savedObject));
      setValidationIssues([]);
      setSelectedObjectSnapshot(savedObject);
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
    <section className={showObjectList ? 'panel metadata-panel' : 'panel metadata-panel metadata-panel-details'}>
      {shouldShowObjectList && (
        <div className="panel-heading">
          <h2>Metadata</h2>
          <div className="metadata-heading-actions">
            {shouldShowCreateButton && (
              <button className="secondary-button" type="button" onClick={openNewObject}>
                <FilePlus2 size={16} aria-hidden="true" />
                New
              </button>
            )}
            <button className="icon-button" type="button" onClick={refreshMetadataObjects} title="Refresh metadata objects">
              <RefreshCcw size={16} aria-hidden="true" />
            </button>
          </div>
        </div>
      )}

      {shouldShowObjectList && (
        <div className="metadata-object-list">
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
      )}

      {isCompact ? null : (
        <>
          {!shouldShowObjectList && (
            <div className="panel-heading">
              <h2>Metadata object</h2>
              <div className="metadata-heading-actions">
                {shouldShowCreateButton && (
                  <button className="secondary-button" type="button" onClick={openNewObject}>
                    <FilePlus2 size={16} aria-hidden="true" />
                    New
                  </button>
                )}
              </div>
            </div>
          )}

          <div className="metadata-object-summary">
            <span>{selectedObjectId === 'new' ? 'Draft object' : 'Saved object'}</span>
            <strong>{draft.name || 'Untitled object'}</strong>
            <small>{draft.tableName || 'Table name'}</small>
          </div>

          <div className="metadata-form">
            <div className="metadata-details-grid">
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
            </div>

            <p className="metadata-help">The Id property is required and must stay mapped to a Guid primary key.</p>

            <div className="metadata-property-list">
              {draft.properties.map((property, index) => (
                <MetadataPropertyCard
                  key={index}
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

            {selectedObjectSnapshot && selectedObjectId !== 'new' && draft.id === selectedObjectSnapshot.id && (
              <MetadataChangeWarnings original={selectedObjectSnapshot} draft={draft} />
            )}

            <div className="metadata-actions">
              <button className="secondary-button" type="button" onClick={discardDraft} disabled={isSaving}>
                <X size={16} aria-hidden="true" />
                Discard
              </button>
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
        </>
      )}
    </section>
  );
}

function MetadataChangeWarnings({
  original,
  draft
}: {
  original: ObjectMetadata;
  draft: ObjectMetadataUpsertRequest;
}) {
  const warnings = getMetadataChangeWarnings(original, draft);

  if (warnings.length === 0)
    return null;

  return (
    <div className="metadata-warning-panel">
      <div className="panel-heading">
        <h3>Potential breaking changes</h3>
        <span className="status-pill warning">Review before saving</span>
      </div>
      <div className="issue-list metadata-issues">
        {warnings.map((warning, index) => (
          <div className="issue-row issue-warning" key={`${warning.field ?? 'warning'}-${index}`}>
            <span>
              <em>Warning</em>
              <strong>{warning.title}</strong>
              <small>{warning.message}</small>
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}

type MetadataWarning = {
  title: string;
  message: string;
  field?: string;
};

function getMetadataChangeWarnings(original: ObjectMetadata, draft: ObjectMetadataUpsertRequest): MetadataWarning[] {
  const warnings: MetadataWarning[] = [];

  if (original.name !== draft.name) {
    warnings.push({
      title: 'Object name changed',
      message: 'Renaming the object can break workflows, references, and any code that looks it up by name.',
      field: 'name'
    });
  }

  if (original.tableName !== draft.tableName) {
    warnings.push({
      title: 'Table name changed',
      message: 'Renaming the table can break the existing physical database table unless a migration is applied.',
      field: 'tableName'
    });
  }

  const originalIdProperty = original.properties.find(property => property.isPrimaryKey || property.name === 'Id');
  const draftIdProperty = draft.properties.find(property => property.isPrimaryKey || property.name === 'Id');

  if (!draftIdProperty) {
    warnings.push({
      title: 'Id property removed',
      message: 'Removing the Id property can break record reads, updates, and identity handling.',
      field: 'properties'
    });
  } else {
    if (draftIdProperty.columnName !== 'Id') {
      warnings.push({
        title: 'Id column changed',
        message: 'Changing the Id column can break the primary key mapping used by the runtime.',
        field: 'properties.Id.columnName'
      });
    }

    if (draftIdProperty.clrType !== 'Guid') {
      warnings.push({
        title: 'Id type changed',
        message: 'The runtime expects the Id property to stay a Guid primary key.',
        field: 'properties.Id.clrType'
      });
    }

    if (originalIdProperty && originalIdProperty.name !== draftIdProperty.name) {
      warnings.push({
        title: 'Primary key identity changed',
        message: 'Changing the identity of the primary key can break existing data mappings.',
        field: 'properties.Id'
      });
    }
  }

  const originalPropertiesByName = new Map(original.properties.map(property => [property.name.toLowerCase(), property] as const));
  const draftPropertiesByName = new Map(draft.properties.map(property => [property.name.toLowerCase(), property] as const));

  for (const originalProperty of original.properties) {
    const draftProperty = draftPropertiesByName.get(originalProperty.name.toLowerCase());
    if (!draftProperty) {
      warnings.push({
        title: `Property removed: ${originalProperty.name}`,
        message: 'Deleting a property can break existing records, saved forms, and workflow expressions that expect the field.',
        field: `properties.${originalProperty.name}`
      });
      continue;
    }

    if (originalProperty.columnName !== draftProperty.columnName) {
      warnings.push({
        title: `Column renamed: ${originalProperty.name}`,
        message: 'Changing a column name can break the existing database schema without a migration.',
        field: `properties.${originalProperty.name}.columnName`
      });
    }

    if (originalProperty.clrType !== draftProperty.clrType) {
      warnings.push({
        title: `Type changed: ${originalProperty.name}`,
        message: 'Changing the CLR type can make existing stored values fail to read or save correctly.',
        field: `properties.${originalProperty.name}.clrType`
      });
    }

    if (originalProperty.isPrimaryKey !== draftProperty.isPrimaryKey) {
      warnings.push({
        title: `Primary key changed: ${originalProperty.name}`,
        message: 'Changing which field acts as the primary key can invalidate existing records.',
        field: `properties.${originalProperty.name}.isPrimaryKey`
      });
    }

    if (originalProperty.isRequired !== draftProperty.isRequired) {
      warnings.push({
        title: `Required flag changed: ${originalProperty.name}`,
        message: 'Tightening required fields can make existing rows or future saves invalid.',
        field: `properties.${originalProperty.name}.isRequired`
      });
    }

    if (originalProperty.isUnique !== draftProperty.isUnique) {
      warnings.push({
        title: `Uniqueness changed: ${originalProperty.name}`,
        message: 'Adding uniqueness can fail if duplicate values already exist in stored records.',
        field: `properties.${originalProperty.name}.isUnique`
      });
    }
  }

  for (const draftProperty of draft.properties) {
    if (originalPropertiesByName.has(draftProperty.name.toLowerCase()))
      continue;

    if (draftProperty.name === 'Id')
      continue;

    if (draftProperty.isPrimaryKey) {
      warnings.push({
        title: `New primary key added: ${draftProperty.name}`,
        message: 'Adding another primary key can conflict with the existing Id-based runtime assumptions.',
        field: `properties.${draftProperty.name}.isPrimaryKey`
      });
    }
  }

  return warnings;
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