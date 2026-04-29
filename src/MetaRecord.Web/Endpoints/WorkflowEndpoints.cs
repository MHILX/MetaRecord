using MetaRecord.Models;
using MetaRecord.Web.Contracts;
using MetaRecord.Web.Infrastructure;
using MetaRecord.Workflows.Catalog;
using MetaRecord.Workflows.Definitions;
using MetaRecord.Workflows.Persistence;
using MetaRecord.Workflows.Runtime;
using MetaRecord.Workflows.Validation;

namespace MetaRecord.Web.Endpoints;

public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/workflow-node-types", (WorkflowNodeCatalog catalog) => Results.Ok(catalog.All));

        var group = app.MapGroup("/api/workflows");

        group.MapGet("/", async (WorkflowRepository repository, CancellationToken cancellationToken) =>
            Results.Ok(await repository.ListDefinitionsAsync(cancellationToken)));

        group.MapPost("/", async (
            WorkflowDefinition workflow,
            WorkflowValidator validator,
            WorkflowRepository repository,
            CancellationToken cancellationToken) =>
        {
            var validation = ValidateWorkflow(workflow, validator);
            if (!validation.IsValid)
                return Results.BadRequest(validation);

            await repository.SaveDefinitionAsync(workflow, cancellationToken);
            return Results.Created($"/api/workflows/{workflow.Id}", workflow);
        });

        group.MapGet("/{id:guid}", async (Guid id, WorkflowRepository repository, CancellationToken cancellationToken) =>
        {
            var workflow = await repository.GetDefinitionAsync(id, cancellationToken);
            return workflow is null ? Results.NotFound() : Results.Ok(workflow);
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            WorkflowDefinition request,
            WorkflowValidator validator,
            WorkflowRepository repository,
            CancellationToken cancellationToken) =>
        {
            var workflow = WithId(request, id);
            var validation = ValidateWorkflow(workflow, validator);
            if (!validation.IsValid)
                return Results.BadRequest(validation);

            await repository.SaveDefinitionAsync(workflow, cancellationToken);
            return Results.Ok(workflow);
        });

        group.MapPost("/{id:guid}/validate", async (
            Guid id,
            WorkflowValidator validator,
            WorkflowRepository repository,
            CancellationToken cancellationToken) =>
        {
            var workflow = await repository.GetDefinitionAsync(id, cancellationToken);
            return workflow is null ? Results.NotFound() : Results.Ok(ValidateWorkflow(workflow, validator));
        });

        group.MapPost("/{id:guid}/enable", async (
            Guid id,
            WorkflowValidator validator,
            WorkflowRepository repository,
            CancellationToken cancellationToken) =>
        {
            var workflow = await repository.GetDefinitionAsync(id, cancellationToken);
            if (workflow is null)
                return Results.NotFound();

            var validation = ValidateWorkflow(workflow, validator);
            if (!validation.IsValid)
                return Results.BadRequest(validation);

            var enabledWorkflow = WithEnabled(workflow, true);
            await repository.SaveDefinitionAsync(enabledWorkflow, cancellationToken);
            return Results.Ok(enabledWorkflow);
        });

        group.MapPost("/{id:guid}/disable", async (
            Guid id,
            WorkflowRepository repository,
            CancellationToken cancellationToken) =>
        {
            var workflow = await repository.GetDefinitionAsync(id, cancellationToken);
            if (workflow is null)
                return Results.NotFound();

            var disabledWorkflow = WithEnabled(workflow, false);
            await repository.SaveDefinitionAsync(disabledWorkflow, cancellationToken);
            return Results.Ok(disabledWorkflow);
        });

        group.MapPost("/{id:guid}/test-run", async (
            Guid id,
            HttpRequest request,
            WorkflowRepository repository,
            IWorkflowEngine engine,
            CancellationToken cancellationToken) =>
        {
            var workflow = await repository.GetDefinitionAsync(id, cancellationToken);
            if (workflow is null)
                return Results.NotFound();

            var runRequest = await ReadTestRunRequestAsync(request, cancellationToken);
            var workflowEvent = CreateWorkflowEvent(workflow, runRequest);
            var result = await engine.RunAsync(workflow, workflowEvent, cancellationToken);
            return Results.Ok(ApiMappings.ToResponse(result));
        });

        return app;
    }

    private static WorkflowValidationResponse ValidateWorkflow(WorkflowDefinition workflow, WorkflowValidator validator)
    {
        var issues = validator.Validate(workflow);
        var isValid = issues.All(issue => issue.Severity != WorkflowValidationSeverity.Error);
        return new WorkflowValidationResponse(isValid, issues);
    }

    private static async Task<WorkflowTestRunRequest?> ReadTestRunRequestAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.Body.CanRead || request.ContentLength.GetValueOrDefault() == 0)
            return null;

        return await request.ReadFromJsonAsync<WorkflowTestRunRequest>(cancellationToken: cancellationToken);
    }

    private static WorkflowEvent CreateWorkflowEvent(WorkflowDefinition workflow, WorkflowTestRunRequest? request)
    {
        var currentRecord = request is null
            ? CreateSampleCurrentRecord(workflow.ObjectName)
            : ApiMappings.ToObjectDictionary(request.CurrentRecord);
        var originalRecord = ApiMappings.ToObjectDictionary(request?.OriginalRecord);

        if (!currentRecord.ContainsKey("Id"))
            currentRecord["Id"] = Guid.NewGuid();

        var recordId = request?.RecordId ?? currentRecord["Id"]?.ToString();

        return new WorkflowEvent
        {
            ObjectName = workflow.ObjectName,
            EventName = workflow.EventName,
            RecordId = recordId,
            CurrentRecord = currentRecord,
            OriginalRecord = originalRecord,
            ChangedFields = request?.ChangedFields ?? Array.Empty<string>()
        };
    }

    private static Dictionary<string, object?> CreateSampleCurrentRecord(string objectName)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!MetadataRegistry.TryGetMetadata(objectName, out var metadata) || metadata is null)
            return values;

        foreach (var property in metadata.Properties)
            values[property.Name] = CreateSampleValue(property, objectName);

        return values;
    }

    private static object? CreateSampleValue(PropertyMetadata property, string objectName)
    {
        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (type == typeof(Guid))
            return Guid.NewGuid();
        if (type == typeof(string))
            return property.IsPrimaryKey ? Guid.NewGuid().ToString() : $"Sample {objectName}";
        if (type == typeof(decimal))
            return 1m;
        if (type == typeof(int))
            return 1;
        if (type == typeof(long))
            return 1L;
        if (type == typeof(bool))
            return true;
        if (type == typeof(DateTime))
            return DateTime.UtcNow;

        return null;
    }

    private static WorkflowDefinition WithId(WorkflowDefinition workflow, Guid id) => new()
    {
        Id = id,
        Name = workflow.Name,
        ObjectName = workflow.ObjectName,
        EventName = workflow.EventName,
        IsEnabled = workflow.IsEnabled,
        Version = workflow.Version,
        Nodes = workflow.Nodes,
        Edges = workflow.Edges
    };

    private static WorkflowDefinition WithEnabled(WorkflowDefinition workflow, bool isEnabled) => new()
    {
        Id = workflow.Id,
        Name = workflow.Name,
        ObjectName = workflow.ObjectName,
        EventName = workflow.EventName,
        IsEnabled = isEnabled,
        Version = workflow.Version,
        Nodes = workflow.Nodes,
        Edges = workflow.Edges
    };
}