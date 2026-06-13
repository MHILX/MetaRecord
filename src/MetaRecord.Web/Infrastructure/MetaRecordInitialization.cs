using MetaRecord.Data;
using MetaRecord.Models;
using MetaRecord.Services;
using MetaRecord.Workflows;
using MetaRecord.Workflows.Persistence;

namespace MetaRecord.Web.Infrastructure;

internal static class MetaRecordInitialization
{
    public static async Task InitializeMetaRecordAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MetaRecordDbContext>();
        var entityStore = scope.ServiceProvider.GetRequiredService<EntityStore>();
        var metadataRepository = scope.ServiceProvider.GetRequiredService<MetadataRepository>();

        await MetadataLoader.InitializeAsync(context, DemoMetadataSeeder.CreateDemoMetadata());
        await MetadataRegistrySynchronizer.RefreshAsync(metadataRepository, entityStore);
        var workflowRepository = scope.ServiceProvider.GetRequiredService<WorkflowRepository>();
        await DemoWorkflowSeeder.SeedAsync(workflowRepository);
    }
}