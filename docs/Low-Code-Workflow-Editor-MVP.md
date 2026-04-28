# Low-Code Workflow Editor MVP Plan

This document outlines an MVP for a visual, metadata-driven workflow editor inspired by tools like n8n, but scoped to MetaRecord's core strength: runtime object metadata, predefined behavior blocks, and event-driven actions.

The MVP should let a user create workflows without writing code by selecting predefined nodes, wiring object events to actions, configuring each node through forms, validating the result, saving it, and running it when records change.

Companion implementation plan: [Low-Code-Workflow-MVP-Implementation-Plan.md](Low-Code-Workflow-MVP-Implementation-Plan.md).

## Product Goal

Build a low-code workflow editor where users can define behavior around metadata-backed objects.

Example workflows:

- When a `Product` is created, write an audit entry.
- When `Product.Quantity` drops below `10`, create a restock task.
- Before saving a `Product`, reject the change if `Price <= 0`.
- When `Product.Price` changes, log the old and new values.

The MVP is not a full n8n clone. It is a smaller, safer foundation that proves the editor, definition format, runtime, and event-to-action execution model.

## MVP Statement

A user can:

1. Open a workflow list.
2. Create a new workflow for a MetaRecord object.
3. Choose one trigger event, such as `Product.Created` or `Product.BeforeSave`.
4. Add predefined action and condition nodes from a palette.
5. Wire nodes on a visual canvas.
6. Configure node properties through forms generated from node schemas.
7. Validate the workflow before saving.
8. Enable or disable the workflow.
9. Execute the workflow when the matching event occurs.
10. Inspect workflow run history and step-level errors.

## Target Users

### Builder

The builder is a power user, admin, or developer-adjacent operator who understands the application's business rules but should not need to write C#.

Needs:

- Create simple event handlers quickly.
- See available object fields.
- Configure actions through forms.
- Understand validation errors before enabling a workflow.

### Operator

The operator monitors workflow behavior.

Needs:

- See whether workflows are enabled.
- Inspect recent runs.
- Understand why a workflow failed.
- Disable a broken workflow without changing code.

### Developer

The developer maintains the predefined node catalog and runtime.

Needs:

- Add new node types safely.
- Version workflow definitions.
- Keep metadata, node configuration, and runtime execution in sync.

## MVP Scope

### In Scope

- Visual workflow editor with a node canvas.
- Predefined trigger, condition, and action nodes.
- Single trigger per workflow.
- Directed graph execution from trigger to connected actions.
- Basic branching through a `Condition` node.
- Configuration forms generated from node type schemas.
- Object and property pickers based on `IObjectMetadata`.
- Save/load workflow definitions as JSON in SQLite.
- Enable/disable workflows.
- Runtime execution for record lifecycle events.
- Run history with status, duration, error message, and per-node results.
- Basic validation before save and before enable.
- Safe expression support through structured condition builders, not arbitrary code.

### Out of Scope for MVP

- Arbitrary C# or JavaScript scripting.
- Public plugin marketplace.
- Multi-user real-time collaboration.
- Long-running distributed workers.
- Secrets vault and external credential management.
- Schedules, webhooks, and external triggers.
- Complex loops, joins, parallel branches, retries, and compensation.
- Dragging arbitrary UI components onto application screens.
- Full RBAC and approval workflows.
- Cross-tenant workflow sharing.

These can be added later, but excluding them keeps the first version focused and safer.

## Relationship to MetaRecord

MetaRecord already has a metadata-driven object model:

- `ObjectMetadata` describes an object such as `Product`.
- `PropertyMetadata` describes fields such as `Name`, `Price`, and `Quantity`.
- `MetadataRegistry` keeps the runtime metadata cache.
- `EntityStore` persists records based on metadata.

The workflow editor should extend that idea from object shape to object behavior.

Current model:

```text
Object metadata -> dynamic persistence
```

MVP extension:

```text
Object metadata + workflow metadata -> dynamic persistence + dynamic behavior
```

This keeps the system coherent: users define object structure and object behavior as metadata.

## High-Level Architecture

```text
Browser Editor
  |-- Workflow List
  |-- Visual Canvas
  |-- Node Palette
  |-- Property Inspector
  |-- Run History
        |
        v
Workflow API
  |-- Workflow definitions
  |-- Node type catalog
  |-- Validation
  |-- Run history
        |
        v
Workflow Runtime
  |-- Event dispatcher
  |-- Graph executor
  |-- Node executors
  |-- Expression evaluator
        |
        v
MetaRecord Core
  |-- MetadataRegistry
  |-- EntityStore
  |-- ActiveRecord<T>
  |-- SQLite metadata/data store
```

For the current console demo, this likely means adding a web host later rather than forcing the editor into the console project. A natural evolution would be:

```text
MetaRecord.Core        -> current metadata, persistence, and runtime concepts
MetaRecord.Web         -> ASP.NET Core API and editor hosting
MetaRecord.Editor      -> React/TypeScript visual editor
```

For the MVP, these can still live in one solution as separate projects or folders.

## Build Plan for the Current Repo

The current repository is small enough that the workflow engine should start inside the existing .NET project, then split into separate projects only after the runtime contract is proven.

### Current Structure Observed

The repo currently has documentation at the root and the runnable .NET project under `src`:

```text
MetaRecord/
  README.md
  docs/
    ActiveRecord-vs-Repository.md
    Low-Code-Workflow-Editor-MVP.md
    Schema-Drift-and-Guard-Rails.md
  src/
    MetaRecord.sln
    MetaRecord.csproj
    Program.cs
    Data/
    Models/
    Services/
```

The current solution contains one project:

| Item | Current State |
|---|---|
| Solution | `src/MetaRecord.sln` |
| Project | `src/MetaRecord.csproj` |
| Output type | Console executable |
| Target framework | `net10.0` |
| Persistence dependencies | EF Core SQLite and EF Core Design |
| Metadata persistence | `MetaRecordDbContext` for object/property metadata |
| Entity persistence | `EntityStore` using raw SQLite SQL from metadata |
| Runtime entry point | `Program.cs` demo |
| Tests | None yet |
| Web/API host | None yet |
| Frontend app | None yet |

This is a good starting point for the workflow runtime, but not yet for a visual editor. The first step should be backend/runtime proof, not React.

### Repository Housekeeping Before Implementation

Before adding workflow code, do these small cleanup/setup tasks:

1. Add a root-level `.gitignore` or move the existing `src/.gitignore` to the repo root.
2. Ignore root-level `bin/`, `obj/`, `*.db`, and editor folders from the repository root.
3. Decide whether the solution should stay under `src/` or move to the repo root.
4. Prefer a root solution once multiple projects exist, because it will naturally include `src/*` and `tests/*` projects.

Recommended near-term command shape if the solution stays where it is:

```bash
dotnet build src/MetaRecord.sln
dotnet run --project src/MetaRecord.csproj
```

Recommended longer-term command shape after a root solution is introduced:

```bash
dotnet build MetaRecord.sln
dotnet test MetaRecord.sln
dotnet run --project src/MetaRecord.Web/MetaRecord.Web.csproj
```

### Recommended Implementation Strategy

Build the workflow system in two structural stages.

Stage 1 should keep the current single .NET project and add workflow runtime code under `src/Workflows`. This keeps the first prototype simple and avoids project churn while the model is still forming.

Stage 2 should split the solution after the runtime works. At that point, the project should become a small platform with a core library, a web host, an editor app, and tests.

### Stage 1: Add Workflows Inside the Existing Project

Add these folders to `src/`:

```text
src/
  Workflows/
    Definitions/
      WorkflowDefinition.cs
      WorkflowNode.cs
      WorkflowEdge.cs
      WorkflowPosition.cs
      WorkflowEventName.cs
      WorkflowRunStatus.cs
    Catalog/
      WorkflowNodeType.cs
      WorkflowNodeCatalog.cs
      NodeConfigSchema.cs
      NodePortDefinition.cs
    Validation/
      WorkflowValidator.cs
      WorkflowValidationIssue.cs
      WorkflowValidationSeverity.cs
    Runtime/
      WorkflowEvent.cs
      WorkflowExecutionContext.cs
      WorkflowEngine.cs
      WorkflowRunResult.cs
      NodeExecutionResult.cs
      IWorkflowNodeExecutor.cs
      IWorkflowEventDispatcher.cs
    Runtime/Executors/
      ConditionNodeExecutor.cs
      SetFieldNodeExecutor.cs
      RejectSaveNodeExecutor.cs
      CreateRecordNodeExecutor.cs
      WriteLogNodeExecutor.cs
    Persistence/
      WorkflowDefinitionEntity.cs
      WorkflowRunEntity.cs
      WorkflowRunStepEntity.cs
      WorkflowRepository.cs
```

This keeps all workflow code isolated while still letting it use the current `MetadataRegistry`, `MetaRecordDbContext`, and `EntityStore`.

#### Stage 1 Milestone A: Definition Model

Add plain C# models that represent the JSON graph:

- `WorkflowDefinition`
- `WorkflowNode`
- `WorkflowEdge`
- `WorkflowPosition`
- `WorkflowEventName`
- `WorkflowRunStatus`

Keep these models independent from EF Core. EF entities should live separately in `Workflows/Persistence` so the runtime can operate on clean definition objects.

Recommended definition shape:

```csharp
public sealed class WorkflowDefinition
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string ObjectName { get; init; }
    public required string EventName { get; init; }
    public bool IsEnabled { get; init; }
    public int Version { get; init; }
    public required IReadOnlyList<WorkflowNode> Nodes { get; init; }
    public required IReadOnlyList<WorkflowEdge> Edges { get; init; }
}
```

Use `JsonElement` or `Dictionary<string, object?>` for node config at first. Prefer `JsonElement` if validation will read strongly from JSON; prefer dictionaries if the MVP needs easier test setup.

#### Stage 1 Milestone B: Node Catalog

Add a code-defined catalog of allowed node types. Do not store node type definitions in the database for the MVP.

Recommended initial catalog:

| Type | Category | Executor |
|---|---|---|
| `trigger.record-created` | Trigger | None, used as graph start. |
| `trigger.record-updated` | Trigger | None, used as graph start. |
| `trigger.before-save` | Trigger | None, used as graph start. |
| `trigger.field-changed` | Trigger | None, used as graph start. |
| `flow.condition` | Flow | `ConditionNodeExecutor` |
| `flow.stop` | Flow | `StopNodeExecutor` |
| `action.set-field` | Action | `SetFieldNodeExecutor` |
| `action.reject-save` | Action | `RejectSaveNodeExecutor` |
| `action.create-record` | Action | `CreateRecordNodeExecutor` |
| `action.write-log` | Action | `WriteLogNodeExecutor` |

The catalog should expose both backend validation data and frontend schema data later. That prevents the editor and runtime from drifting.

#### Stage 1 Milestone C: Validation

Add `WorkflowValidator` before adding execution. This gives a hard gate before definitions can run.

Validation needs access to:

- `WorkflowDefinition`
- `WorkflowNodeCatalog`
- `MetadataRegistry`

Minimum validations for this repo:

- The workflow object exists in `MetadataRegistry`.
- Exactly one trigger node exists.
- The trigger node type matches `WorkflowDefinition.EventName`.
- Every node type exists in `WorkflowNodeCatalog`.
- Every edge references existing node ids.
- Ports are valid for the source and target node types.
- The graph has no cycles.
- Every non-trigger node is reachable from the trigger.
- Node configs reference valid object and property names.
- Before-only nodes are used only with `BeforeSave`.

Validation should return all issues at once instead of failing on the first error.

#### Stage 1 Milestone D: Persistence Tables

Extend `MetaRecordDbContext` with workflow tables, but keep workflow definitions stored as JSON plus queryable columns.

Add DbSets:

```csharp
public DbSet<WorkflowDefinitionEntity> WorkflowDefinitions => Set<WorkflowDefinitionEntity>();
public DbSet<WorkflowRunEntity> WorkflowRuns => Set<WorkflowRunEntity>();
public DbSet<WorkflowRunStepEntity> WorkflowRunSteps => Set<WorkflowRunStepEntity>();
```

Add EF entities under `src/Workflows/Persistence` rather than `src/Data`. The current `Data` folder can remain focused on metadata and entity persistence.

Configure tables in `OnModelCreating`:

```text
WorkflowDefinitions
WorkflowRuns
WorkflowRunSteps
```

SQLite does not enforce schemas the same way SQL Server does, so avoid relying on EF schema names for isolation. Table names alone are enough for the MVP.

#### Stage 1 Milestone E: Runtime Engine

Add `WorkflowEngine` after validation and persistence.

The engine should:

1. Find the trigger node.
2. Traverse outgoing edges.
3. Execute each reachable node through `IWorkflowNodeExecutor`.
4. Record a `WorkflowRunEntity`.
5. Record one `WorkflowRunStepEntity` per executed node.
6. Stop branches when a node fails or returns a stop result.

Keep execution single-process and in-memory for the MVP. A background queue can come later.

#### Stage 1 Milestone F: Console Demo Workflow

Before adding a web API or editor, update the console demo to seed and run one workflow.

Example demo path:

1. Initialize metadata.
2. Seed a sample `BeforeSave` workflow that rejects products with `Price <= 0`.
3. Seed a sample `FieldChanged` workflow that logs when `Quantity < 10`.
4. Save a valid product.
5. Attempt to save an invalid product and show the workflow rejection.
6. Update quantity and show a workflow run record.

This proves the runtime without needing browser code.

### Stage 2: Split Into Projects After Runtime Proof

Once Stage 1 works, move toward this structure:

```text
MetaRecord/
  MetaRecord.sln
  docs/
  src/
    MetaRecord.Core/
      Data/
      Models/
      Services/
      Workflows/
    MetaRecord.Console/
      Program.cs
    MetaRecord.Web/
      Program.cs
      Endpoints/
      wwwroot/
    MetaRecord.Editor/
      package.json
      src/
  tests/
    MetaRecord.Core.Tests/
```

Suggested responsibilities:

| Project | Responsibility |
|---|---|
| `MetaRecord.Core` | Metadata model, entity persistence, workflow definitions, validation, runtime, repositories. |
| `MetaRecord.Console` | Demo runner and development smoke tests. |
| `MetaRecord.Web` | ASP.NET Core API, static editor hosting, dependency injection, app startup. |
| `MetaRecord.Editor` | React/TypeScript visual workflow editor. |
| `MetaRecord.Core.Tests` | Unit and runtime tests. |

The split should happen after the runtime shape stabilizes because project boundaries are expensive to keep changing while core abstractions are still moving.

### Current Code Integration Points

#### `ActiveRecord<T>.Save()`

This is the main lifecycle hook. It currently decides whether to insert or update and calls `EntityStore` directly.

For the MVP, add workflow dispatch around the existing persistence call:

```text
Save()
  -> build before-save context
  -> run BeforeSave workflows
  -> stop if rejected
  -> insert or update through EntityStore
  -> build after-save context
  -> run Created, Updated, and FieldChanged workflows
```

The current `Save()` method is synchronous. There are two possible paths:

| Path | Pros | Cons | Recommendation |
|---|---|---|---|
| Add `SaveAsync()` and keep `Save()` as a wrapper | Clean long-term API; web host can use async naturally. | Touches call sites and requires async runtime from the start. | Best long-term path. |
| Keep `Save()` only and run workflow dispatch synchronously | Smallest first change. | Easy to block on async and harder to host cleanly later. | Acceptable only for a throwaway demo. |

Recommended MVP path: add `SaveAsync()` first, then make `Save()` call `SaveAsync().GetAwaiter().GetResult()` for console compatibility.

#### `EntityStore`

`EntityStore` currently handles table creation, insert, update, delete, find, all, and count. It should remain the low-level persistence adapter.

Workflow actions need a slightly more dynamic API than the current generic methods provide:

- Read current record values as a dictionary by object name and record id.
- Insert a record from metadata and a dictionary of values.
- Update a record from metadata and a dictionary of values.
- Compare original and current values for field-changed workflows.

Add these methods alongside the existing generic API:

```csharp
public Dictionary<string, object?> FindValues(IObjectMetadata metadata, Guid id);
public void InsertValues(IObjectMetadata metadata, IReadOnlyDictionary<string, object?> values);
public void UpdateValues(IObjectMetadata metadata, Guid id, IReadOnlyDictionary<string, object?> values);
```

These methods let workflow actions operate on metadata-backed objects without needing a CLR type for every object.

#### `MetaRecordDbContext`

This is the right place to add workflow definition and run-history EF entities. Keeping one SQLite database is fine for the MVP.

Later, if runtime load grows, workflow run history can move to a separate store or retention policy.

#### `MetadataRegistry`

The editor and validator should use `MetadataRegistry.GetAll()` and `MetadataRegistry.TryGetMetadata(...)` for object/property pickers and validation.

Because the registry is static today, the web host should initialize it once on startup. Later, if metadata editing becomes dynamic, the workflow validator must respond to metadata version changes.

### API and Editor Placement in This Repo

Do not add the React editor until the runtime has a working console/API proof.

When ready, add `MetaRecord.Web` as an ASP.NET Core project. It should reference `MetaRecord.Core` after the project split, or reference the existing project only temporarily if the split has not happened yet.

Recommended API endpoint folders:

```text
src/MetaRecord.Web/
  Endpoints/
    MetadataEndpoints.cs
    WorkflowEndpoints.cs
    WorkflowRunEndpoints.cs
  Services/
    WorkflowEditorAppSettings.cs
```

Recommended editor folder:

```text
src/MetaRecord.Editor/
  package.json
  src/
    api/
    components/
    workflow/
      WorkflowCanvas.tsx
      NodePalette.tsx
      PropertyInspector.tsx
      ValidationPanel.tsx
      RunHistoryPanel.tsx
```

For deployment simplicity, the web project can serve the built editor from `wwwroot` later.

### Test Project Plan

Add tests before the visual editor.

Recommended test project:

```text
tests/
  MetaRecord.Core.Tests/
    MetaRecord.Core.Tests.csproj
    Workflows/
      WorkflowValidatorTests.cs
      WorkflowEngineTests.cs
      ConditionEvaluatorTests.cs
      TemplateResolverTests.cs
```

The first useful tests should focus on the parts that are easiest to break:

- Validator rejects missing object references.
- Validator rejects missing field references.
- Validator rejects graph cycles.
- `BeforeSave` workflow can reject a save.
- `FieldChanged` workflow fires only when the configured field changes.
- Run history records failed node id and message.

### Recommended Build Order in This Repo

```text
0. Repo hygiene
   -> root .gitignore, ignore generated files, confirm solution location

1. Workflow definition models
   -> pure C# graph model under src/Workflows/Definitions

2. Node catalog
   -> hard-coded predefined node types under src/Workflows/Catalog

3. Validator
   -> graph + metadata + node config validation

4. Persistence
   -> WorkflowDefinitions, WorkflowRuns, WorkflowRunSteps in MetaRecordDbContext

5. Runtime engine
   -> graph traversal and node executor pipeline

6. EntityStore dynamic value APIs
   -> dictionary-based read/insert/update for workflow actions

7. ActiveRecord lifecycle dispatch
   -> BeforeSave, Created, Updated, FieldChanged hooks

8. Console demo workflows
   -> prove runtime behavior without a web UI

9. Test project
   -> lock down validator and runtime behavior

10. Project split
   -> Core, Console, Web, Editor, Tests

11. ASP.NET Core API
   -> metadata, workflows, validation, test-run, run history

12. React editor
   -> visual canvas over the working API/runtime
```

### Why This Order Fits the Current Repo

The current app is a console demo with static registries and direct persistence calls. That is perfectly fine for proving workflow behavior, but it is not yet shaped like a hosted low-code platform.

Starting with the runtime inside the existing project has three advantages:

- It reuses `MetadataRegistry`, `MetaRecordDbContext`, and `EntityStore` immediately.
- It avoids introducing a web app before the workflow contract is stable.
- It gives the later React editor a real API/runtime to author against instead of a mock graph format.

The main thing to avoid is starting with the canvas. A canvas can make invalid definitions quickly unless the backend definition model, validator, and runtime already know what a valid workflow means.

## Core Concepts

### Workflow Definition

A saved workflow is metadata describing a graph of nodes and edges.

Key fields:

| Field | Purpose |
|---|---|
| `Id` | Stable workflow identifier. |
| `Name` | Human-readable workflow name. |
| `ObjectName` | Target MetaRecord object, such as `Product`. |
| `EventName` | Trigger event, such as `Created`, `BeforeSave`, or `FieldChanged`. |
| `IsEnabled` | Whether runtime should execute it. |
| `Version` | Workflow definition version. |
| `Nodes` | Node instances on the canvas. |
| `Edges` | Connections between node ports. |
| `CreatedAt` / `UpdatedAt` | Audit fields. |

### Node Type

A node type is a predefined capability in the system.

Example node types:

- `trigger.record-created`
- `trigger.before-save`
- `flow.condition`
- `action.set-field`
- `action.create-record`
- `action.write-log`
- `action.reject-save`

Each node type defines:

| Field | Purpose |
|---|---|
| `Type` | Stable type key. |
| `Category` | Trigger, Flow, or Action. |
| `DisplayName` | Label shown in the palette. |
| `Description` | Short help text for builders. |
| `InputPorts` | Named incoming connections. |
| `OutputPorts` | Named outgoing connections. |
| `ConfigSchema` | JSON schema-like definition for the property inspector. |
| `Executor` | Runtime handler that executes the node. |

### Node Instance

A node instance is one configured node in one workflow.

Key fields:

| Field | Purpose |
|---|---|
| `Id` | Unique node id inside the workflow. |
| `Type` | References a node type. |
| `Label` | Optional user-defined label. |
| `Position` | Canvas x/y location. |
| `Config` | Node-specific configuration JSON. |

### Edge

An edge connects an output port on one node to an input port on another node.

Key fields:

| Field | Purpose |
|---|---|
| `Id` | Stable edge id. |
| `FromNodeId` | Source node instance. |
| `FromPort` | Source output port, such as `success`, `true`, or `false`. |
| `ToNodeId` | Target node instance. |
| `ToPort` | Target input port, usually `input`. |

### Execution Context

The execution context is the data available while a workflow runs.

For a record event, it should include:

| Field | Purpose |
|---|---|
| `WorkflowId` | Workflow being executed. |
| `RunId` | Current run identifier. |
| `ObjectName` | Object that raised the event. |
| `EventName` | Event that raised the workflow. |
| `RecordId` | Record id, when available. |
| `CurrentRecord` | Field values after the change. |
| `OriginalRecord` | Field values before the change, when available. |
| `ChangedFields` | List of changed fields, when available. |
| `Variables` | Runtime values produced by nodes. |

## Visual Workflow Editor

The editor should have five main areas.

```text
+----------------------------------------------------------------+
| Top Bar: workflow name, status, validate, save, enable, run     |
+-------------------+------------------------------+-------------+
| Node Palette       | Canvas                       | Inspector   |
| - Triggers         | - Nodes                      | - Config    |
| - Flow             | - Edges                      | - Errors    |
| - Actions          | - Selection                  | - Metadata  |
+-------------------+------------------------------+-------------+
| Bottom Panel: validation results, recent runs, selected run log |
+----------------------------------------------------------------+
```

### Workflow List

The workflow list is the entry point.

Required columns:

- Name
- Target object
- Trigger event
- Enabled status
- Last run status
- Last modified date

Required actions:

- Create workflow
- Open workflow
- Duplicate workflow
- Enable/disable workflow
- Delete workflow

### Create Workflow Dialog

The create dialog should gather the minimum information needed to start.

Fields:

- Workflow name
- Target object, populated from `MetadataRegistry.GetAll()`
- Trigger event

After creation, the canvas should open with the trigger node already placed.

### Node Palette

The palette lists node types the user is allowed to add.

MVP categories:

- Triggers
- Flow
- Actions

The palette should filter actions based on the selected workflow object and trigger timing.

Examples:

- `Reject Save` should be available for `BeforeSave`, not `Created`.
- `Set Field` should be available before persistence events.
- `Write Log` should be available for all events.

### Canvas

The canvas should support:

- Drag node from palette.
- Move nodes.
- Connect compatible ports.
- Delete node or edge.
- Select node to edit configuration.
- Show validation badges on invalid nodes.
- Fit graph to view.
- Pan and zoom.

Recommended library:

- React Flow for the visual graph editor.

### Property Inspector

The inspector renders configuration forms from the selected node type's schema.

Example fields:

- Field picker
- Literal value input
- Comparison operator picker
- Target object picker
- Target property mapping grid
- Checkbox/toggle for optional behavior

The inspector should not expose raw JSON in the MVP. Raw JSON editing is useful later for developers, but it increases the chance of invalid definitions.

### Bottom Panel

Tabs:

- Validation
- Test Input
- Recent Runs
- Step Output

MVP behavior:

- Validation tab shows graph and configuration errors.
- Test Input lets a builder run the workflow with sample record values.
- Recent Runs shows the last N runs.
- Step Output shows the selected run's node-by-node details.

## Predefined Node Catalog

### Trigger Nodes

| Node | Event | Purpose | MVP Notes |
|---|---|---|---|
| Record Created | `Created` | Runs after a new record is saved. | Good first trigger. |
| Record Updated | `Updated` | Runs after an existing record is saved. | Include changed fields when possible. |
| Before Save | `BeforeSave` | Runs before create or update is committed. | Can reject save or set fields. |
| Field Changed | `FieldChanged` | Runs when a specific field changes. | Config requires field name. |
| Manual Run | `Manual` | Runs from the editor for testing. | Useful for debugging. |

### Flow Nodes

| Node | Purpose | Config | Ports |
|---|---|---|---|
| Condition | Branch based on a structured condition. | Field, operator, value. | `true`, `false` |
| Stop | Ends the workflow early. | Optional reason. | None |

Only `Condition` and `Stop` are needed for the MVP. More advanced flow control can wait.

### Action Nodes

| Node | Purpose | Config | Trigger Timing |
|---|---|---|---|
| Set Field | Updates a field on the current record. | Field, value expression. | Before events only. |
| Reject Save | Cancels the current save. | Error message. | Before events only. |
| Create Record | Creates another metadata-backed record. | Target object, field mappings. | Any event. |
| Update Record | Updates an existing record. | Target object, id expression, field mappings. | Any event. |
| Write Log | Writes a structured workflow log entry. | Message template, severity. | Any event. |
| Raise Event | Emits an internal event for another workflow. | Event name, payload. | Any event. |

Optional later additions:

- Send email
- HTTP request
- Webhook response
- Schedule trigger
- AI-assisted action generation
- External integration nodes

## Low-Code Event Handler Model

In the MVP, every workflow is an event handler.

```text
Event -> Trigger Node -> Flow Nodes -> Action Nodes
```

### Supported Events

| Event | Timing | Can Modify Current Record | Can Cancel Operation | Typical Use |
|---|---|---:|---:|---|
| `BeforeSave` | Before insert/update | Yes | Yes | Validation, defaulting. |
| `Created` | After insert | No | No | Audit, follow-up records. |
| `Updated` | After update | No | No | Notifications, audit. |
| `FieldChanged` | After update | No | No | Field-specific automation. |
| `Manual` | Editor/test only | No | No | Testing. |

This separation is important. Before-events are allowed to change or reject the record. After-events should be side effects only because the data has already been committed.

### Event Dispatch Flow

```text
ActiveRecord<T>.Save()
  -> Build RecordChangeContext
  -> Run BeforeSave workflows
       -> may set fields
       -> may reject save
  -> Persist through EntityStore
  -> Run Created / Updated / FieldChanged workflows
       -> may create/update related records
       -> may write logs
```

### Field Changed Detection

For `FieldChanged`, the runtime needs both original and current values.

Possible MVP approach:

1. Load the existing record before update.
2. Compare metadata-defined properties.
3. Build `ChangedFields` with `FieldName`, `OldValue`, and `NewValue`.
4. Dispatch field-specific workflows only when their configured field changed.

## Condition Builder

Avoid arbitrary code in the MVP. Use a structured condition builder.

Supported operands:

- Current record field
- Original record field
- Event field, such as event name or object name
- Literal value
- Node output variable

Supported operators:

- equals
- not equals
- greater than
- greater than or equal
- less than
- less than or equal
- contains
- starts with
- ends with
- is empty
- is not empty

Example condition JSON:

```json
{
  "left": { "source": "currentRecord", "field": "Quantity" },
  "operator": "lessThan",
  "right": { "source": "literal", "value": 10 }
}
```

Later, this could evolve into a more expressive rule language, but the MVP should stay form-driven.

## Workflow Definition Example

Example: when a product's quantity drops below 10, create a restock task and write a log entry.

```json
{
  "id": "8f8f0b8d-7c50-4b14-9709-5f92f2d62f01",
  "name": "Create restock task when inventory is low",
  "objectName": "Product",
  "eventName": "FieldChanged",
  "isEnabled": true,
  "version": 1,
  "nodes": [
    {
      "id": "trigger-1",
      "type": "trigger.field-changed",
      "position": { "x": 100, "y": 120 },
      "config": {
        "fieldName": "Quantity"
      }
    },
    {
      "id": "condition-1",
      "type": "flow.condition",
      "position": { "x": 360, "y": 120 },
      "config": {
        "condition": {
          "left": { "source": "currentRecord", "field": "Quantity" },
          "operator": "lessThan",
          "right": { "source": "literal", "value": 10 }
        }
      }
    },
    {
      "id": "create-task-1",
      "type": "action.create-record",
      "position": { "x": 640, "y": 80 },
      "config": {
        "targetObjectName": "Task",
        "fieldMappings": {
          "Title": "Restock {{currentRecord.Name}}",
          "RelatedProductId": "{{currentRecord.Id}}",
          "Priority": "High"
        }
      }
    },
    {
      "id": "log-1",
      "type": "action.write-log",
      "position": { "x": 640, "y": 220 },
      "config": {
        "severity": "Information",
        "message": "Product {{currentRecord.Name}} inventory dropped to {{currentRecord.Quantity}}."
      }
    }
  ],
  "edges": [
    {
      "id": "edge-1",
      "fromNodeId": "trigger-1",
      "fromPort": "success",
      "toNodeId": "condition-1",
      "toPort": "input"
    },
    {
      "id": "edge-2",
      "fromNodeId": "condition-1",
      "fromPort": "true",
      "toNodeId": "create-task-1",
      "toPort": "input"
    },
    {
      "id": "edge-3",
      "fromNodeId": "condition-1",
      "fromPort": "true",
      "toNodeId": "log-1",
      "toPort": "input"
    }
  ]
}
```

## Runtime Design

### Runtime Interfaces

The runtime can be modeled around a small set of interfaces.

```csharp
public interface IWorkflowEventDispatcher
{
    Task DispatchAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken = default);
}

public interface IWorkflowEngine
{
    Task<WorkflowRunResult> RunAsync(
        WorkflowDefinition workflow,
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default);
}

public interface IWorkflowNodeExecutor
{
    string NodeType { get; }

    Task<NodeExecutionResult> ExecuteAsync(
        WorkflowNode node,
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default);
}
```

### Execution Rules

MVP execution should be intentionally simple:

- Exactly one trigger node per workflow.
- Trigger node is the only node with no incoming edge.
- Graph must be acyclic.
- Each action node can execute at most once per run.
- `Condition` can choose one or more outgoing branches by port.
- Node failures stop the current branch.
- Workflow run status is `Succeeded`, `Failed`, `Canceled`, or `Skipped`.

### Before Event Failure Behavior

For `BeforeSave` workflows:

- `Reject Save` should stop execution and return a user-facing validation error.
- Unexpected node failures should block the save by default.
- The error should include the workflow name and node label.

For after-events:

- Failures should be logged in run history.
- The original data change should remain committed.
- Later retry support can be added outside the MVP.

## Persistence Model

The simplest MVP storage approach is to store workflow definitions as JSON plus indexed columns for common queries.

### `WorkflowDefinitions`

| Column | Type | Notes |
|---|---|---|
| `Id` | Guid | Primary key. |
| `Name` | Text | Required. |
| `ObjectName` | Text | Indexed. |
| `EventName` | Text | Indexed. |
| `IsEnabled` | Boolean | Indexed. |
| `DefinitionJson` | Text | Full workflow graph. |
| `Version` | Integer | Incremented on save. |
| `DateCreated` | DateTime | UTC. |
| `DateModified` | DateTime | UTC. |

### `WorkflowRuns`

| Column | Type | Notes |
|---|---|---|
| `Id` | Guid | Primary key. |
| `WorkflowId` | Guid | Indexed. |
| `WorkflowVersion` | Integer | Version executed. |
| `ObjectName` | Text | Indexed. |
| `EventName` | Text | Indexed. |
| `RecordId` | Text | Optional. |
| `Status` | Text | Succeeded, Failed, Canceled, Skipped. |
| `StartedAt` | DateTime | UTC. |
| `CompletedAt` | DateTime | UTC nullable. |
| `DurationMs` | Integer | For quick display. |
| `ErrorMessage` | Text | Nullable. |

### `WorkflowRunSteps`

| Column | Type | Notes |
|---|---|---|
| `Id` | Guid | Primary key. |
| `RunId` | Guid | Indexed. |
| `NodeId` | Text | Node instance id. |
| `NodeType` | Text | Node type key. |
| `NodeLabel` | Text | Label at execution time. |
| `Status` | Text | Succeeded, Failed, Skipped. |
| `StartedAt` | DateTime | UTC. |
| `CompletedAt` | DateTime | UTC nullable. |
| `InputJson` | Text | Optional for debugging. |
| `OutputJson` | Text | Optional for debugging. |
| `ErrorMessage` | Text | Nullable. |

## Validation Rules

Validation should run in three places:

1. Live in the editor while the user works.
2. On save.
3. Before enabling a workflow.

Required validations:

- Workflow has exactly one trigger node.
- Trigger event matches workflow `EventName`.
- Node type exists in the node catalog.
- Every required config field is present.
- Config values have valid types.
- Referenced object names exist in metadata.
- Referenced property names exist in metadata.
- Edges connect compatible ports.
- Graph has no cycles.
- Every non-trigger node is reachable from the trigger.
- Before-only actions are not used in after-events.
- After-only actions are not used in before-events.
- Workflow definition version is supported by the runtime.

Validation output should be structured:

```json
{
  "severity": "Error",
  "nodeId": "condition-1",
  "field": "config.condition.left.field",
  "message": "Field 'Quantity' does not exist on object 'Product'."
}
```

This lets the editor highlight the exact node and inspector field.

## API Surface

MVP API endpoints could look like this:

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/metadata/objects` | List available object definitions. |
| `GET` | `/api/metadata/objects/{name}` | Get object properties. |
| `GET` | `/api/workflow-node-types` | List node catalog. |
| `GET` | `/api/workflows` | List workflows. |
| `POST` | `/api/workflows` | Create workflow. |
| `GET` | `/api/workflows/{id}` | Load workflow. |
| `PUT` | `/api/workflows/{id}` | Save workflow. |
| `POST` | `/api/workflows/{id}/validate` | Validate workflow. |
| `POST` | `/api/workflows/{id}/enable` | Enable workflow after validation. |
| `POST` | `/api/workflows/{id}/disable` | Disable workflow. |
| `POST` | `/api/workflows/{id}/test-run` | Run with test input. |
| `GET` | `/api/workflows/{id}/runs` | List runs. |
| `GET` | `/api/workflow-runs/{runId}` | Get run details and steps. |

## Suggested Technology Choices

### Frontend

- React
- TypeScript
- React Flow for canvas editing
- Zod or JSON Schema for client-side config validation
- A small state store such as Zustand, or local React state for the MVP

### Backend

- ASP.NET Core Minimal APIs or controllers
- EF Core with SQLite initially
- `System.Text.Json` for workflow definition serialization
- A server-side validator for graph and node config rules
- A runtime service invoked from `ActiveRecord<T>` or `EntityStore`

### Expression and Template Handling

For the MVP:

- Use structured condition JSON for comparisons.
- Use simple template tokens for messages and field mappings, such as `{{currentRecord.Name}}`.
- Do not evaluate arbitrary code.

Later:

- Consider a known expression engine with sandboxing.
- Add expression versioning and test cases per workflow.

## Build Phases

### Phase 1: Workflow Definition Foundation

Goal: Define the workflow metadata model and validate it without a UI.

Deliverables:

- `WorkflowDefinition`, `WorkflowNode`, `WorkflowEdge` models.
- Node type catalog in code.
- Server-side workflow validator.
- JSON serialization/deserialization.
- Unit tests for valid and invalid graphs.

Exit criteria:

- A workflow JSON file can be loaded, validated, and saved.
- Invalid references to object fields are caught.
- Invalid graph shapes are caught.

### Phase 2: Runtime Engine

Goal: Execute validated workflow definitions from code.

Deliverables:

- `WorkflowEngine`.
- Node executor abstraction.
- Executors for `Condition`, `Set Field`, `Reject Save`, `Create Record`, and `Write Log`.
- `WorkflowRun` and `WorkflowRunStep` persistence.
- Manual test-run support.

Exit criteria:

- A test workflow can run from a console or API call.
- Run history records success and failure.
- `Reject Save` can cancel a before-save workflow.

### Phase 3: Event Dispatch Integration

Goal: Connect workflows to MetaRecord record lifecycle events.

Deliverables:

- Record event model.
- Before/after event dispatch from save operations.
- Field-changed detection.
- Enabled workflow lookup by object and event.
- Error behavior for before and after events.

Exit criteria:

- Saving a `Product` can trigger an enabled workflow.
- Before-save workflows can modify or reject the record.
- After-save workflows can create a related record or write a log.

### Phase 4: Visual Editor MVP

Goal: Let users build workflows visually.

Deliverables:

- Workflow list screen.
- Create workflow dialog.
- React Flow canvas.
- Node palette.
- Property inspector generated from node schemas.
- Save, validate, enable, disable actions.
- Validation panel.

Exit criteria:

- A user can create, configure, wire, validate, save, and enable a workflow without editing JSON.

### Phase 5: Run History and Testing UX

Goal: Make workflows understandable and debuggable.

Deliverables:

- Test input panel.
- Recent runs list.
- Run detail view.
- Step-level input/output inspection.
- Clear error messages mapped back to nodes.

Exit criteria:

- A user can test a workflow before enabling it.
- A failed workflow run identifies the failed node and reason.

## MVP Milestone Estimate

Rough difficulty for one developer familiar with .NET and React:

| Milestone | Difficulty | Estimate |
|---|---:|---:|
| Workflow JSON model and validator | Medium | 3-5 days |
| Runtime engine and basic node executors | Medium-hard | 1-2 weeks |
| Event dispatch integration | Medium-hard | 1 week |
| Visual editor shell with React Flow | Medium | 1-2 weeks |
| Property inspector and node config schemas | Medium-hard | 1 week |
| Run history and test-run UX | Medium | 1 week |
| Polish, error handling, tests | Medium | 1-2 weeks |

Total MVP range: about 6-10 weeks for a solid internal prototype.

The highest-risk part is not the canvas. The highest-risk part is the runtime contract: event timing, validation, safe execution, and debugging when something goes wrong.

## Guard Rails

The editor should follow the same philosophy as the schema drift guard rails: metadata is powerful, so invalid metadata must fail early and clearly.

Required guard rails:

- Validate every workflow before enable.
- Store a workflow version with each run.
- Keep node type keys stable.
- Never execute workflows that reference missing objects or fields.
- Do not allow arbitrary code in node config.
- Keep before-event actions restricted to safe mutations and validation.
- Keep after-event actions from modifying the already-committed triggering record unless explicitly designed.
- Log every execution with enough context to reproduce failures.
- Disable workflows automatically only for repeated system failures if that policy is explicit.

## Testing Strategy

### Unit Tests

- Graph validator accepts valid graphs.
- Graph validator rejects cycles.
- Graph validator rejects missing trigger node.
- Graph validator rejects multiple trigger nodes.
- Node config validator catches missing required fields.
- Metadata reference validator catches deleted or renamed fields.
- Condition evaluator handles each supported operator.
- Template resolver handles missing fields safely.

### Runtime Tests

- `BeforeSave` workflow can set a field.
- `BeforeSave` workflow can reject a save.
- `Created` workflow runs after insert.
- `Updated` workflow receives old and new values.
- `FieldChanged` workflow runs only when the selected field changes.
- Failed after-event workflow records a failed run without rolling back the original save.

### Editor Tests

- User can create a workflow.
- User can drag a node to the canvas.
- User can connect compatible ports.
- Incompatible ports are rejected.
- Inspector renders schema-driven fields.
- Validation errors select or highlight the related node.

## Open Design Questions

- Should workflows be global, tenant-specific, or environment-specific?
- Should workflow definitions be editable only in development, or also in production?
- Should metadata changes automatically disable workflows that reference removed fields?
- Should after-event failures ever retry automatically?
- Should workflow run input/output store full record snapshots or redacted snapshots?
- Should before-save workflows run before or after built-in validation rules?
- Should workflow definitions be treated as data, source-controlled files, or both?

## Recommended First Prototype

The smallest useful prototype should avoid a full web editor at first.

Step 1:

- Define workflow JSON.
- Build validator.
- Build runtime.
- Run workflows from code using sample definitions.

Step 2:

- Add event dispatch from `Save()`.
- Prove `BeforeSave`, `Created`, and `FieldChanged`.

Step 3:

- Add the visual editor over the already-working model.

This order reduces risk because the runtime contract is the foundation. Once workflow definitions execute correctly, the canvas becomes a friendlier way to author those definitions.

## Success Criteria

The MVP is successful when:

- A non-developer can create a workflow for a metadata-backed object without writing code.
- The workflow can react to object lifecycle events.
- The workflow can branch on record values.
- The workflow can perform at least one meaningful action, such as creating a related record or rejecting a save.
- Invalid workflows cannot be enabled.
- Failed workflow runs are understandable from the UI.
- Developers can add a new predefined node type without changing the editor canvas itself.
