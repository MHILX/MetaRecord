# Low-Code Workflow MVP Implementation Plan

This plan turns the low-code workflow editor MVP into an implementation sequence for the current MetaRecord repository.

The first goal is not the visual canvas. The first goal is a working workflow contract: definitions, validation, persistence, runtime execution, and lifecycle dispatch. Once that is stable, the API and React editor can be built on real behavior instead of mock graph data.

## Working Branch

Use this branch for the MVP planning and first implementation work:

```text
feature/low-code-workflow-mvp
```

## Current Baseline

The current repo has a single runnable .NET project under `src/`:

```text
src/
  MetaRecord.csproj
  MetaRecord.sln
  Program.cs
  Data/
  Models/
  Services/
```

Key existing pieces to reuse:

| Area | Existing Type | MVP Use |
|---|---|---|
| Runtime metadata | `MetadataRegistry` | Validate workflow object and property references. |
| Object shape | `IObjectMetadata`, `PropertyMetadata` | Drive object pickers, condition fields, and action field mappings. |
| Record persistence | `EntityStore` | Read and write metadata-backed records from workflow actions. |
| Lifecycle hook | `ActiveRecord<T>.Save()` | Dispatch before-save and after-save workflow events. |
| Metadata database | `MetaRecordDbContext` | Add workflow definition and run-history tables. |

## Implementation Principles

- Build the runtime before the editor.
- Keep workflow definition models separate from EF Core persistence entities.
- Keep node types code-defined for the MVP so runtime and editor schemas share one source of truth.
- Validate before saving, before enabling, and before execution.
- Avoid arbitrary user code in conditions, templates, and actions.
- Keep before-save behavior strict: validation failures and unexpected workflow failures should block the save.
- Keep after-save behavior non-blocking: failures should be recorded in run history without rolling back the original save.

## Milestone Overview

| Milestone | Outcome | Recommended Scope |
|---|---|---|
| 0. Repo hygiene | Clean branch foundation. | `.gitignore`, solution location decision, generated files out of source control. |
| 1. Definition model | Workflow JSON can round-trip. | Pure C# graph types under `src/Workflows/Definitions`. |
| 2. Node catalog | Predefined nodes have schemas and ports. | Catalog under `src/Workflows/Catalog`. |
| 3. Validator | Invalid graphs are rejected clearly. | Graph, metadata, timing, and config validation. |
| 4. Persistence | Workflows and runs are stored in SQLite. | EF entities and repository under `src/Workflows/Persistence`. |
| 5. Runtime engine | Valid workflows execute from code. | Engine, context, node executor pipeline. |
| 6. Dynamic data APIs | Actions can work without CLR types. | Dictionary-based `EntityStore` methods. |
| 7. Lifecycle dispatch | Saves trigger workflows. | `SaveAsync()`, before-save, created, updated, field-changed. |
| 8. Console proof | MVP behavior visible without a web UI. | Seed demo workflows and print run results. |
| 9. Tests | Runtime contract is protected. | Validator and engine unit tests. |
| 10. Web/API | Editor has backend endpoints. | ASP.NET Core host after runtime proof. |
| 11. Visual editor | Users can author workflows visually. | React Flow editor after API exists. |

## Milestone 0: Repo Hygiene

### Tasks

- Add or restore a root-level `.gitignore`.
- Ignore `bin/`, `obj/`, `.vs/`, `.vscode/`, `.idea/`, `*.user`, `*.suo`, and SQLite runtime database files.
- Decide whether the solution remains in `src/` for the first implementation or moves to the repo root.
- Do not mix generated build artifacts with workflow runtime changes.

### Acceptance Criteria

- `git status --short` shows source and docs changes, not build outputs.
- The repo can still build with the chosen solution path.

### Recommended Commands

```bash
dotnet build src/MetaRecord.sln
dotnet run --project src/MetaRecord.csproj
```

## Milestone 1: Workflow Definition Model

### Target Files

```text
src/Workflows/Definitions/
  WorkflowDefinition.cs
  WorkflowNode.cs
  WorkflowEdge.cs
  WorkflowPosition.cs
  WorkflowEventName.cs
  WorkflowRunStatus.cs
```

### Tasks

- Create immutable or mostly immutable definition types.
- Store node config as `JsonElement` for accurate JSON round-tripping.
- Represent canvas position separately from runtime behavior.
- Add stable event names: `BeforeSave`, `Created`, `Updated`, `FieldChanged`, `Manual`.
- Add run statuses: `Succeeded`, `Failed`, `Canceled`, `Skipped`.

### Acceptance Criteria

- A sample workflow definition can deserialize from JSON.
- The same definition can serialize back without losing nodes, edges, ports, or config.
- Definition models have no EF Core dependency.

## Milestone 2: Node Catalog

### Target Files

```text
src/Workflows/Catalog/
  WorkflowNodeType.cs
  WorkflowNodeCatalog.cs
  NodeConfigSchema.cs
  NodeConfigField.cs
  NodePortDefinition.cs
  WorkflowNodeCategory.cs
```

### Initial Node Types

| Type | Category | Timing | Executor |
|---|---|---|---|
| `trigger.record-created` | Trigger | After | None |
| `trigger.record-updated` | Trigger | After | None |
| `trigger.before-save` | Trigger | Before | None |
| `trigger.field-changed` | Trigger | After | None |
| `trigger.manual` | Trigger | Manual | None |
| `flow.condition` | Flow | Any | `ConditionNodeExecutor` |
| `flow.stop` | Flow | Any | `StopNodeExecutor` |
| `action.set-field` | Action | Before | `SetFieldNodeExecutor` |
| `action.reject-save` | Action | Before | `RejectSaveNodeExecutor` |
| `action.create-record` | Action | Any | `CreateRecordNodeExecutor` |
| `action.write-log` | Action | Any | `WriteLogNodeExecutor` |

### Tasks

- Define input and output ports per node type.
- Define config schema fields for each configurable node.
- Include field types such as object picker, property picker, literal value, condition builder, template text, and field mapping grid.
- Expose catalog lookup by node type key.

### Acceptance Criteria

- Unknown node type keys are rejected by lookup.
- Every non-trigger node has at least one input port.
- Trigger nodes have no input port and at least one output port.
- Before-only actions are explicitly marked in catalog metadata.

## Milestone 3: Workflow Validator

### Target Files

```text
src/Workflows/Validation/
  WorkflowValidator.cs
  WorkflowValidationIssue.cs
  WorkflowValidationSeverity.cs
```

### Tasks

- Validate the workflow target object exists in `MetadataRegistry`.
- Validate exactly one trigger node exists.
- Validate trigger node type matches `WorkflowDefinition.EventName`.
- Validate every node type exists in `WorkflowNodeCatalog`.
- Validate every edge references existing nodes.
- Validate source and target ports exist and are compatible.
- Validate the graph is acyclic.
- Validate every non-trigger node is reachable from the trigger.
- Validate node config fields against the node schema.
- Validate object and property references against metadata.
- Validate before-only actions are used only in before-save workflows.

### Acceptance Criteria

- Validator returns all discovered issues, not only the first issue.
- Validation issue includes severity, message, optional node id, and optional config field path.
- Invalid metadata references are caught before execution.
- Cycles and disconnected nodes are caught before execution.

## Milestone 4: Workflow Persistence

### Target Files

```text
src/Workflows/Persistence/
  WorkflowDefinitionEntity.cs
  WorkflowRunEntity.cs
  WorkflowRunStepEntity.cs
  WorkflowRepository.cs
```

### MetaRecordDbContext Changes

Add DbSets:

```csharp
public DbSet<WorkflowDefinitionEntity> WorkflowDefinitions => Set<WorkflowDefinitionEntity>();
public DbSet<WorkflowRunEntity> WorkflowRuns => Set<WorkflowRunEntity>();
public DbSet<WorkflowRunStepEntity> WorkflowRunSteps => Set<WorkflowRunStepEntity>();
```

### Tasks

- Store workflow definitions as JSON plus queryable columns: `ObjectName`, `EventName`, `IsEnabled`, and `Version`.
- Store workflow run summaries.
- Store step-level execution details.
- Add repository methods for create, update, get by id, list, enable, disable, and find enabled workflows by object/event.
- Serialize workflow definitions using `System.Text.Json`.

### Acceptance Criteria

- A workflow definition can be saved and loaded intact.
- Enabled workflows can be queried by object name and event name.
- A run can be saved with step records.
- Definition version executed is captured on every run.

## Milestone 5: Runtime Engine

### Target Files

```text
src/Workflows/Runtime/
  WorkflowEvent.cs
  WorkflowExecutionContext.cs
  WorkflowEngine.cs
  WorkflowRunResult.cs
  NodeExecutionResult.cs
  WorkflowExecutionSignal.cs
  IWorkflowEngine.cs
  IWorkflowNodeExecutor.cs
  IWorkflowEventDispatcher.cs
```

### Tasks

- Build an execution context from the incoming event.
- Start execution at the trigger node.
- Traverse outgoing edges by named output port.
- Execute every reachable node at most once per run.
- Stop a branch when a node fails, cancels, or returns no selected output ports.
- Persist run and step history through `WorkflowRepository`.
- Keep execution single-process and synchronous from the caller perspective for the MVP, even if APIs are async.

### Acceptance Criteria

- A valid workflow can run from code without a UI.
- Condition nodes select `true` or `false` outgoing ports.
- Failed nodes record the node id, node type, and error message.
- Before-save rejection is represented distinctly from unexpected failure.

## Milestone 6: Node Executors

### Target Files

```text
src/Workflows/Runtime/Executors/
  ConditionNodeExecutor.cs
  StopNodeExecutor.cs
  SetFieldNodeExecutor.cs
  RejectSaveNodeExecutor.cs
  CreateRecordNodeExecutor.cs
  WriteLogNodeExecutor.cs
```

### Tasks

- Implement structured condition evaluation.
- Implement template resolution for values such as `{{currentRecord.Name}}`.
- Implement `Set Field` by mutating the execution context current record.
- Implement `Reject Save` by returning a rejection signal and message.
- Implement `Create Record` using metadata and dictionary values.
- Implement `Write Log` as run output first; move to a dedicated table only if needed.

### Acceptance Criteria

- Condition supports equals, not equals, numeric comparisons, contains, starts with, ends with, is empty, and is not empty.
- Missing template fields produce a clear validation or runtime error.
- `Set Field` only works in before-save workflows.
- `Reject Save` only works in before-save workflows.
- `Create Record` can create a metadata-backed record without a CLR type.

## Milestone 7: EntityStore Dynamic Value APIs

### Target File

```text
src/Data/EntityStore.cs
```

### New Methods

```csharp
public Dictionary<string, object?> FindValues(IObjectMetadata metadata, Guid id);
public void InsertValues(IObjectMetadata metadata, IReadOnlyDictionary<string, object?> values);
public void UpdateValues(IObjectMetadata metadata, Guid id, IReadOnlyDictionary<string, object?> values);
```

### Tasks

- Reuse existing metadata-to-SQL conversion behavior.
- Convert SQLite values back to CLR-compatible values using property metadata.
- Keep SQL parameterized.
- Treat primary key and `Id` consistently with existing generic methods.

### Acceptance Criteria

- Workflow actions can read the current record by metadata and id.
- Workflow actions can create a record from field mappings.
- Workflow actions can update a metadata-backed record without a CLR type.
- Existing generic Active Record behavior still works.

## Milestone 8: ActiveRecord Lifecycle Dispatch

### Target File

```text
src/Models/ActiveRecord.cs
```

### Tasks

- Add `SaveAsync()` as the primary lifecycle method.
- Keep `Save()` as a compatibility wrapper for the console demo.
- Load original values before update.
- Build a before-save workflow event.
- Apply workflow-modified current values before persistence.
- Dispatch created, updated, and field-changed events after persistence.
- Make before-save workflow rejections block persistence with a clear exception type.

### Acceptance Criteria

- `BeforeSave` can reject invalid data before insert or update.
- `BeforeSave` can set field defaults before persistence.
- `Created` runs after insert.
- `Updated` runs after update.
- `FieldChanged` runs only when the configured field changed.

## Milestone 9: Console Proof

### Target File

```text
src/Program.cs
```

### Demo Workflows

- Reject product save when `Price <= 0`.
- Write a log when a product is created.
- Write a log when `Quantity` changes below `10`.

### Tasks

- Seed workflow definitions after metadata initialization.
- Enable seeded demo workflows.
- Save a valid product and print workflow run output.
- Attempt to save an invalid product and print the rejection.
- Update quantity and print field-changed run output.

### Acceptance Criteria

- The console demo proves workflow execution without browser code.
- Rejection is visible and understandable.
- Run history is written for successful and failed workflows.

## Milestone 10: Test Project

### Target Structure

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

### First Tests

- Validator rejects missing object references.
- Validator rejects missing property references.
- Validator rejects cycles.
- Validator rejects disconnected nodes.
- Before-save workflow can reject a save.
- Field-changed workflow fires only for the configured field.
- Runtime records failed node id and message.

### Acceptance Criteria

- `dotnet test` runs from the solution.
- Validator and runtime have focused tests before the visual editor starts.

## Milestone 11: Web API

Start this after the console proof works.

### Target Structure

```text
src/MetaRecord.Web/
  Program.cs
  Endpoints/
    MetadataEndpoints.cs
    WorkflowEndpoints.cs
    WorkflowRunEndpoints.cs
```

### Endpoints

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/metadata/objects` | List object metadata. |
| `GET` | `/api/metadata/objects/{name}` | Get one object definition. |
| `GET` | `/api/workflow-node-types` | List predefined node types and schemas. |
| `GET` | `/api/workflows` | List workflow definitions. |
| `POST` | `/api/workflows` | Create workflow. |
| `GET` | `/api/workflows/{id}` | Load workflow. |
| `PUT` | `/api/workflows/{id}` | Save workflow. |
| `POST` | `/api/workflows/{id}/validate` | Validate workflow. |
| `POST` | `/api/workflows/{id}/enable` | Enable workflow after validation. |
| `POST` | `/api/workflows/{id}/disable` | Disable workflow. |
| `POST` | `/api/workflows/{id}/test-run` | Run with sample input. |
| `GET` | `/api/workflows/{id}/runs` | List runs for a workflow. |
| `GET` | `/api/workflow-runs/{runId}` | Get run and step details. |

### Acceptance Criteria

- API exposes the same node catalog the backend runtime uses.
- Save and enable paths both run server-side validation.
- Test-run works without enabling a workflow.

## Milestone 12: Visual Editor

Start this after the API exposes real workflow definitions and validation.

### Target Structure

```text
src/MetaRecord.Editor/
  package.json
  src/
    api/
    workflow/
      WorkflowCanvas.tsx
      NodePalette.tsx
      PropertyInspector.tsx
      ValidationPanel.tsx
      RunHistoryPanel.tsx
```

### Tasks

- Use React Flow for canvas editing.
- Load node types from `/api/workflow-node-types`.
- Render configuration forms from node config schemas.
- Load metadata object and property pickers from the API.
- Save, validate, enable, disable, and test-run from the editor.
- Highlight validation issues by node id and config field path.

### Acceptance Criteria

- A user can create a workflow without editing JSON.
- Incompatible edges are rejected or shown as invalid.
- Validation errors map back to the relevant node and inspector field.
- Run history is visible for enabled workflows.

## First Implementation Slice

The first coding slice should be intentionally small:

1. Repo hygiene.
2. Workflow definition models.
3. Hard-coded node catalog.
4. Validator for graph shape, node types, ports, metadata references, and timing rules.
5. One sample workflow JSON validated from the console or a unit test.

This slice proves the definition contract before any runtime side effects are introduced.

## MVP Definition of Done

The MVP is complete when all of these are true:

- A workflow definition can be authored visually, saved, validated, enabled, and disabled.
- Enabled workflows run from record lifecycle events.
- `BeforeSave` workflows can set fields and reject saves.
- `Created`, `Updated`, and `FieldChanged` workflows run after persistence.
- `Condition`, `Set Field`, `Reject Save`, `Create Record`, and `Write Log` nodes work.
- Invalid workflows cannot be enabled.
- Run history shows workflow status, failed node, step output, and error message.
- The runtime has tests for validation, graph execution, condition evaluation, template resolution, and lifecycle dispatch.

## Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Workflow definitions drift from editor assumptions. | Use one backend node catalog that also feeds the editor. |
| Metadata changes break enabled workflows. | Validate on enable and before execution; later add metadata version checks. |
| Before-save workflows create inconsistent data. | Restrict before-save actions to safe mutations and rejection. |
| After-save workflow failures confuse users. | Persist run history with node-level errors and keep original save committed. |
| Dynamic record writes bypass type safety. | Use metadata validation, parameterized SQL, and focused tests for dictionary APIs. |
| The UI ships before runtime semantics are stable. | Require console proof and API validation before starting the visual editor. |

## Decisions Before Coding

- Keep the solution under `src/` for the first slice, or move to a root solution now?
- Use `JsonElement` or dictionaries for node config in definition models?
- Use `EnsureCreated` for workflow tables during the MVP, or introduce EF migrations now?
- Should workflow run input/output store full snapshots or only selected fields?
- Should before-save unexpected failures block by default in every environment?