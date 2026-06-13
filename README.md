# MetaRecord

A demonstration of combining the **Active Record pattern** with a **metadata-driven object modeling system** in C#, featuring **database-backed metadata storage**, a **low-code workflow runtime**, an **ASP.NET Core workflow API**, and a **React visual workflow editor**.

## Overview

MetaRecord showcases two powerful enterprise patterns working together:

### Active Record Pattern

The Active Record pattern wraps a database row in a domain object, combining data access with business logic. Each entity knows how to:

- **Save itself** to the database (`Save()`)
- **Delete itself** from the database (`Delete()`)
- **Find** instances by ID (`Find(id)`)
- **Query** all instances (`All()`)

```csharp
var todo = new Todo { Title = "Write docs", Status = "Open", Priority = 2 };
todo.Save();  // Entity persists itself - no separate repository needed
```

### Metadata-Driven Object Model

The metadata system provides runtime introspection of entity structures. **Metadata is stored in SQLite** and loaded at application startup. This enables:

- **Dynamic UI generation** - Forms built from property metadata
- **Validation rules** - Required fields, data types
- **Runtime configurability** - Modify object definitions without recompiling
- **Schema evolution** - Track metadata versions

```csharp
var metadata = Todo.Metadata;
Console.WriteLine($"Table: {metadata.TableName}");
foreach (var prop in metadata.Properties)
    Console.WriteLine($"  {prop.Name}: {prop.ClrType.Name}");
```

### Low-Code Workflow Runtime

MetaRecord now extends metadata from object shape to object behavior. Workflows are saved as graph definitions with predefined trigger, flow, and action nodes. They can be validated, enabled, executed from record lifecycle events, tested through the API, and inspected through run history.

Supported MVP workflow events include:

- `BeforeSave`
- `Created`
- `Updated`
- `FieldChanged`
- `Manual`

The visual editor is intentionally powered by the same backend node catalog and validator that the runtime uses, so editor assumptions stay aligned with execution behavior.

## Concepts

### What is the Active Record pattern?

Active Record, cataloged by Martin Fowler in *Patterns of Enterprise Application Architecture*, is an object that **wraps a single row in a database table and carries both its data and the behavior needed to persist itself**. One class owns the schema shape, the domain logic, and the CRUD operations — so `todo.Save()` is all a caller needs.

It is most useful when the domain model and the table layout line up closely (CRUD-heavy apps, admin tools, prototypes). The trade-off is tight coupling between domain logic and persistence: entities are harder to unit-test in isolation, and complex domains often outgrow it and migrate toward **Repository** + **Data Mapper** patterns. MetaRecord implements the "classic" form via the generic `ActiveRecord<T>` base class.

> For a deeper comparison with the Repository pattern, including side-by-side code and use cases, see [docs/ActiveRecord-vs-Repository.md](docs/ActiveRecord-vs-Repository.md).

### What does "metadata-driven object modeling" mean?

In a typical ORM, an entity's shape is fixed at compile time — via class definitions, attributes, or fluent configuration. A **metadata-driven** model flips that: the shape (tables, columns, types, constraints) lives as **data** that the runtime reads and acts on. The same engine can host any number of entities without code changes, and the set of entities can grow or change while the app is running.

In MetaRecord, `IObjectMetadata` and `PropertyMetadata` describe each entity. `EntityStore` builds `CREATE TABLE`, `INSERT`, `UPDATE`, and `SELECT` statements from that description using reflection to read values off the CLR instance. Swap the metadata and you get a different table — no recompile.

### Why store metadata in a database?

Metadata could live in code (attributes), config files (JSON/YAML), or a database. Putting it in a database adds capabilities that matter for long-lived systems:

- **Runtime editability** — an admin UI or a migration script can edit metadata rows without recompiling. In this demo, `EnsureTableExists` only creates missing tables; existing tables still need an explicit migration or reconciliation step when metadata changes.
- **Versioning & change detection** — the `MetadataVersion` table lets clients cheaply detect "has anything changed since I last loaded?" if every metadata edit path bumps the version. This demo records version rows but does not poll them or refresh running caches.
- **Multi-tenant variation** — different tenants (or environments) can ship different object definitions without branching code.
- **Auditable history** — because metadata is just rows, it can be queried, diffed, and backed up with normal database tooling.

The cost is a bootstrapping step (`MetadataLoader`) and an indirection — metadata has to be loaded before any entity code can run. The bigger long-term cost is **schema drift** between the three independent shapes (metadata rows, entity table, CLR class); see [docs/Schema-Drift-and-Guard-Rails.md](docs/Schema-Drift-and-Guard-Rails.md) for failure modes and concrete guard rails.

### How the three pieces fit together

```
Todo.Save()
   └─► ActiveRecord<Todo>.Save()
          └─► asks MetadataRegistry for Todo's shape (IObjectMetadata)
                 └─► EntityStore uses that shape + reflection
                        └─► emits parameterized SQL against metarecord.db
```

`MetadataRegistry` is the in-memory cache populated at startup by `MetadataLoader`, which in turn reads from the `meta.ObjectDefinitions` / `meta.PropertyDefinitions` tables via `MetadataRepository`. Nothing in `ActiveRecord<T>` or `EntityStore` hard-codes the `Todo` shape — it's all read from metadata.

## Project Structure

```
MetaRecord/
├── docs/
│   ├── Low-Code-Workflow-Editor-MVP.md
│   └── Low-Code-Workflow-MVP-Implementation-Plan.md
├── src/
│   ├── MetaRecord.sln
│   ├── MetaRecord.csproj                 # Console demo + core runtime for current MVP stage
│   ├── Program.cs                        # Console proof for metadata + workflow runtime
│   ├── Data/                             # EF metadata store + dynamic entity persistence
│   ├── Models/                           # ActiveRecord, metadata contracts, Todo sample
│   ├── Services/                         # Metadata initialization/repository services
│   ├── Workflows/                        # Definition, catalog, validation, runtime, persistence
│   ├── MetaRecord.Web/                   # ASP.NET Core API for editor/runtime operations
│   └── MetaRecord.Editor/                # React/TypeScript visual workflow editor
├── tests/
│   └── MetaRecord.Core.Tests/            # xUnit tests for runtime, lifecycle, API, helpers
└── README.md
```

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Application Startup                     │
├─────────────────────────────────────────────────────────────┤
│  MetadataLoader.InitializeAsync()                           │
│    ├── EnsureCreatedAsync() (creates SQLite DB if needed)   │
│    ├── SeedIfEmptyAsync() (inserts initial metadata)        │
│    ├── LoadAllMetadataAsync() (reads from DB)               │
│    └── MetadataRegistry.RegisterByName() (in-memory cache)  │
│                                                             │
│  EntityStore.EnsureTableExists()                            │
│    └── Creates entity tables from metadata definitions      │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│   ┌─────────────┐      ┌──────────────────┐                 │
│   │     Todo    │ ───► │ MetadataRegistry │ (runtime cache) │
│   │  .Metadata  │      └────────┬─────────┘                 │
│   └─────────────┘               │                           │
│         │                       │ loaded from               │
│         │ Save()/Find()         ▼                           │
│         │                ┌──────────────┐                   │
│         └──────────────► │   SQLite DB  │                   │
│         (via EntityStore)│ metarecord.db│                   │
│                          └──────────────┘                   │
│                          (all data stored)                  │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

## Key Components

| Component | Purpose |
|-----------|---------|
| `ActiveRecord<T>` | Generic base class providing CRUD operations |
| `IObjectMetadata` | Interface describing an entity's structure |
| `MetadataRegistry` | In-memory cache for registered metadata |
| `MetadataRepository` | Loads/saves metadata from SQLite database |
| `MetadataLoader` | Initializes metadata system at startup |
| `EntityStore` | Metadata-driven SQL generation for entity persistence |
| `WorkflowNodeCatalog` | Code-defined trigger, flow, and action node catalog |
| `WorkflowValidator` | Server-side validation for graph shape, metadata references, ports, timing, and config |
| `WorkflowEngine` | Executes validated workflow graphs and records run history |
| `WorkflowRepository` | Persists workflow definitions, runs, and step details |
| `MetaRecord.Web` | Minimal API host for metadata, workflows, validation, test-run, and run history |
| `MetaRecord.Editor` | React Flow visual editor for authoring and testing workflows |

## Database Schema

All data is stored in `metarecord.db` (SQLite):

### Metadata Tables (EF Core managed)

The `meta.*` prefix below is the conceptual EF Core schema. SQLite does not implement schemas, so local database tools may display these tables without the prefix.

| Table | Purpose |
|-------|---------|
| `meta.ObjectDefinitions` | Object/entity definitions |
| `meta.PropertyDefinitions` | Property definitions for each object |
| `meta.MetadataVersion` | Version tracking for change detection |

### Entity Tables (Metadata-driven)

Entity tables are created dynamically based on metadata definitions. For example, the `Todo` metadata creates:

```sql
CREATE TABLE Todos (
   Id TEXT PRIMARY KEY,
   Title TEXT NOT NULL,
   Description TEXT,
   Status TEXT NOT NULL,
   Priority INTEGER
)
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- [Node.js](https://nodejs.org/) 20 LTS or later for the React workflow editor

## Quick Start

Run commands from the repository root unless a command says otherwise.

### Restore Dependencies

```powershell
dotnet restore src/MetaRecord.sln
npm --prefix src/MetaRecord.Editor install
```

### Build and Test Everything

```powershell
dotnet test src/MetaRecord.sln
npm --prefix src/MetaRecord.Editor run build
```

### Run the Console Proof

```powershell
dotnet run --project src/MetaRecord.csproj
```

The console demo initializes metadata, seeds sample workflows, saves valid and invalid todos, and prints workflow run results.

### Run the Low-Code Workflow MVP

Use the helper script for a one-command launch on Windows:

```powershell
.\Start-MetaRecordMvp.ps1
```

It starts the API in one PowerShell window and the visual editor in another, then opens the editor automatically. The API uses `http://127.0.0.1:5050` when available and falls back to the next free port if needed; the editor does the same for `5173`. Pass `-ApiPort 5000` if you want the original API port.

On startup, the web host seeds the demo metadata plus four demo workflows, so the editor opens with a complete sample workflow set instead of an empty canvas. The default editor selection is the richer "Capture todo snapshot" workflow.

If you are new to the editor, start with the step-by-step [Workflow Editor User Guide](docs/Workflow-Editor-User-Guide.md).

For a guided first-time tour of that workflow and every editor panel, see [Capture Todo Audit Snapshot Workflow](docs/Capture-Todo-Audit-Snapshot-Workflow.md).

Doc map:

- [Workflow Editor User Guide](docs/Workflow-Editor-User-Guide.md) - start here if you have not used the editor yet.
- [Capture Todo Audit Snapshot Workflow](docs/Capture-Todo-Audit-Snapshot-Workflow.md) - read next for one complete walkthrough.
- [Low-Code Workflow Editor MVP Plan](docs/Low-Code-Workflow-Editor-MVP.md) - read if you want the broader product and design context.

Start the API in one terminal:

```powershell
dotnet run --project src/MetaRecord.Web/MetaRecord.Web.csproj --urls http://localhost:5000
```

If this fails with `address already in use`, another API process is already listening on port `5000`. You can either use the already-running API, stop that terminal, or run the API on another port:

```powershell
dotnet run --project src/MetaRecord.Web/MetaRecord.Web.csproj --urls http://localhost:5050
```

Start the visual editor in a second terminal:

```powershell
npm --prefix src/MetaRecord.Editor run dev
```

Open the editor at:

```text
http://localhost:5173/
```

The editor dev server proxies `/api` calls to `http://localhost:5000` by default. If you run the API on a different URL, set `VITE_API_PROXY_TARGET` before starting the editor:

```powershell
$env:VITE_API_PROXY_TARGET = "http://localhost:5050"
npm --prefix src/MetaRecord.Editor run dev
```

### Useful Commands

```powershell
# Backend build only
dotnet build src/MetaRecord.sln

# Backend tests only
dotnet test src/MetaRecord.sln

# Editor production build only
npm --prefix src/MetaRecord.Editor run build

# Editor preview after a production build
npm --prefix src/MetaRecord.Editor run preview
```

The first API or console run creates a local SQLite `metarecord.db` and seeds metadata. Runtime database files are ignored by git.

## Expected Output

```
=== MetaRecord Demo ===

0. METADATA INITIALIZATION (from SQLite)
  [META] Initializing metadata system...
  [META] Database: C:\...\MetaRecord\metarecord.db
   [META] Seeded 2 object definitions to database   <- First run only
   [META] Loaded 2 object definitions (version 2)
  [DATA] Entity tables initialized

1. SEED WORKFLOW DEFINITIONS
   Enabled: Capture todo snapshot (Manual)
   Enabled: Reject empty todo title (BeforeSave)
   Enabled: Write log when todo is created (Created)
   Enabled: Write log when todo is completed (FieldChanged)

2. VALID SAVE: BeforeSave + Created
   Saved: Todo-... [Open] priority 3
   BeforeSave validation: Succeeded (... steps)
   Created log: Succeeded (... steps)

3. INVALID SAVE: BeforeSave rejection
   Rejected: Todo title is required for ...
   Todo persisted: False

4. UPDATE SAVE: FieldChanged
   Updated status: Done
   Completed log: Succeeded (... steps)

5. METADATA-DRIVEN OBJECT MODEL
   Object: Todo
   Table:  Todos

6. QUERY ALL (from SQLite)
   Total todos in database: ...

7. ALL REGISTERED METADATA (from database)
   - Todo -> Todos (5 properties)

=== Demo Complete ===
```

## Extending the Demo

### Adding a New Entity

1. Create a class inheriting from `ActiveRecord<T>`:
   ```csharp
   [Table("Customers")]
   public class Customer : ActiveRecord<Customer>
   {
       public string Name { get; set; } = "";
       public string Email { get; set; } = "";
   }
   ```

2. Add metadata to the seed data in `Program.cs`:
   ```csharp
   var seedData = new[]
   {
      // existing Todo metadata...
       new ObjectMetadata
       {
           Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
           Name = "Customer",
           TableName = "Customers",
           Properties = new[]
           {
               new PropertyMetadata("Id", "Id", typeof(Guid), true),
               new PropertyMetadata("Name", "Name", typeof(string), true),
               new PropertyMetadata("Email", "Email", typeof(string), true)
           }
       }
   };
   ```

3. Link the CLR type after initialization:
   ```csharp
   MetadataRegistry.LinkType<Customer>("Customer");
   ```

### Viewing the SQLite Database

You can inspect `metarecord.db` using any SQLite browser to see stored metadata.

## Use Cases

- **Rapid prototyping** - Quick domain modeling without ORM setup
- **Dynamic applications** - Runtime-configurable entities
- **Code generation** - Metadata-driven scaffolding
- **Admin interfaces** - Auto-generated CRUD screens
- **Multi-tenant systems** - Different metadata per tenant

## Limitations

This is a teaching demo, not a production framework. Known gaps:

- **Reflection on every call** — no compiled accessors or caching of `PropertyInfo` lookups.
- **No migrations** — `EnsureTableExists` only creates tables; it does not `ALTER` them when metadata changes.
- **No metadata cache invalidation** — `MetadataVersion` rows are written, but running processes do not poll them, and `ActiveRecord<T>` also caches metadata per CLR type.
- **No transactions, no unit-of-work** — every `Save()` opens its own connection and commits immediately.
- **No concurrency control** — last writer wins; no row versions or optimistic locks.
- **Limited type mapping** — the CLR↔SQLite converter covers common primitives only; enums, `byte[]`, nullable value types, navigation properties, and collections are not supported.
- **SQLite constraint caveats** — `MaxLength` is not enforced by SQLite, decimal values map to `REAL`, and precision/scale metadata is not honored.
- **No relationships** — foreign keys, associations, and eager/lazy loading are out of scope.
- **No validation pipeline** — `IsRequired`/`MaxLength` shape the schema but are not enforced before insert/update.
- **Global state** — `EntityStore.Current` and `MetadataRegistry` are static, which complicates testing and multi-tenant isolation.
- **Unvalidated metadata SQL identifiers** — table/column names and default expressions are interpolated from metadata; production code should validate or quote them before generating SQL.
- **No drift detection** — metadata, entity tables, and CLR classes can diverge silently. See [docs/Schema-Drift-and-Guard-Rails.md](docs/Schema-Drift-and-Guard-Rails.md) for the failure modes and recommended guard rails.

## License

MIT