using MetaRecord.Data;
using MetaRecord.Models;

namespace MetaRecord.Services;

/// <summary>
/// Initializes the metadata system at application startup.
/// Loads metadata from the database and registers it with MetadataRegistry.
/// </summary>
public static class MetadataLoader
{
    private static readonly string[] LegacyDemoObjectNames = ["Product", "ProductAuditEntry"];

    /// <summary>
    /// Initializes the database and loads all metadata into the registry.
    /// </summary>
    public static async Task InitializeAsync(MetaRecordDbContext context, IEnumerable<IObjectMetadata>? seedData = null)
    {
        Console.WriteLine("  [META] Initializing metadata system...");

        MetadataRegistry.Clear();

        // Ensure database is created
        await context.Database.EnsureCreatedAsync();
        Console.WriteLine($"  [META] Database: {context.DbPath}");

        var repository = new MetadataRepository(context);

        await RemoveLegacyDemoMetadataAsync(repository);

        // Seed any missing demo metadata before loading the registry
        if (seedData != null)
        {
            await repository.SeedMissingAsync(seedData);
        }

        // Load all metadata from database
        var allMetadata = await repository.LoadAllMetadataAsync();
        var count = 0;

        foreach (var metadata in allMetadata)
        {
            MetadataRegistry.RegisterByName(metadata);
            count++;
        }

        var version = await repository.GetVersionAsync();
        Console.WriteLine($"  [META] Loaded {count} object definitions (version {version})");
    }

    private static async Task RemoveLegacyDemoMetadataAsync(MetadataRepository repository)
    {
        var removedCount = 0;

        foreach (var objectName in LegacyDemoObjectNames)
        {
            var metadata = await repository.GetByNameAsync(objectName);
            if (metadata is null)
                continue;

            if (await repository.DeleteAsync(metadata.Id))
                removedCount++;
        }

        if (removedCount > 0)
            Console.WriteLine($"  [META] Removed {removedCount} legacy object definition(s) from database");
    }
}
