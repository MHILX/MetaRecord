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
        var metadata = CreateTodoMetadata();
        var id = Guid.NewGuid();
        var createdAt = new DateTime(2026, 4, 28, 12, 30, 0, DateTimeKind.Utc);

        store.EnsureTableExists(metadata);
        store.InsertValues(metadata, new Dictionary<string, object?>
        {
            ["Id"] = id,
            ["Title"] = "Todo item",
            ["Description"] = "Sample todo",
            ["Priority"] = 5,
            ["IsActive"] = true,
            ["CreatedAt"] = createdAt
        });

        var values = store.FindValues(metadata, id);

        Assert.Equal(id, values["Id"]);
        Assert.Equal("Todo item", values["Title"]);
        Assert.Equal("Sample todo", values["Description"]);
        Assert.Equal(5, values["Priority"]);
        Assert.Equal(true, values["IsActive"]);
        Assert.Equal(createdAt, values["CreatedAt"]);
    }

    [Fact]
    public void UpdateValues_updates_subset_without_overwriting_omitted_fields()
    {
        var store = new EntityStore(_dbPath);
        var metadata = CreateTodoMetadata();
        var id = Guid.NewGuid();

        store.EnsureTableExists(metadata);
        store.InsertValues(metadata, new Dictionary<string, object?>
        {
            ["Id"] = id,
            ["Title"] = "Todo item",
            ["Description"] = "Sample todo",
            ["Priority"] = 5,
            ["IsActive"] = true,
            ["CreatedAt"] = new DateTime(2026, 4, 28, 12, 30, 0, DateTimeKind.Utc)
        });

        store.UpdateValues(metadata, id, new Dictionary<string, object?>
        {
            ["Priority"] = 7,
            ["Description"] = "Updated todo"
        });

        var values = store.FindValues(metadata, id);
        Assert.Equal("Todo item", values["Title"]);
        Assert.Equal("Updated todo", values["Description"]);
        Assert.Equal(7, values["Priority"]);
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
        var metadata = CreateTodoMetadata();

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
        var metadata = CreateTodoMetadata();

        store.EnsureTableExists(metadata);

        var values = store.FindValues(metadata, Guid.NewGuid());

        Assert.Empty(values);
    }

    [Fact]
    public void AllValues_skips_missing_columns_in_a_drifted_table()
    {
        var store = new EntityStore(_dbPath);
        var metadata = CreateDriftedTodoMetadata();

        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Todos (
                    Id TEXT PRIMARY KEY,
                    Title TEXT NOT NULL
                );

                INSERT INTO Todos (Id, Title) VALUES ('11111111-1111-1111-1111-111111111111', 'Todo item');
                """;
            command.ExecuteNonQuery();
        }

        var values = store.AllValues(metadata);

        Assert.Single(values);
        Assert.Equal("Todo item", values[0]["Title"]);
        Assert.Null(values[0]["Description"]);
    }

    [Fact]
    public void Generic_insert_update_and_find_still_work()
    {
        var store = new EntityStore(_dbPath);
        var metadata = CreateTodoMetadata();
        var todo = new Todo
        {
            Title = "Todo item",
            Description = "Initial todo",
            Status = "Open",
            Priority = 5
        };

        store.EnsureTableExists(metadata);
        store.Insert(todo, metadata);

        var inserted = store.Find<Todo>(metadata, todo.Id);
        Assert.NotNull(inserted);
        Assert.Equal("Todo item", inserted.Title);
        Assert.Equal("Initial todo", inserted.Description);
        Assert.Equal("Open", inserted.Status);
        Assert.Equal(5, inserted.Priority);

        todo.Description = "Updated todo";
        todo.Priority = 8;
        store.Update(todo, metadata, todo.Id);

        var updated = store.Find<Todo>(metadata, todo.Id);
        Assert.NotNull(updated);
        Assert.Equal("Todo item", updated.Title);
        Assert.Equal("Updated todo", updated.Description);
        Assert.Equal("Open", updated.Status);
        Assert.Equal(8, updated.Priority);
    }

    private static ObjectMetadata CreateTodoMetadata() => new()
    {
        Name = "Todo",
        TableName = "Todos",
        Properties = new[]
        {
            new PropertyMetadata("Id", "Id", typeof(Guid), true) { IsPrimaryKey = true },
            new PropertyMetadata("Title", "Title", typeof(string), true),
            new PropertyMetadata("Description", "Description", typeof(string), false),
            new PropertyMetadata("Priority", "Priority", typeof(int), false),
            new PropertyMetadata("IsActive", "IsActive", typeof(bool), false),
            new PropertyMetadata("CreatedAt", "CreatedAt", typeof(DateTime), false)
        }
    };

    private static ObjectMetadata CreateDriftedTodoMetadata() => new()
    {
        Name = "Todo",
        TableName = "Todos",
        Properties = new[]
        {
            new PropertyMetadata("Id", "Id", typeof(Guid), true) { IsPrimaryKey = true },
            new PropertyMetadata("Title", "Title", typeof(string), true),
            new PropertyMetadata("Description", "Description", typeof(string), false)
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