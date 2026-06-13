using System.Text.Json;
using MetaRecord.Data;
using MetaRecord.Workflows.Definitions;
using Microsoft.EntityFrameworkCore;

namespace MetaRecord.Workflows.Persistence;

public sealed class WorkflowRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly MetaRecordDbContext _context;

    public WorkflowRepository(MetaRecordDbContext context)
    {
        _context = context;
    }

    public async Task SaveDefinitionAsync(
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var existing = await _context.WorkflowDefinitions
            .FirstOrDefaultAsync(entity => entity.Id == workflow.Id, cancellationToken);

        if (existing is null)
        {
            _context.WorkflowDefinitions.Add(ToEntity(workflow));
        }
        else
        {
            existing.Name = workflow.Name;
            existing.ObjectName = workflow.ObjectName;
            existing.EventName = workflow.EventName;
            existing.IsEnabled = workflow.IsEnabled;
            existing.DefinitionJson = Serialize(workflow);
            existing.Version = workflow.Version;
            existing.DateModified = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorkflowDefinition?> GetDefinitionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(workflow => workflow.Id == id, cancellationToken);

        return entity is null ? null : ToDefinition(entity);
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> ListDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        var entities = await _context.WorkflowDefinitions
            .AsNoTracking()
            .OrderBy(workflow => workflow.Name)
            .ToListAsync(cancellationToken);

        return entities.Select(ToDefinition).ToList();
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> GetEnabledDefinitionsAsync(
        string objectName,
        string eventName,
        CancellationToken cancellationToken = default)
    {
        var entities = await _context.WorkflowDefinitions
            .AsNoTracking()
            .Where(workflow =>
                workflow.IsEnabled &&
                workflow.ObjectName == objectName &&
                workflow.EventName == eventName)
            .OrderBy(workflow => workflow.Name)
            .ToListAsync(cancellationToken);

        return entities.Select(ToDefinition).ToList();
    }

    public Task<bool> EnableAsync(Guid id, CancellationToken cancellationToken = default) =>
        SetEnabledAsync(id, true, cancellationToken);

    public Task<bool> DisableAsync(Guid id, CancellationToken cancellationToken = default) =>
        SetEnabledAsync(id, false, cancellationToken);

    public async Task<bool> DeleteDefinitionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var workflow = await _context.WorkflowDefinitions
            .FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);

        if (workflow is null)
            return false;

        var workflowRuns = await _context.WorkflowRuns
            .Where(run => run.WorkflowId == id)
            .ToListAsync(cancellationToken);

        if (workflowRuns.Count > 0)
            _context.WorkflowRuns.RemoveRange(workflowRuns);

        _context.WorkflowDefinitions.Remove(workflow);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetEnabledAsync(
        Guid id,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.WorkflowDefinitions
            .FirstOrDefaultAsync(workflow => workflow.Id == id, cancellationToken);

        if (entity is null)
            return false;

        entity.IsEnabled = isEnabled;
        entity.DateModified = DateTime.UtcNow;
        entity.DefinitionJson = Serialize(ToDefinition(entity));
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task SaveRunAsync(
        WorkflowRunEntity run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        _context.WorkflowRuns.Add(run);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorkflowRunEntity?> GetRunAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowRuns
            .AsNoTracking()
            .Include(run => run.Steps)
            .FirstOrDefaultAsync(run => run.Id == id, cancellationToken);
    }

    private static WorkflowDefinitionEntity ToEntity(WorkflowDefinition workflow)
    {
        var now = DateTime.UtcNow;
        return new WorkflowDefinitionEntity
        {
            Id = workflow.Id,
            Name = workflow.Name,
            ObjectName = workflow.ObjectName,
            EventName = workflow.EventName,
            IsEnabled = workflow.IsEnabled,
            DefinitionJson = Serialize(workflow),
            Version = workflow.Version,
            DateCreated = now,
            DateModified = now
        };
    }

    private static WorkflowDefinition ToDefinition(WorkflowDefinitionEntity entity)
    {
        var definition = JsonSerializer.Deserialize<WorkflowDefinition>(entity.DefinitionJson, JsonOptions)
            ?? throw new InvalidOperationException($"Workflow definition '{entity.Id}' could not be deserialized.");

        return new WorkflowDefinition
        {
            Id = entity.Id,
            Name = entity.Name,
            ObjectName = entity.ObjectName,
            EventName = entity.EventName,
            IsEnabled = entity.IsEnabled,
            Version = entity.Version,
            Nodes = definition.Nodes,
            Edges = definition.Edges
        };
    }

    private static string Serialize(WorkflowDefinition workflow) =>
        JsonSerializer.Serialize(workflow, JsonOptions);
}