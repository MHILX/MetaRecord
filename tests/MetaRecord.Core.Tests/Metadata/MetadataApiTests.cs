using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MetaRecord.Web;
using MetaRecord.Web.Contracts;
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

    private static ObjectMetadataUpsertRequest CreateNotebookRequest(Guid id) => new(
        id,
        "Notebook",
        "Notebooks",
        new[]
        {
            new PropertyMetadataUpsertRequest("Id", "Id", "Guid", true, null, false, true, null, null),
            new PropertyMetadataUpsertRequest("Title", "Title", "String", true, 200, false, false, null, "Notebook title")
        });

    private sealed class MetaRecordWebApiFactory : WebApplicationFactory<WebApiMarker>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"metarecord-web-{Guid.NewGuid():N}.db");
        private readonly string? _previousDbPath;

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