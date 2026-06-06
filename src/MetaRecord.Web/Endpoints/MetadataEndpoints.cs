using MetaRecord.Data;
using MetaRecord.Models;
using MetaRecord.Services;
using MetaRecord.Web.Contracts;
using MetaRecord.Web.Infrastructure;

namespace MetaRecord.Web.Endpoints;

public static class MetadataEndpoints
{
    public static IEndpointRouteBuilder MapMetadataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/metadata");

        group.MapGet("/objects", async (MetadataRepository repository) =>
        {
            var metadata = await repository.LoadAllMetadataAsync();
            return Results.Ok(metadata.Select(ApiMappings.ToResponse).OrderBy(item => item.Name));
        });

        group.MapGet("/objects/{name}", async (string name, MetadataRepository repository) =>
        {
            var metadata = await repository.GetByNameAsync(name);
            if (metadata is null)
                return Results.NotFound();

            return Results.Ok(ApiMappings.ToResponse(metadata));
        });

        group.MapGet("/objects/{id:guid}", async (Guid id, MetadataRepository repository) =>
        {
            var metadata = await repository.GetByIdAsync(id);
            if (metadata is null)
                return Results.NotFound();

            return Results.Ok(ApiMappings.ToResponse(metadata));
        });

        group.MapPost("/objects/validate", async (
            ObjectMetadataUpsertRequest request,
            MetadataRepository repository) =>
        {
            var existing = (await repository.LoadAllMetadataAsync()).ToList();
            return Results.Ok(MetadataDefinitionValidator.Validate(request, existing));
        });

        group.MapPost("/objects", async (
            ObjectMetadataUpsertRequest request,
            MetadataRepository repository,
            EntityStore entityStore) =>
        {
            var existing = (await repository.LoadAllMetadataAsync()).ToList();
            var validation = MetadataDefinitionValidator.Validate(request, existing);
            if (!validation.IsValid)
                return Results.BadRequest(validation);

            var id = request.Id is { } requestId && requestId != Guid.Empty ? requestId : Guid.NewGuid();
            if (await repository.GetByIdAsync(id) is not null)
            {
                return Results.Conflict(new MetadataValidationResponse(false, new[]
                {
                    new MetadataValidationIssue(MetadataValidationSeverity.Error, $"Object definition '{id}' already exists.", "id")
                }));
            }

            var metadata = ApiMappings.ToMetadata(request with { Id = id }, id);
            await repository.SaveAsync(metadata);
            await MetadataRegistrySynchronizer.RefreshAsync(repository, entityStore);

            return Results.Created($"/api/metadata/objects/{metadata.Id}", ApiMappings.ToResponse(metadata));
        });

        group.MapPut("/objects/{id:guid}", async (
            Guid id,
            ObjectMetadataUpsertRequest request,
            MetadataRepository repository,
            EntityStore entityStore) =>
        {
            var existing = await repository.GetByIdAsync(id);
            if (existing is null)
                return Results.NotFound();

            if (request.Id is { } requestId && requestId != Guid.Empty && requestId != id)
            {
                return Results.BadRequest(new MetadataValidationResponse(false, new[]
                {
                    new MetadataValidationIssue(MetadataValidationSeverity.Error, "The request id must match the route id.", "id")
                }));
            }

            var normalizedRequest = request with { Id = id };
            var existingMetadata = (await repository.LoadAllMetadataAsync()).ToList();
            var validation = MetadataDefinitionValidator.Validate(normalizedRequest, existingMetadata, id);
            if (!validation.IsValid)
                return Results.BadRequest(validation);

            var metadata = ApiMappings.ToMetadata(normalizedRequest, id);
            await repository.SaveAsync(metadata);
            await MetadataRegistrySynchronizer.RefreshAsync(repository, entityStore);

            return Results.Ok(ApiMappings.ToResponse(metadata));
        });

        group.MapDelete("/objects/{id:guid}", async (
            Guid id,
            MetadataRepository repository,
            EntityStore entityStore) =>
        {
            if (!await repository.DeleteAsync(id))
                return Results.NotFound();

            await MetadataRegistrySynchronizer.RefreshAsync(repository, entityStore);
            return Results.NoContent();
        });

        return app;
    }
}