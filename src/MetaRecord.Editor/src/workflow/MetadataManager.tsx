import { Database, FilePlus2, Plus, RefreshCcw, Save, Trash2, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import { ApiError, workflowApi } from '../api/client';
import type {
  MetadataValidationIssue,
  MetadataValidationResponse,
  ObjectMetadata,
  ObjectMetadataUpsertRequest,
  PropertyMetadataUpsertRequest,
  RelationshipCardinality,
  RelationshipDeleteBehavior,
  RelationshipMetadataUpsertRequest
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
const supportedRelationshipCardinalities: Array<{ value: RelationshipCardinality; label: string }> = [
  { value: 'ManyToOne', label: 'Many to one' },
  { value: 'OneToOne', label: 'One to one' },
  { value: 'OneToMany', label: 'One to many' },
  { value: 'ManyToMany', label: 'Many to many' }
];

const supportedRelationshipDeleteBehaviors: Array<{ value: RelationshipDeleteBehavior; label: string }> = [
  { value: 'Restrict', label: 'Restrict' },
  { value: 'SetNull', label: 'Set null' },
  { value: 'Cascade', label: 'Cascade' },
  { value: 'NoAction', label: 'No action' }
];

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

  function addRelationship() {
    updateDraft(current => {
      const relationship = createRelationshipDraft(current, metadataObjects);
      if (!relationship)
        return current;

      return {
        ...current,
        relationships: [...current.relationships, relationship]
      };
    });
  }

  function updateRelationship(index: number, updater: (relationship: RelationshipMetadataUpsertRequest) => RelationshipMetadataUpsertRequest) {
    updateDraft(current => ({
      ...current,
      relationships: current.relationships.map((relationship, relationshipIndex) => relationshipIndex === index ? updater(relationship) : relationship)
    }));
  }

  function removeRelationship(index: number) {
    updateDraft(current => ({
      ...current,
      relationships: current.relationships.filter((_, relationshipIndex) => relationshipIndex !== index)
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

  async function validateDraft(currentDraft: ObjectMetadataUpsertRequest = draft): Promise<MetadataValidationResponse | null> {
    try {
      const validation = await workflowApi.validateObject(normalizeMetadataDraft(currentDraft, metadataObjects));
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
      const normalizedDraft = normalizeMetadataDraft(draft, metadataObjects);
      setDraft(normalizedDraft);

      const validation = await validateDraft(normalizedDraft);
      if (!validation)
        return;

      if (!validation.isValid) {
        onNotice('Validation found issues.', 'error');
        return;
      }

      const savedObject = normalizedDraft.id
        ? await workflowApi.updateObject(normalizedDraft.id, normalizedDraft)
        : await workflowApi.createObject(normalizedDraft);

      const nextObjects = normalizedDraft.id
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
              <em>{metadataObject.properties.length} props, {metadataObject.relationships.length} rels</em>
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

            <div className="metadata-section-header">
              <div>
                <h3>Relationships</h3>
                <p className="metadata-help">Lookup relationships store a Guid on this object and point it at a target object.</p>
              </div>
              <button className="secondary-button" type="button" onClick={addRelationship} disabled={metadataObjects.length === 0}>
                <Plus size={16} aria-hidden="true" />
                Add relationship
              </button>
            </div>

            {metadataObjects.length === 0 && (
              <p className="metadata-help">Create or load at least one object before adding relationships.</p>
            )}

            {draft.relationships.length === 0 ? (
              <p className="metadata-help">No relationships defined yet.</p>
            ) : (
              <div className="metadata-relationship-list">
                {draft.relationships.map((relationship, index) => (
                  <MetadataRelationshipCard
                    key={index}
                    relationship={relationship}
                    index={index}
                    metadataObjects={metadataObjects}
                    sourceProperties={draft.properties}
                    onChange={updater => updateRelationship(index, updater)}
                    onDelete={() => removeRelationship(index)}
                  />
                ))}
              </div>
            )}

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
              <button className="secondary-button" type="button" onClick={() => validateDraft()} disabled={isSaving}>
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

  const originalRelationshipsByName = new Map(original.relationships.map(relationship => [relationship.name.toLowerCase(), relationship] as const));
  const draftRelationshipsByName = new Map(draft.relationships.map(relationship => [relationship.name.toLowerCase(), relationship] as const));

  for (const originalRelationship of original.relationships) {
    const draftRelationship = draftRelationshipsByName.get(originalRelationship.name.toLowerCase());
    if (!draftRelationship) {
      warnings.push({
        title: `Relationship removed: ${originalRelationship.name}`,
        message: 'Removing a relationship can break lookup forms, display fields, and workflows that expect the link to exist.',
        field: `relationships.${originalRelationship.name}`
      });
      continue;
    }

    if (originalRelationship.sourcePropertyName !== draftRelationship.sourcePropertyName) {
      warnings.push({
        title: `Source field changed: ${originalRelationship.name}`,
        message: 'Changing the source field can break the lookup field that stores the related record id.',
        field: `relationships.${originalRelationship.name}.sourcePropertyName`
      });
    }

    if (originalRelationship.targetObjectId !== draftRelationship.targetObjectId) {
      warnings.push({
        title: `Target object changed: ${originalRelationship.name}`,
        message: 'Changing the target object can alter the meaning of existing relationship values.',
        field: `relationships.${originalRelationship.name}.targetObjectId`
      });
    }

    if (originalRelationship.targetPropertyName !== draftRelationship.targetPropertyName) {
      warnings.push({
        title: `Target key changed: ${originalRelationship.name}`,
        message: 'Changing the target key can make existing lookup values resolve differently.',
        field: `relationships.${originalRelationship.name}.targetPropertyName`
      });
    }

    if (originalRelationship.cardinality !== draftRelationship.cardinality) {
      warnings.push({
        title: `Cardinality changed: ${originalRelationship.name}`,
        message: 'Changing relationship cardinality can affect how the runtime interprets the link.',
        field: `relationships.${originalRelationship.name}.cardinality`
      });
    }

    if (originalRelationship.deleteBehavior !== draftRelationship.deleteBehavior) {
      warnings.push({
        title: `Delete behavior changed: ${originalRelationship.name}`,
        message: 'Changing delete behavior can alter how related records are handled when the target is removed.',
        field: `relationships.${originalRelationship.name}.deleteBehavior`
      });
    }

    if (originalRelationship.displayPropertyName !== draftRelationship.displayPropertyName) {
      warnings.push({
        title: `Display field changed: ${originalRelationship.name}`,
        message: 'Changing the display property can alter how the relationship appears in the editor and runtime.',
        field: `relationships.${originalRelationship.name}.displayPropertyName`
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

interface MetadataRelationshipCardProps {
  relationship: RelationshipMetadataUpsertRequest;
  index: number;
  metadataObjects: ObjectMetadata[];
  sourceProperties: PropertyMetadataUpsertRequest[];
  onChange: (updater: (relationship: RelationshipMetadataUpsertRequest) => RelationshipMetadataUpsertRequest) => void;
  onDelete: () => void;
}

function MetadataRelationshipCard({ relationship, index, metadataObjects, sourceProperties, onChange, onDelete }: MetadataRelationshipCardProps) {
  const sourcePropertyOptions = buildGuidPropertyOptions(sourceProperties, relationship.sourcePropertyName);
  const targetObjectOptions = buildObjectOptions(metadataObjects, relationship.targetObjectId, relationship.targetObjectName);
  const selectedTargetObject = metadataObjects.find(metadataObject => metadataObject.id === relationship.targetObjectId) ?? null;
  const targetPropertyOptions = buildGuidPropertyOptions(selectedTargetObject?.properties ?? [], relationship.targetPropertyName);

  function updateRelationshipField(field: keyof RelationshipMetadataUpsertRequest, value: string | null) {
    onChange(current => ({
      ...current,
      [field]: value
    }));
  }

  function updateTargetObject(targetObjectId: string) {
    const selectedObject = metadataObjects.find(metadataObject => metadataObject.id === targetObjectId) ?? null;
    const nextTargetPropertyName = getPreferredGuidPropertyName(selectedObject?.properties ?? []) ?? 'Id';

    onChange(current => ({
      ...current,
      targetObjectId,
      targetObjectName: selectedObject?.name ?? null,
      targetPropertyName: nextTargetPropertyName
    }));
  }

  return (
    <article className="metadata-relationship-card">
      <div className="metadata-property-card-header">
        <strong>{relationship.name || `Relationship ${index + 1}`}</strong>
        <button className="icon-button" type="button" onClick={onDelete} title="Remove relationship">
          <Trash2 size={14} aria-hidden="true" />
        </button>
      </div>

      <p className="metadata-help">
        {relationship.sourcePropertyName || 'Source property'} → {relationship.targetObjectName || 'Target object'}.{relationship.targetPropertyName || 'Id'}
      </p>

      <div className="metadata-relationship-grid">
        <label className="field-control">
          <span>Name</span>
          <input
            value={relationship.name}
            onChange={event => updateRelationshipField('name', event.target.value)}
            placeholder="TodoOwner"
          />
        </label>

        <label className="field-control">
          <span>Source property</span>
          <select
            value={relationship.sourcePropertyName}
            onChange={event => updateRelationshipField('sourcePropertyName', event.target.value)}
          >
            {sourcePropertyOptions.map(option => (
              <option key={option.value} value={option.value}>{option.label}</option>
            ))}
          </select>
        </label>

        <label className="field-control">
          <span>Target object</span>
          <select
            value={relationship.targetObjectId}
            onChange={event => updateTargetObject(event.target.value)}
          >
            {targetObjectOptions.map(option => (
              <option key={option.value} value={option.value}>{option.label}</option>
            ))}
          </select>
        </label>

        <label className="field-control">
          <span>Target property</span>
          <select
            value={relationship.targetPropertyName ?? 'Id'}
            onChange={event => updateRelationshipField('targetPropertyName', event.target.value)}
          >
            {targetPropertyOptions.map(option => (
              <option key={option.value} value={option.value}>{option.label}</option>
            ))}
          </select>
        </label>

        <label className="field-control">
          <span>Cardinality</span>
          <select
            value={relationship.cardinality}
            onChange={event => updateRelationshipField('cardinality', event.target.value as RelationshipCardinality)}
          >
            {supportedRelationshipCardinalities.map(option => (
              <option key={option.value} value={option.value}>{option.label}</option>
            ))}
          </select>
        </label>

        <label className="field-control">
          <span>Delete behavior</span>
          <select
            value={relationship.deleteBehavior}
            onChange={event => updateRelationshipField('deleteBehavior', event.target.value as RelationshipDeleteBehavior)}
          >
            {supportedRelationshipDeleteBehaviors.map(option => (
              <option key={option.value} value={option.value}>{option.label}</option>
            ))}
          </select>
        </label>

        <label className="field-control field-control-full">
          <span>Display property name</span>
          <input
            value={relationship.displayPropertyName ?? ''}
            onChange={event => updateRelationshipField('displayPropertyName', toNullableText(event.target.value))}
            placeholder="Optional display field on the target object"
          />
        </label>

        <label className="field-control">
          <span>Caption</span>
          <input
            value={relationship.caption ?? ''}
            onChange={event => updateRelationshipField('caption', toNullableText(event.target.value))}
            placeholder="Optional"
          />
        </label>

        <label className="field-control field-control-full">
          <span>Description</span>
          <textarea
            value={relationship.description ?? ''}
            onChange={event => updateRelationshipField('description', toNullableText(event.target.value))}
            placeholder="Optional"
          />
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
    properties: [createIdPropertyDraft()],
    relationships: []
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

type RelationshipSelectOption = {
  value: string;
  label: string;
};

function buildObjectOptions(metadataObjects: ObjectMetadata[], currentValue?: string | null, currentLabel?: string | null): RelationshipSelectOption[] {
  const options = metadataObjects.map(metadataObject => ({
    value: metadataObject.id,
    label: metadataObject.name
  }));

  if (currentValue && !options.some(option => option.value === currentValue)) {
    options.unshift({
      value: currentValue,
      label: currentLabel ? `${currentLabel} (missing)` : 'Missing target object'
    });
  }

  return options;
}

function buildGuidPropertyOptions(properties: PropertyMetadataUpsertRequest[], currentValue?: string | null): RelationshipSelectOption[] {
  const options = properties
    .filter(property => property.clrType === 'Guid')
    .map(property => ({
      value: property.name,
      label: property.caption ? `${property.name} - ${property.caption}` : property.name
    }));

  if (currentValue && !options.some(option => option.value === currentValue)) {
    options.unshift({
      value: currentValue,
      label: `${currentValue} (missing)`
    });
  }

  if (options.length === 0) {
    options.push({
      value: 'Id',
      label: 'Id'
    });
  }

  return options;
}

function getPreferredGuidPropertyName(properties: PropertyMetadataUpsertRequest[]) {
  return properties.find(property => property.clrType === 'Guid' && property.isPrimaryKey)?.name
    ?? properties.find(property => property.clrType === 'Guid')?.name
    ?? null;
}

function getUniqueRelationshipName(existingRelationships: Array<{ name: string }>) {
  const existingNames = new Set(existingRelationships.map(relationship => relationship.name.toLowerCase()));
  let suffix = existingRelationships.length + 1;
  let candidate = `Relationship${suffix}`;

  while (existingNames.has(candidate.toLowerCase())) {
    suffix += 1;
    candidate = `Relationship${suffix}`;
  }

  return candidate;
}

function createRelationshipDraft(currentDraft: ObjectMetadataUpsertRequest, metadataObjects: ObjectMetadata[]): RelationshipMetadataUpsertRequest | null {
  if (metadataObjects.length === 0)
    return null;

  const targetObject = currentDraft.id
    ? metadataObjects.find(metadataObject => metadataObject.id !== currentDraft.id) ?? metadataObjects[0]
    : metadataObjects[0];

  const sourcePropertyName = getPreferredGuidPropertyName(currentDraft.properties) ?? currentDraft.properties[0]?.name ?? 'Id';
  const targetPropertyName = getPreferredGuidPropertyName(targetObject.properties) ?? 'Id';

  return {
    name: getUniqueRelationshipName(currentDraft.relationships),
    sourcePropertyName,
    targetObjectId: targetObject.id,
    targetObjectName: targetObject.name,
    targetPropertyName,
    cardinality: 'ManyToOne',
    deleteBehavior: 'Restrict',
    displayPropertyName: null,
    caption: null,
    description: null
  };
}

function normalizeMetadataDraft(metadataDraft: ObjectMetadataUpsertRequest, metadataObjects: ObjectMetadata[]): ObjectMetadataUpsertRequest {
  const targetObjectsById = new Map(metadataObjects.map(metadataObject => [metadataObject.id, metadataObject] as const));

  return {
    ...metadataDraft,
    properties: metadataDraft.properties.map(property => ({ ...property })),
    relationships: metadataDraft.relationships.map(relationship => {
      const targetObject = targetObjectsById.get(relationship.targetObjectId);

      return {
        ...relationship,
        targetObjectName: targetObject?.name ?? relationship.targetObjectName ?? null,
        targetPropertyName: relationship.targetPropertyName || getPreferredGuidPropertyName(targetObject?.properties ?? []) || 'Id'
      };
    })
  };
}

function toNullableText(value: string) {
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}

function toMetadataDraft(metadataObject: ObjectMetadata): ObjectMetadataUpsertRequest {
  return {
    id: metadataObject.id,
    name: metadataObject.name,
    tableName: metadataObject.tableName,
    properties: metadataObject.properties.map(property => ({ ...property })),
    relationships: metadataObject.relationships.map(relationship => ({ ...relationship }))
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