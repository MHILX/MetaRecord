using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MetaRecord.Web;
using MetaRecord.Web.Contracts;
using MetaRecord.Workflows.Definitions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MetaRecord.Core.Tests.Workflows;

public sealed class WorkflowApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Get_node_types_returns_runtime_catalog()
    {
        using var factory = new MetaRecordWebApiFactory();
        var client = factory.CreateClient();

        var nodeTypes = await client.GetFromJsonAsync<JsonElement[]>("/api/workflow-node-types");

        Assert.NotNull(nodeTypes);
        Assert.Contains(nodeTypes, nodeType => nodeType.GetProperty("type").GetString() == "flow.condition");
        Assert.Contains(nodeTypes, nodeType => nodeType.GetProperty("type").GetString() == "action.write-log");
    }

    [Fact]
    public async Task Startup_seeds_demo_metadata_and_workflows_for_the_editor()
    {
        using var factory = new MetaRecordWebApiFactory();
        var client = factory.CreateClient();

        var metadataObjects = await client.GetFromJsonAsync<ObjectMetadataResponse[]>("/api/metadata/objects", JsonOptions);
        var workflows = await client.GetFromJsonAsync<WorkflowDefinition[]>("/api/workflows", JsonOptions);

        Assert.NotNull(metadataObjects);
        Assert.Equal(2, metadataObjects.Length);
        Assert.Contains(metadataObjects, metadata => metadata.Name == "WorkflowAuditEntry");

        Assert.NotNull(workflows);
        Assert.Equal(4, workflows.Length);
        Assert.Contains(workflows, workflow => workflow.Name == "Capture product audit snapshot" && workflow.EventName == WorkflowEventName.Manual);
        Assert.Contains(workflows, workflow => workflow.Name == "Reject invalid product price" && workflow.EventName == WorkflowEventName.BeforeSave);
        Assert.Contains(workflows, workflow => workflow.Name == "Write log when product is created" && workflow.EventName == WorkflowEventName.Created);
        Assert.Contains(workflows, workflow => workflow.Name == "Write log when quantity is low" && workflow.EventName == WorkflowEventName.FieldChanged);
    }

    [Fact]
    public async Task Hero_workflow_executes_audit_snapshot_flow()
    {
        using var factory = new MetaRecordWebApiFactory();
        var client = factory.CreateClient();

        var workflows = await client.GetFromJsonAsync<WorkflowDefinition[]>("/api/workflows", JsonOptions);
        var workflow = Assert.Single(workflows!.Where(workflow => workflow.Name == "Capture product audit snapshot"));

        var runResponse = await client.PostAsJsonAsync($"/api/workflows/{workflow.Id}/test-run", new
        {
            currentRecord = new
            {
                id = Guid.NewGuid(),
                name = "Widget",
                price = 9.99m,
                quantity = 5
            }
        });
        var run = await runResponse.Content.ReadFromJsonAsync<WorkflowTestRunResponse>(JsonOptions);

        runResponse.EnsureSuccessStatusCode();
        Assert.NotNull(run);
        Assert.Equal(WorkflowRunStatus.Succeeded, run.Status);
        Assert.Contains(run.Steps, step => step.NodeType == "action.create-record");
        Assert.Contains(run.Steps, step => step.NodeType == "action.write-log");
        Assert.Contains(run.Steps, step => step.NodeType == "flow.stop");

        var history = await client.GetFromJsonAsync<WorkflowRunSummaryResponse[]>($"/api/workflows/{workflow.Id}/runs", JsonOptions);
        Assert.NotNull(history);
        Assert.Single(history);
    }

    [Fact]
    public async Task Create_workflow_rejects_invalid_definition()
    {
        using var factory = new MetaRecordWebApiFactory();
        var client = factory.CreateClient();
        var workflow = CreateManualLogWorkflow(objectName: "MissingObject");

        var response = await client.PostAsJsonAsync("/api/workflows", workflow);
        var validation = await response.Content.ReadFromJsonAsync<WorkflowValidationResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(validation);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue => issue.Message.Contains("MissingObject"));
    }

    [Fact]
    public async Task Test_run_executes_saved_workflow_without_enabling_it()
    {
        using var factory = new MetaRecordWebApiFactory();
        var client = factory.CreateClient();
        var workflow = CreateManualLogWorkflow(isEnabled: false);

        var createResponse = await client.PostAsJsonAsync("/api/workflows", workflow);
        createResponse.EnsureSuccessStatusCode();

        var runResponse = await client.PostAsJsonAsync($"/api/workflows/{workflow.Id}/test-run", new
        {
            currentRecord = new
            {
                id = Guid.NewGuid(),
                name = "Widget",
                price = 9.99m,
                quantity = 5
            }
        });
        var run = await runResponse.Content.ReadFromJsonAsync<WorkflowTestRunResponse>(JsonOptions);

        runResponse.EnsureSuccessStatusCode();
        Assert.NotNull(run);
        Assert.Equal(WorkflowRunStatus.Succeeded, run.Status);
        Assert.Contains(run.Steps, step => step.NodeType == "action.write-log");

        var history = await client.GetFromJsonAsync<WorkflowRunSummaryResponse[]>($"/api/workflows/{workflow.Id}/runs", JsonOptions);
        Assert.NotNull(history);
        Assert.Single(history);
    }

    private static WorkflowDefinition CreateManualLogWorkflow(
        string objectName = "Product",
        bool isEnabled = true) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Manual log workflow",
        ObjectName = objectName,
        EventName = WorkflowEventName.Manual,
        IsEnabled = isEnabled,
        Nodes = new[]
        {
            Node("trigger-1", "trigger.manual", "{}"),
            Node("log-1", "action.write-log", """
            {
              "severity": "Information",
              "message": "Manual run for {{currentRecord.name}}."
            }
            """)
        },
        Edges = new[]
        {
            new WorkflowEdge
            {
                Id = "edge-1",
                FromNodeId = "trigger-1",
                FromPort = "success",
                ToNodeId = "log-1",
                ToPort = "input"
            }
        }
    };

    private static WorkflowNode Node(string id, string type, string configJson) => new()
    {
        Id = id,
        Type = type,
        Config = JsonDocument.Parse(configJson).RootElement.Clone()
    };

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