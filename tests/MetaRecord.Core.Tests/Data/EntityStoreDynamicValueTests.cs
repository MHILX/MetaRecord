using MetaRecord.Data;
using MetaRecord.Models;
using Microsoft.Data.Sqlite;

namespace MetaRecord.Core.Tests.Data;

public sealed class EntityStoreDynamicValueTests : IDisposable
{
    private readonly string _dbPath = CreateTempDbPath();

    public void Dispose()
    {
        DeleteTempDb(_dbPath);
    }

    [Fact]
    public void InsertValues_and_FindValues_round_trip_clr_compatible_values()
    {
        var store = new EntityStore(_dbPath);
        var metadata = CreateProductMetadata();
        var id = Guid.NewGuid();
        var createdAt = new DateTime(2026, 4, 28, 12, 30, 0, DateTimeKind.Utc);

        store.EnsureTableExists(metadata);
        store.InsertValues(metadata, new Dictionary<string, object?>
        {
            ["Id"] = id,
            ["Name"] = "Widget",
            ["Price"] = 9.99m,
            ["Quantity"] = 5,
            ["IsActive"] = true,
            ["CreatedAt"] = createdAt
        });

        var values = store.FindValues(metadata, id);

        Assert.Equal(id, values["Id"]);
        Assert.Equal("Widget", values["Name"]);
        Assert.Equal(9.99m, values["Price"]);
        Assert.Equal(5, values["Quantity"]);
        Assert.Equal(true, values["IsActive"]);
        Assert.Equal(createdAt, values["CreatedAt"]);
    }

    [Fact]
    public void UpdateValues_updates_subset_without_overwriting_omitted_fields()
    {
        var store = new EntityStore(_dbPath);
        var metadata = CreateProductMetadata();
        var id = Guid.NewGuid();

        store.EnsureTableExists(metadata);
        store.InsertValues(metadata, new Dictionary<string, object?>
        {
            ["Id"] = id,
            ["Name"] = "Widget",
            ["Price"] = 9.99m,
            ["Quantity"] = 5,
            ["IsActive"] = true,
            ["CreatedAt"] = new DateTime(2026, 4, 28, 12, 30, 0, DateTimeKind.Utc)
        });

        store.UpdateValues(metadata, id, new Dictionary<string, object?>
        {
            ["Price"] = 15.50m,
            ["Quantity"] = 7
        });

        var values = store.FindValues(metadata, id);
        Assert.Equal("Widget", values["Name"]);
        Assert.Equal(15.50m, values["Price"]);
        Assert.Equal(7, values["Quantity"]);
        Assert.Equal(true, values["IsActive"]);
    }

    [Fact]
    public void Dynamic_apis_use_configured_primary_key_column()
    {
        var store = new EntityStore(_dbPath);
        var metadata = new ObjectMetadata
        {
            Name = "ExternalTask",
            TableName = "ExternalTasks",
            Properties = new[]
            {
                new PropertyMetadata("RecordKey", "RecordKey", typeof(Guid), true) { IsPrimaryKey = true },
                new PropertyMetadata("Title", "Title", typeof(string), true),
                new PropertyMetadata("Priority", "Priority", typeof(string), false)
            }
        };
        var id = Guid.NewGuid();

        store.EnsureTableExists(metadata);
        store.InsertValues(metadata, new Dictionary<string, object?>
        {
            ["RecordKey"] = id,
            ["Title"] = "First title",
            ["Priority"] = "Normal"
        });

        store.UpdateValues(metadata, id, new Dictionary<string, object?>
        {
            ["Title"] = "Updated title"
        });

        var values = store.FindValues(metadata, id);
        Assert.Equal(id, values["RecordKey"]);
        Assert.Equal("Updated title", values["Title"]);
        Assert.Equal("Normal", values["Priority"]);
    }

    [Fact]
    public void Dynamic_apis_reject_unknown_fields()
    {
        var store = new EntityStore(_dbPath);
        var metadata = CreateProductMetadata();

        store.EnsureTableExists(metadata);

        var exception = Assert.Throws<InvalidOperationException>(() => store.InsertValues(metadata, new Dictionary<string, object?>
        {
            ["MissingField"] = "value"
        }));

        Assert.Contains("MissingField", exception.Message);
    }

    [Fact]
    public void FindValues_returns_empty_dictionary_when_record_is_missing()
    {
        var store = new EntityStore(_dbPath);
        var metadata = CreateProductMetadata();

        store.EnsureTableExists(metadata);

        var values = store.FindValues(metadata, Guid.NewGuid());

        Assert.Empty(values);
    }

    [Fact]
    public void Generic_insert_update_and_find_still_work()
    {
        var store = new EntityStore(_dbPath);
        var metadata = CreateProductMetadata();
        var product = new Product
        {
            Name = "Widget",
            Price = 9.99m,
            Quantity = 5
        };

        store.EnsureTableExists(metadata);
        store.Insert(product, metadata);

        var inserted = store.Find<Product>(metadata, product.Id);
        Assert.NotNull(inserted);
        Assert.Equal("Widget", inserted.Name);
        Assert.Equal(9.99m, inserted.Price);
        Assert.Equal(5, inserted.Quantity);

        product.Price = 12.25m;
        product.Quantity = 8;
        store.Update(product, metadata, product.Id);

        var updated = store.Find<Product>(metadata, product.Id);
        Assert.NotNull(updated);
        Assert.Equal("Widget", updated.Name);
        Assert.Equal(12.25m, updated.Price);
        Assert.Equal(8, updated.Quantity);
    }

    private static ObjectMetadata CreateProductMetadata() => new()
    {
        Name = "Product",
        TableName = "Products",
        Properties = new[]
        {
            new PropertyMetadata("Id", "Id", typeof(Guid), true) { IsPrimaryKey = true },
            new PropertyMetadata("Name", "Name", typeof(string), true),
            new PropertyMetadata("Price", "Price", typeof(decimal), true),
            new PropertyMetadata("Quantity", "Quantity", typeof(int), false),
            new PropertyMetadata("IsActive", "IsActive", typeof(bool), false),
            new PropertyMetadata("CreatedAt", "CreatedAt", typeof(DateTime), false)
        }
    };

    private static string CreateTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"metarecord-{Guid.NewGuid():N}.db");

    private static void DeleteTempDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();

        foreach (var path in new[] { dbPath, $"{dbPath}-shm", $"{dbPath}-wal", $"{dbPath}-journal" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}