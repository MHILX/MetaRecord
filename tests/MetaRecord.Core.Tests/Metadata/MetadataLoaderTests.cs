using MetaRecord.Data;
using MetaRecord.Models;
using MetaRecord.Services;
using MetaRecord.Workflows;
using Microsoft.Data.Sqlite;

namespace MetaRecord.Core.Tests.Metadata;

public sealed class MetadataLoaderTests
{
    [Fact]
    public async Task Initialize_async_removes_legacy_product_metadata_before_loading_demo_metadata()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"metarecord-metadata-{Guid.NewGuid():N}.db");

        try
        {
            await using (var context = new MetaRecordDbContext(dbPath))
            {
                await context.Database.EnsureCreatedAsync();

                var repository = new MetadataRepository(context);
                await repository.SaveAsync(CreateLegacyMetadata("Product", "Products"));
                await repository.SaveAsync(CreateLegacyMetadata("ProductAuditEntry", "ProductAuditEntries"));

                MetadataRegistry.RegisterByName(CreateLegacyMetadata("Product", "Products"));
                MetadataRegistry.RegisterByName(CreateLegacyMetadata("ProductAuditEntry", "ProductAuditEntries"));

                await MetadataLoader.InitializeAsync(context, DemoMetadataSeeder.CreateDemoMetadata());

                var allMetadata = (await repository.LoadAllMetadataAsync()).ToList();
                Assert.Equal(2, allMetadata.Count);
                Assert.DoesNotContain(allMetadata, metadata => metadata.Name == "Product");
                Assert.DoesNotContain(allMetadata, metadata => metadata.Name == "ProductAuditEntry");
                Assert.Contains(allMetadata, metadata => metadata.Name == DemoDomain.ObjectName);
                Assert.Contains(allMetadata, metadata => metadata.Name == DemoDomain.AuditEntryObjectName);

                Assert.False(MetadataRegistry.TryGetMetadata("Product", out _));
                Assert.False(MetadataRegistry.TryGetMetadata("ProductAuditEntry", out _));
                Assert.True(MetadataRegistry.TryGetMetadata(DemoDomain.ObjectName, out _));
            }
        }
        finally
        {
            MetadataRegistry.Clear();
            SqliteConnection.ClearAllPools();

            foreach (var path in new[] { dbPath, $"{dbPath}-shm", $"{dbPath}-wal", $"{dbPath}-journal" })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    private static ObjectMetadata CreateLegacyMetadata(string name, string tableName) => new()
    {
        Name = name,
        TableName = tableName,
        Properties = new[]
        {
            new PropertyMetadata("Id", "Id", typeof(Guid), true) { IsPrimaryKey = true },
            new PropertyMetadata("Name", "Name", typeof(string), true) { MaxLength = 100, Caption = "Name" }
        }
    };
}