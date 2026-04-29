using MetaRecord.Models;
using MetaRecord.Web.Infrastructure;

namespace MetaRecord.Web.Endpoints;

public static class MetadataEndpoints
{
    public static IEndpointRouteBuilder MapMetadataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/metadata");

        group.MapGet("/objects", () =>
            Results.Ok(MetadataRegistry.GetAll().Select(ApiMappings.ToResponse).OrderBy(metadata => metadata.Name)));

        group.MapGet("/objects/{name}", (string name) =>
        {
            if (!MetadataRegistry.TryGetMetadata(name, out var metadata) || metadata is null)
                return Results.NotFound();

            return Results.Ok(ApiMappings.ToResponse(metadata));
        });

        return app;
    }
}