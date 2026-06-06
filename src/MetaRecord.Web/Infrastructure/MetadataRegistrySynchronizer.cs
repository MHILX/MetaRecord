using MetaRecord.Data;
using MetaRecord.Models;
using MetaRecord.Services;
using MetaRecord.Workflows;

namespace MetaRecord.Web.Infrastructure;

internal static class MetadataRegistrySynchronizer
{
    public static async Task RefreshAsync(MetadataRepository repository, EntityStore entityStore)
    {
        var metadata = (await repository.LoadAllMetadataAsync()).ToList();
        MetadataRegistry.ReplaceAll(metadata);
        MetadataRegistry.LinkType<Todo>(DemoDomain.ObjectName);
        MetadataRegistry.LinkType<Product>("Product");

        foreach (var definition in metadata)
            entityStore.EnsureTableExists(definition);
    }
}