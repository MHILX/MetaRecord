using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MetaRecord.Models;
using MetaRecord.Web;
using MetaRecord.Web.Contracts;
using MetaRecord.Workflows;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MetaRecord.Core.Tests.Metadata;

public sealed class MetadataApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Metadata_crud_round_trips_and_refreshes_the_registry()
    {
        using var factory = new MetaRecordWebApiFactory();
        var client = factory.CreateClient();
        var objectId = Guid.NewGuid();
        var request = CreateNotebookRequest(objectId);

        var validateResponse = await client.PostAsJsonAsync("/api/metadata/objects/validate", request);
        var validation = await validateResponse.Content.ReadFromJsonAsync<MetadataValidationResponse>(JsonOptions);

        validateResponse.EnsureSuccessStatusCode();
        Assert.NotNull(validation);
        Assert.True(validation.IsValid);

        var createResponse = await client.PostAsJsonAsync("/api/metadata/objects", request);
        var created = await createResponse.Content.ReadFromJsonAsync<ObjectMetadataResponse>(JsonOptions);

        createResponse.EnsureSuccessStatusCode();
        Assert.NotNull(created);
        Assert.Equal(objectId, created.Id);
        Assert.Equal("Notebook", created.Name);
        Assert.Equal("Notebooks", created.TableName);
        Assert.Equal(2, created.Properties.Count);

        var loaded = await client.GetFromJsonAsync<ObjectMetadataResponse>($"/api/metadata/objects/{created.Id}", JsonOptions);
        Assert.NotNull(loaded);
        Assert.Equal(created.Id, loaded.Id);

        var updatedRequest = request with { Name = "NotebookEntry" };
        var updateResponse = await client.PutAsJsonAsync($"/api/metadata/objects/{created.Id}", updatedRequest);

        updateResponse.EnsureSuccessStatusCode();

        var updated = await client.GetFromJsonAsync<ObjectMetadataResponse>($"/api/metadata/objects/{created.Id}", JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("NotebookEntry", updated.Name);

        var allMetadata = await client.GetFromJsonAsync<ObjectMetadataResponse[]>("/api/metadata/objects", JsonOptions);
        Assert.NotNull(allMetadata);
        Assert.Contains(allMetadata, metadata => metadata.Name == "NotebookEntry");

        var deleteResponse = await client.DeleteAsync($"/api/metadata/objects/{created.Id}");
        deleteResponse.EnsureSuccessStatusCode();

        var deleted = await client.GetAsync($"/api/metadata/objects/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);

        var afterDelete = await client.GetFromJsonAsync<ObjectMetadataResponse[]>("/api/metadata/objects", JsonOptions);
        Assert.NotNull(afterDelete);
        Assert.DoesNotContain(afterDelete, metadata => metadata.Name == "NotebookEntry");
    }

    [Fact]
    public async Task Metadata_relationships_round_trip_through_the_api()
    {
        using var factory = new MetaRecordWebApiFactory();
        var client = factory.CreateClient();
        var request = new ObjectMetadataUpsertRequest(
            Guid.NewGuid(),
            "Notebook",
            "Notebooks",
            new[]
            {
                new PropertyMetadataUpsertRequest("Id", "Id", "Guid", true, null, false, true, null, null),
                new PropertyMetadataUpsertRequest("Title", "Title", "String", true, 200, false, false, null, null),
                new PropertyMetadataUpsertRequest("TodoId", "TodoId", "Guid", false, null, false, false, null, null)
            },
            new[]
            {
                new RelationshipMetadataUpsertRequest(
                    "Todo",
                    "TodoId",
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    DemoDomain.ObjectName,
                    "Id",
                    RelationshipCardinality.ManyToOne,
                    RelationshipDeleteBehavior.Restrict,
                    "Title",
                    "Related todo",
                    "Lookup to the owning todo")
            });

        var validateResponse = await client.PostAsJsonAsync("/api/metadata/objects/validate", request);
        var validation = await validateResponse.Content.ReadFromJsonAsync<MetadataValidationResponse>(JsonOptions);

        validateResponse.EnsureSuccessStatusCode();
        Assert.NotNull(validation);
        Assert.True(validation.IsValid);

        var createResponse = await client.PostAsJsonAsync("/api/metadata/objects", request);
        var created = await createResponse.Content.ReadFromJsonAsync<ObjectMetadataResponse>(JsonOptions);

        createResponse.EnsureSuccessStatusCode();
        Assert.NotNull(created);
        Assert.Single(created.Relationships);

        var relationship = created.Relationships[0];
        Assert.Equal("Todo", relationship.Name);
        Assert.Equal("TodoId", relationship.SourcePropertyName);
        Assert.Equal(DemoDomain.ObjectName, relationship.TargetObjectName);
        Assert.Equal("Id", relationship.TargetPropertyName);
        Assert.Equal(RelationshipCardinality.ManyToOne, relationship.Cardinality);

        var loaded = await client.GetFromJsonAsync<ObjectMetadataResponse>($"/api/metadata/objects/{created.Id}", JsonOptions);
        Assert.NotNull(loaded);
        Assert.Single(loaded.Relationships);
        Assert.Equal("TodoId", loaded.Relationships[0].SourcePropertyName);
    }

    [Fact]
    public async Task Metadata_record_save_rejects_missing_relationship_targets()
    {
        using var factory = new MetaRecordWebApiFactory();
        var client = factory.CreateClient();
        var todoObjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var targetTodoRecordId = Guid.NewGuid();
        var notebookId = Guid.NewGuid();
        var request = CreateNotebookWithRelationshipRequest(notebookId, todoObjectId);

        var validateResponse = await client.PostAsJsonAsync("/api/metadata/objects/validate", request);
        var validation = await validateResponse.Content.ReadFromJsonAsync<MetadataValidationResponse>(JsonOptions);

        validateResponse.EnsureSuccessStatusCode();
        Assert.NotNull(validation);
        Assert.True(validation.IsValid);

        var createMetadataResponse = await client.PostAsJsonAsync("/api/metadata/objects", request);
        createMetadataResponse.EnsureSuccessStatusCode();

        var missingTargetResponse = await client.PostAsJsonAsync(
            "/api/metadata/objects/11111111-1111-1111-1111-111111111111/records",
            new MetadataRecordSaveRequest(new Dictionary<string, JsonElement>
            {
                ["Id"] = JsonSerializer.SerializeToElement(targetTodoRecordId, JsonOptions),
                ["Title"] = JsonSerializer.SerializeToElement("Target todo", JsonOptions)
            }));

        missingTargetResponse.EnsureSuccessStatusCode();

        var createNotebookRecordResponse = await client.PostAsJsonAsync(
            $"/api/metadata/objects/{notebookId}/records",
            new MetadataRecordSaveRequest(new Dictionary<string, JsonElement>
            {
                ["Id"] = JsonSerializer.SerializeToElement(Guid.NewGuid(), JsonOptions),
                ["Title"] = JsonSerializer.SerializeToElement("Notebook A", JsonOptions),
                ["TodoId"] = JsonSerializer.SerializeToElement(targetTodoRecordId, JsonOptions)
            }));

        var createdNotebookRecord = await createNotebookRecordResponse.Content.ReadFromJsonAsync<MetadataRecordSaveResponse>(JsonOptions);

        createNotebookRecordResponse.EnsureSuccessStatusCode();
        Assert.NotNull(createdNotebookRecord);

        var invalidNotebookRecordResponse = await client.PostAsJsonAsync(
            $"/api/metadata/objects/{notebookId}/records",
            new MetadataRecordSaveRequest(new Dictionary<string, JsonElement>
            {
                ["Id"] = JsonSerializer.SerializeToElement(Guid.NewGuid(), JsonOptions),
                ["Title"] = JsonSerializer.SerializeToElement("Notebook B", JsonOptions),
                ["TodoId"] = JsonSerializer.SerializeToElement(Guid.NewGuid(), JsonOptions)
            }));

        var invalidNotebookRecord = await invalidNotebookRecordResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, invalidNotebookRecordResponse.StatusCode);
        Assert.Contains("missing 'Todo' record", invalidNotebookRecord, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Metadata_record_reads_can_expand_related_records()
    {
        using var factory = new MetaRecordWebApiFactory();
        var client = factory.CreateClient();
        var todoObjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var notebookId = Guid.NewGuid();
        var notebookRequest = CreateNotebookWithRelationshipRequest(notebookId, todoObjectId);

        var createMetadataResponse = await client.PostAsJsonAsync("/api/metadata/objects", notebookRequest);
        createMetadataResponse.EnsureSuccessStatusCode();

        var todoRecordId = Guid.NewGuid();
        var createTodoResponse = await client.PostAsJsonAsync(
            "/api/metadata/objects/11111111-1111-1111-1111-111111111111/records",
            new MetadataRecordSaveRequest(new Dictionary<string, JsonElement>
            {
                ["Id"] = JsonSerializer.SerializeToElement(todoRecordId, JsonOptions),
                ["Title"] = JsonSerializer.SerializeToElement("Expanded todo", JsonOptions)
            }));

        createTodoResponse.EnsureSuccessStatusCode();

        var saveNotebookResponse = await client.PostAsJsonAsync(
            $"/api/metadata/objects/{notebookId}/records",
            new MetadataRecordSaveRequest(new Dictionary<string, JsonElement>
            {
                ["Id"] = JsonSerializer.SerializeToElement(Guid.NewGuid(), JsonOptions),
                ["Title"] = JsonSerializer.SerializeToElement("Notebook for expansion", JsonOptions),
                ["TodoId"] = JsonSerializer.SerializeToElement(todoRecordId, JsonOptions)
            }));

        saveNotebookResponse.EnsureSuccessStatusCode();

        var expandedRecords = await client.GetFromJsonAsync<Dictionary<string, JsonElement>[]>($"/api/metadata/objects/{notebookId}/records?expandRelationships=true", JsonOptions);
        Assert.NotNull(expandedRecords);
        Assert.Single(expandedRecords);

        var record = expandedRecords[0];
        Assert.True(record.ContainsKey("__relationships"));

        var relationships = record["__relationships"].EnumerateArray().ToArray();
        Assert.Single(relationships);
        Assert.Equal("Todo", relationships[0].GetProperty("name").GetString());
        Assert.True(relationships[0].GetProperty("isResolved").GetBoolean());
        Assert.Equal("Expanded todo", relationships[0].GetProperty("displayValue").GetString());
    }

    [Fact]
    public async Task Metadata_existing_guid_field_can_be_promoted_to_relationship_without_rewriting_records()
    {
        using var factory = new MetaRecordWebApiFactory();
        var client = factory.CreateClient();
        var todoObjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var targetTodoRecordId = Guid.NewGuid();
        var notebookId = Guid.NewGuid();
        var baseRequest = CreateNotebookRequest(notebookId, includeTodoLookupField: true);

        var createMetadataResponse = await client.PostAsJsonAsync("/api/metadata/objects", baseRequest);
        createMetadataResponse.EnsureSuccessStatusCode();

        var createTodoResponse = await client.PostAsJsonAsync(
            "/api/metadata/objects/11111111-1111-1111-1111-111111111111/records",
            new MetadataRecordSaveRequest(new Dictionary<string, JsonElement>
            {
                ["Id"] = JsonSerializer.SerializeToElement(targetTodoRecordId, JsonOptions),
                ["Title"] = JsonSerializer.SerializeToElement("Promoted target", JsonOptions)
            }));

        createTodoResponse.EnsureSuccessStatusCode();

        var saveNotebookRecordResponse = await client.PostAsJsonAsync(
            $"/api/metadata/objects/{notebookId}/records",
            new MetadataRecordSaveRequest(new Dictionary<string, JsonElement>
            {
                ["Id"] = JsonSerializer.SerializeToElement(Guid.NewGuid(), JsonOptions),
                ["Title"] = JsonSerializer.SerializeToElement("Notebook before promotion", JsonOptions),
                ["TodoId"] = JsonSerializer.SerializeToElement(targetTodoRecordId, JsonOptions)
            }));

        saveNotebookRecordResponse.EnsureSuccessStatusCode();

        var promotedRequest = CreateNotebookWithRelationshipRequest(notebookId, todoObjectId);
        var updateMetadataResponse = await client.PutAsJsonAsync($"/api/metadata/objects/{notebookId}", promotedRequest);
        var updateValidation = await updateMetadataResponse.Content.ReadFromJsonAsync<ObjectMetadataResponse>(JsonOptions);

        updateMetadataResponse.EnsureSuccessStatusCode();
        Assert.NotNull(updateValidation);
        Assert.Single(updateValidation.Relationships);

        using (var connection = new SqliteConnection($"Data Source={factory.DbPath}"))
        {
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND name = 'IX_Notebooks_TodoId'";

            var indexName = command.ExecuteScalar() as string;
            Assert.Equal("IX_Notebooks_TodoId", indexName);
        }

        var loadedRecords = await client.GetFromJsonAsync<Dictionary<string, JsonElement>[]>($"/api/metadata/objects/{notebookId}/records", JsonOptions);
        Assert.NotNull(loadedRecords);
        Assert.Single(loadedRecords);
        Assert.Equal(targetTodoRecordId.ToString(), loadedRecords[0]["TodoId"].GetString());

        var rewriteResponse = await client.PostAsJsonAsync(
            $"/api/metadata/objects/{notebookId}/records",
            new MetadataRecordSaveRequest(new Dictionary<string, JsonElement>
            {
                ["Id"] = JsonSerializer.SerializeToElement(loadedRecords[0]["Id"].GetGuid(), JsonOptions),
                ["Title"] = JsonSerializer.SerializeToElement("Notebook after promotion", JsonOptions),
                ["TodoId"] = JsonSerializer.SerializeToElement(targetTodoRecordId, JsonOptions)
            }));

        rewriteResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Metadata_validation_rejects_duplicate_property_names()
    {
        using var factory = new MetaRecordWebApiFactory();
        var client = factory.CreateClient();
        var request = new ObjectMetadataUpsertRequest(
            Guid.NewGuid(),
            "BrokenNotebook",
            "BrokenNotebooks",
            new[]
            {
                new PropertyMetadataUpsertRequest("Id", "Id", "Guid", true, null, false, true, null, null),
                new PropertyMetadataUpsertRequest("Title", "Title", "String", true, 200, false, false, null, null),
                new PropertyMetadataUpsertRequest("Title", "TitleTwo", "String", false, 200, false, false, null, null)
            });

        var validateResponse = await client.PostAsJsonAsync("/api/metadata/objects/validate", request);
        var validation = await validateResponse.Content.ReadFromJsonAsync<MetadataValidationResponse>(JsonOptions);

        validateResponse.EnsureSuccessStatusCode();
        Assert.NotNull(validation);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue => issue.Field == "properties[2].name");

        var createResponse = await client.PostAsJsonAsync("/api/metadata/objects", request);
        var createValidation = await createResponse.Content.ReadFromJsonAsync<MetadataValidationResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, createResponse.StatusCode);
        Assert.NotNull(createValidation);
        Assert.False(createValidation.IsValid);
    }

    [Fact]
    public async Task Metadata_validation_rejects_duplicate_object_name_and_table_name()
    {
        using var factory = new MetaRecordWebApiFactory();
        var client = factory.CreateClient();
        var request = new ObjectMetadataUpsertRequest(
            Guid.NewGuid(),
            "Todo",
            "Todos",
            new[]
            {
                new PropertyMetadataUpsertRequest("Id", "Id", "Guid", true, null, false, true, null, null),
                new PropertyMetadataUpsertRequest("Title", "Title", "String", true, 200, false, false, null, null)
            });

        var validateResponse = await client.PostAsJsonAsync("/api/metadata/objects/validate", request);
        var validation = await validateResponse.Content.ReadFromJsonAsync<MetadataValidationResponse>(JsonOptions);

        validateResponse.EnsureSuccessStatusCode();
        Assert.NotNull(validation);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue => issue.Field == "name");
        Assert.Contains(validation.Issues, issue => issue.Field == "tableName");

        var createResponse = await client.PostAsJsonAsync("/api/metadata/objects", request);
        var createValidation = await createResponse.Content.ReadFromJsonAsync<MetadataValidationResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, createResponse.StatusCode);
        Assert.NotNull(createValidation);
        Assert.False(createValidation.IsValid);
    }

    [Fact]
    public async Task Metadata_validation_rejects_unsupported_clr_type()
    {
        using var factory = new MetaRecordWebApiFactory();
        var client = factory.CreateClient();
        var request = new ObjectMetadataUpsertRequest(
            Guid.NewGuid(),
            "ArchivedNotebook",
            "ArchivedNotebooks",
            new[]
            {
                new PropertyMetadataUpsertRequest("Id", "Id", "Guid", true, null, false, true, null, null),
                new PropertyMetadataUpsertRequest("Total", "Total", "Money", true, null, false, false, null, null)
            });

        var validateResponse = await client.PostAsJsonAsync("/api/metadata/objects/validate", request);
        var validation = await validateResponse.Content.ReadFromJsonAsync<MetadataValidationResponse>(JsonOptions);

        validateResponse.EnsureSuccessStatusCode();
        Assert.NotNull(validation);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue => issue.Field == "properties[1].clrType");

        var createResponse = await client.PostAsJsonAsync("/api/metadata/objects", request);
        var createValidation = await createResponse.Content.ReadFromJsonAsync<MetadataValidationResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, createResponse.StatusCode);
        Assert.NotNull(createValidation);
        Assert.False(createValidation.IsValid);
    }

    [Fact]
    public async Task Metadata_update_rejects_route_id_mismatch()
    {
        using var factory = new MetaRecordWebApiFactory();
        var client = factory.CreateClient();
        var objectId = Guid.NewGuid();
        var request = CreateNotebookRequest(objectId);

        var createResponse = await client.PostAsJsonAsync("/api/metadata/objects", request);
        createResponse.EnsureSuccessStatusCode();

        var mismatchedRequest = request with { Id = Guid.NewGuid(), Name = "NotebookRenamed" };
        var updateResponse = await client.PutAsJsonAsync($"/api/metadata/objects/{objectId}", mismatchedRequest);
        var validation = await updateResponse.Content.ReadFromJsonAsync<MetadataValidationResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);
        Assert.NotNull(validation);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue => issue.Field == "id");
    }

    [Fact]
    public async Task Metadata_record_save_inserts_and_updates_rows()
    {
        using var factory = new MetaRecordWebApiFactory();
        var client = factory.CreateClient();
        var objectId = Guid.NewGuid();
        var request = CreateNotebookRequest(objectId);

        var createMetadataResponse = await client.PostAsJsonAsync("/api/metadata/objects", request);
        createMetadataResponse.EnsureSuccessStatusCode();

        var recordId = Guid.NewGuid();
        var createRecordResponse = await client.PostAsJsonAsync(
            $"/api/metadata/objects/{objectId}/records",
            new MetadataRecordSaveRequest(new Dictionary<string, JsonElement>
            {
                ["Id"] = JsonSerializer.SerializeToElement(recordId, JsonOptions),
                ["Title"] = JsonSerializer.SerializeToElement("Notebook A", JsonOptions)
            }));

        var createdRecord = await createRecordResponse.Content.ReadFromJsonAsync<MetadataRecordSaveResponse>(JsonOptions);

        createRecordResponse.EnsureSuccessStatusCode();
        Assert.NotNull(createdRecord);
        Assert.True(createdRecord.IsNew);
        Assert.Equal(recordId.ToString(), createdRecord.RecordId);

        var updateRecordResponse = await client.PostAsJsonAsync(
            $"/api/metadata/objects/{objectId}/records",
            new MetadataRecordSaveRequest(new Dictionary<string, JsonElement>
            {
                ["Id"] = JsonSerializer.SerializeToElement(recordId, JsonOptions),
                ["Title"] = JsonSerializer.SerializeToElement("Notebook B", JsonOptions)
            }));

        var updatedRecord = await updateRecordResponse.Content.ReadFromJsonAsync<MetadataRecordSaveResponse>(JsonOptions);

        updateRecordResponse.EnsureSuccessStatusCode();
        Assert.NotNull(updatedRecord);
        Assert.False(updatedRecord.IsNew);
        Assert.Equal(recordId.ToString(), updatedRecord.RecordId);

        var recordList = await client.GetFromJsonAsync<Dictionary<string, JsonElement>[]>($"/api/metadata/objects/{objectId}/records", JsonOptions);

        Assert.NotNull(recordList);
        Assert.Single(recordList);
        Assert.Equal(recordId.ToString(), recordList[0]["Id"].GetString());
        Assert.Equal("Notebook B", recordList[0]["Title"].GetString());

        using var connection = new SqliteConnection($"Data Source={factory.DbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Title FROM Notebooks WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", recordId.ToString());

        var savedTitle = command.ExecuteScalar() as string;
        Assert.Equal("Notebook B", savedTitle);
    }

    private static ObjectMetadataUpsertRequest CreateNotebookRequest(Guid id, bool includeTodoLookupField = false) => new(
        id,
        "Notebook",
        "Notebooks",
        includeTodoLookupField
            ? new[]
            {
                new PropertyMetadataUpsertRequest("Id", "Id", "Guid", true, null, false, true, null, null),
                new PropertyMetadataUpsertRequest("Title", "Title", "String", true, 200, false, false, null, "Notebook title"),
                new PropertyMetadataUpsertRequest("TodoId", "TodoId", "Guid", false, null, false, false, null, "Related todo id")
            }
            : new[]
            {
                new PropertyMetadataUpsertRequest("Id", "Id", "Guid", true, null, false, true, null, null),
                new PropertyMetadataUpsertRequest("Title", "Title", "String", true, 200, false, false, null, "Notebook title")
            },
        Array.Empty<RelationshipMetadataUpsertRequest>());

    private static ObjectMetadataUpsertRequest CreateNotebookWithRelationshipRequest(Guid id, Guid targetObjectId) => new(
        id,
        "Notebook",
        "Notebooks",
        new[]
        {
            new PropertyMetadataUpsertRequest("Id", "Id", "Guid", true, null, false, true, null, null),
            new PropertyMetadataUpsertRequest("Title", "Title", "String", true, 200, false, false, null, "Notebook title"),
            new PropertyMetadataUpsertRequest("TodoId", "TodoId", "Guid", false, null, false, false, null, null)
        },
        new[]
        {
            new RelationshipMetadataUpsertRequest(
                "Todo",
                "TodoId",
                targetObjectId,
                DemoDomain.ObjectName,
                "Id",
                RelationshipCardinality.ManyToOne,
                RelationshipDeleteBehavior.Restrict,
                "Title",
                "Related todo",
                "Lookup to the owning todo")
        });

    private sealed class MetaRecordWebApiFactory : WebApplicationFactory<WebApiMarker>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"metarecord-web-{Guid.NewGuid():N}.db");
        private readonly string? _previousDbPath;

        public string DbPath => _dbPath;

        public MetaRecordWebApiFactory()
        {
            _previousDbPath = Environment.GetEnvironmentVariable("METARECORD_DB_PATH");
            Environment.SetEnvironmentVariable("METARECORD_DB_PATH", _dbPath);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MetaRecord:DbPath"] = _dbPath
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Environment.SetEnvironmentVariable("METARECORD_DB_PATH", _previousDbPath);
            DeleteTempDb(_dbPath);
        }

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
}