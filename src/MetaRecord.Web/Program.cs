using System.Text.Json.Serialization;
using MetaRecord.Data;
using MetaRecord.Services;
using MetaRecord.Web.Endpoints;
using MetaRecord.Web.Infrastructure;
using MetaRecord.Workflows.Catalog;
using MetaRecord.Workflows.Persistence;
using MetaRecord.Workflows.Runtime;
using MetaRecord.Workflows.Validation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var dbPath = builder.Configuration["MetaRecord:DbPath"] ??
    Environment.GetEnvironmentVariable("METARECORD_DB_PATH") ??
    Path.Join(Environment.CurrentDirectory, "metarecord.db");

builder.Services.AddScoped(_ => new MetaRecordDbContext(dbPath));
builder.Services.AddSingleton(_ => new EntityStore(dbPath));
builder.Services.AddSingleton(WorkflowNodeCatalog.Default);
builder.Services.AddSingleton(services => new WorkflowValidator(services.GetRequiredService<WorkflowNodeCatalog>()));
builder.Services.AddScoped<WorkflowRepository>();
builder.Services.AddScoped<IWorkflowEngine>(services =>
{
    var entityStore = services.GetRequiredService<EntityStore>();
    var repository = services.GetRequiredService<WorkflowRepository>();
    return new WorkflowEngine(WorkflowRuntime.CreateDefaultExecutors(entityStore), repository);
});

var app = builder.Build();

await app.InitializeMetaRecordAsync();

app.MapGet("/", () => Results.Ok(new { name = "MetaRecord Web API", status = "Running" }));
app.MapMetadataEndpoints();
app.MapWorkflowEndpoints();
app.MapWorkflowRunEndpoints();

await app.RunAsync();