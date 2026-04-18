using MetaRecord.Data;
using MetaRecord.Models;

namespace MetaRecord.Services;

/// <summary>
/// Initializes the metadata system at application startup.
/// Loads metadata from the database and registers it with MetadataRegistry.
/// </summary>
public static class MetadataLoader
{
    /// <summary>
    /// Initializes the database and loads all metadata into the registry.
    /// </summary>
    public static async Task InitializeAsync(MetaRecordDbContext context, IEnumerable<IObjectMetadata>? seedData = null)
    {
        Console.WriteLine("  [META] Initializing metadata system...");

        // Ensure database is created
        await context.Database.EnsureCreatedAsync();
        Console.WriteLine($"  [META] Database: {context.DbPath}");

        var repository = new MetadataRepository(context);

        // Seed initial data if provided and DB is empty
        if (seedData != null)
        {
            await repository.SeedIfEmptyAsync(seedData);
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
}
