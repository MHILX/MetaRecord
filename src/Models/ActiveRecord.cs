using MetaRecord.Data;
using MetaRecord.Workflows.Definitions;
using MetaRecord.Workflows.Runtime;
using System.Reflection;
using System.Text.Json;

namespace MetaRecord.Models;

/// <summary>
/// Active Record base class - entity knows how to save itself.
/// Combines data access with domain logic in a single class.
/// Uses metadata-driven SQL generation for SQLite persistence.
/// </summary>
public abstract class ActiveRecord<T> where T : ActiveRecord<T>, new()
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool IsNew { get; internal set; } = true;
    public bool IsDirty { get; internal set; }

    /// <summary>
    /// Gets the metadata describing this entity's structure.
    /// </summary>
    public static IObjectMetadata Metadata =>
        MetadataRegistry.GetMetadata<T>();

    /// <summary>
    /// Persists the entity to the database (insert or update).
    /// Skips the write when the entity is unchanged (not new and not dirty).
    /// </summary>
    public void Save()
    {
        SaveAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Persists the entity to the database and dispatches configured workflow lifecycle events.
    /// </summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var runtime = WorkflowRuntime.Current;
        var store = runtime?.EntityStore ?? EntityStore.Current;
        var metadata = Metadata;

        if (!IsNew && !IsDirty)
            return;

        var isNew = IsNew;
        var originalValues = isNew
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : store.FindValues(metadata, Id);
        var currentValues = GetEntityValues((T)this, metadata);
        var changedFields = GetChangedFields(metadata, originalValues, currentValues);

        if (runtime is not null)
        {
            await RunBeforeSaveWorkflowsAsync(runtime, metadata, originalValues, currentValues, changedFields, cancellationToken);
            ApplyValuesToEntity((T)this, metadata, currentValues);
            currentValues = GetEntityValues((T)this, metadata);
            changedFields = GetChangedFields(metadata, originalValues, currentValues);
        }

        if (IsNew)
        {
            store.Insert((T)this, metadata);
            Console.WriteLine($"  [DB] Inserted {typeof(T).Name} with Id={Id}");
        }
        else if (IsDirty)
        {
            store.Update((T)this, metadata, Id);
            Console.WriteLine($"  [DB] Updated {typeof(T).Name} with Id={Id}");
        }
        else
        {
            return; // no changes, nothing to persist
        }

        IsNew = false;
        IsDirty = false;

        if (runtime is not null)
        {
            await RunAfterSaveWorkflowsAsync(runtime, metadata, isNew, originalValues, currentValues, changedFields, cancellationToken);
        }
    }

    /// <summary>
    /// Removes the entity from the database.
    /// </summary>
    public void Delete()
    {
        (WorkflowRuntime.Current?.EntityStore ?? EntityStore.Current).Delete(Metadata, Id);
        Console.WriteLine($"  [DB] Deleted {typeof(T).Name} with Id={Id}");
    }

    /// <summary>
    /// Finds an entity by its unique identifier.
    /// </summary>
    public static T? Find(Guid id)
    {
        var entity = (WorkflowRuntime.Current?.EntityStore ?? EntityStore.Current).Find<T>(Metadata, id);
        if (entity != null)
        {
            entity.IsNew = false;
            entity.IsDirty = false;
        }
        return entity;
    }

    /// <summary>
    /// Returns all entities of this type from the database.
    /// </summary>
    public static List<T> All()
    {
        var entities = (WorkflowRuntime.Current?.EntityStore ?? EntityStore.Current).All<T>(Metadata);
        foreach (var entity in entities)
        {
            entity.IsNew = false;
            entity.IsDirty = false;
        }
        return entities;
    }

    /// <summary>
    /// Counts all entities of this type.
    /// </summary>
    public static int Count() => (WorkflowRuntime.Current?.EntityStore ?? EntityStore.Current).Count(Metadata);

    /// <summary>
    /// Marks a property as modified.
    /// </summary>
    protected void MarkDirty() => IsDirty = true;

    private static async Task RunBeforeSaveWorkflowsAsync(
        WorkflowRuntimeServices runtime,
        IObjectMetadata metadata,
        Dictionary<string, object?> originalValues,
        Dictionary<string, object?> currentValues,
        IReadOnlyList<string> changedFields,
        CancellationToken cancellationToken)
    {
        var workflows = await runtime.Repository.GetEnabledDefinitionsAsync(
            metadata.Name,
            WorkflowEventName.BeforeSave,
            cancellationToken);
        var results = new List<WorkflowRunResult>();

        foreach (var workflow in workflows)
        {
            var context = CreateExecutionContext(workflow, metadata, WorkflowEventName.BeforeSave, originalValues, currentValues, changedFields);
            var result = await runtime.Engine.RunAsync(workflow, context, cancellationToken);
            results.Add(result);

            if (result.IsRejected)
                throw new WorkflowSaveRejectedException(result.ErrorMessage ?? "Save was rejected by a workflow.", results);

            if (result.Status != WorkflowRunStatus.Succeeded)
                throw new WorkflowSaveFailedException(result.ErrorMessage ?? "Before-save workflow failed.", results);
        }
    }

    private static async Task RunAfterSaveWorkflowsAsync(
        WorkflowRuntimeServices runtime,
        IObjectMetadata metadata,
        bool isNew,
        Dictionary<string, object?> originalValues,
        Dictionary<string, object?> currentValues,
        IReadOnlyList<string> changedFields,
        CancellationToken cancellationToken)
    {
        try
        {
            await RunEnabledWorkflowsAsync(runtime, metadata, isNew ? WorkflowEventName.Created : WorkflowEventName.Updated, originalValues, currentValues, changedFields, cancellationToken);

            if (!isNew && changedFields.Count > 0)
            {
                await RunEnabledWorkflowsAsync(
                    runtime,
                    metadata,
                    WorkflowEventName.FieldChanged,
                    originalValues,
                    currentValues,
                    changedFields,
                    cancellationToken,
                    workflow => IsFieldChangedWorkflowMatch(workflow, changedFields));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [WORKFLOW] After-save workflow dispatch failed: {ex.Message}");
        }
    }

    private static async Task RunEnabledWorkflowsAsync(
        WorkflowRuntimeServices runtime,
        IObjectMetadata metadata,
        string eventName,
        Dictionary<string, object?> originalValues,
        Dictionary<string, object?> currentValues,
        IReadOnlyList<string> changedFields,
        CancellationToken cancellationToken,
        Func<WorkflowDefinition, bool>? filter = null)
    {
        var workflows = await runtime.Repository.GetEnabledDefinitionsAsync(metadata.Name, eventName, cancellationToken);
        foreach (var workflow in workflows)
        {
            if (filter is not null && !filter(workflow))
                continue;

            var context = CreateExecutionContext(workflow, metadata, eventName, originalValues, currentValues, changedFields);
            await runtime.Engine.RunAsync(workflow, context, cancellationToken);
        }
    }

    private static WorkflowExecutionContext CreateExecutionContext(
        WorkflowDefinition workflow,
        IObjectMetadata metadata,
        string eventName,
        Dictionary<string, object?> originalValues,
        Dictionary<string, object?> currentValues,
        IReadOnlyList<string> changedFields) => new()
    {
        WorkflowId = workflow.Id,
        WorkflowVersion = workflow.Version,
        ObjectName = metadata.Name,
        EventName = eventName,
        RecordId = GetRecordId(currentValues),
        CurrentRecord = currentValues,
        OriginalRecord = originalValues,
        ChangedFields = changedFields
    };

    private static Dictionary<string, object?> GetEntityValues(T entity, IObjectMetadata metadata)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in metadata.Properties)
        {
            var propertyInfo = GetPropertyInfo(property.Name);
            if (propertyInfo is not null)
                values[property.Name] = propertyInfo.GetValue(entity);
        }

        return values;
    }

    private static void ApplyValuesToEntity(T entity, IObjectMetadata metadata, IReadOnlyDictionary<string, object?> values)
    {
        foreach (var property in metadata.Properties)
        {
            if (!values.TryGetValue(property.Name, out var value))
                continue;

            var propertyInfo = GetPropertyInfo(property.Name);
            if (propertyInfo is null || !propertyInfo.CanWrite)
                continue;

            propertyInfo.SetValue(entity, ConvertToPropertyType(value, propertyInfo.PropertyType));
        }
    }

    private static IReadOnlyList<string> GetChangedFields(
        IObjectMetadata metadata,
        IReadOnlyDictionary<string, object?> originalValues,
        IReadOnlyDictionary<string, object?> currentValues)
    {
        var changedFields = new List<string>();

        foreach (var property in metadata.Properties)
        {
            if (property.IsPrimaryKey || string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                continue;

            currentValues.TryGetValue(property.Name, out var currentValue);
            originalValues.TryGetValue(property.Name, out var originalValue);

            if (!object.Equals(originalValue, currentValue))
                changedFields.Add(property.Name);
        }

        return changedFields;
    }

    private static bool IsFieldChangedWorkflowMatch(WorkflowDefinition workflow, IReadOnlyList<string> changedFields)
    {
        var trigger = workflow.Nodes.FirstOrDefault(node => string.Equals(node.Type, "trigger.field-changed", StringComparison.OrdinalIgnoreCase));
        if (trigger is null || trigger.Config.ValueKind != JsonValueKind.Object)
            return false;

        if (!trigger.Config.TryGetProperty("fieldName", out var fieldValue) || fieldValue.ValueKind != JsonValueKind.String)
            return false;

        var fieldName = fieldValue.GetString();
        return !string.IsNullOrWhiteSpace(fieldName) &&
            changedFields.Any(changedField => string.Equals(changedField, fieldName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetRecordId(IReadOnlyDictionary<string, object?> currentValues) =>
        currentValues.TryGetValue("Id", out var id) ? id?.ToString() : null;

    private static PropertyInfo? GetPropertyInfo(string propertyName) =>
        typeof(T).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

    private static object? ConvertToPropertyType(object? value, Type propertyType)
    {
        if (value is null)
            return null;

        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (targetType.IsInstanceOfType(value))
            return value;
        if (targetType == typeof(Guid))
            return value is Guid guidValue ? guidValue : Guid.Parse(value.ToString()!);
        if (targetType.IsEnum)
            return Enum.Parse(targetType, value.ToString()!, ignoreCase: true);

        return Convert.ChangeType(value, targetType);
    }
}