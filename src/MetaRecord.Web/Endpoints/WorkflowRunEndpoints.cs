using MetaRecord.Data;
using MetaRecord.Web.Infrastructure;
using MetaRecord.Workflows.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MetaRecord.Web.Endpoints;

public static class WorkflowRunEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowRunEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/workflows/{id:guid}/runs", async (
            Guid id,
            MetaRecordDbContext context,
            CancellationToken cancellationToken) =>
        {
            var workflowExists = await context.WorkflowDefinitions
                .AsNoTracking()
                .AnyAsync(workflow => workflow.Id == id, cancellationToken);

            if (!workflowExists)
                return Results.NotFound();

            var runs = await context.WorkflowRuns
                .AsNoTracking()
                .Where(run => run.WorkflowId == id)
                .OrderByDescending(run => run.StartedAt)
                .ToListAsync(cancellationToken);

            return Results.Ok(runs.Select(ApiMappings.ToSummaryResponse));
        });

        app.MapGet("/api/workflow-runs/{runId:guid}", async (
            Guid runId,
            WorkflowRepository repository,
            CancellationToken cancellationToken) =>
        {
            var run = await repository.GetRunAsync(runId, cancellationToken);
            return run is null ? Results.NotFound() : Results.Ok(ApiMappings.ToDetailResponse(run));
        });

        return app;
    }
}