# MetaRecord

A demonstration of combining the **Active Record pattern** with a **metadata-driven object modeling system** in C#, featuring **database-backed metadata storage** using Entity Framework Core and SQLite.

## Overview

MetaRecord showcases two powerful enterprise patterns working together:

### Active Record Pattern

The Active Record pattern wraps a database row in a domain object, combining data access with business logic. Each entity knows how to:

- **Save itself** to the database (`Save()`)
- **Delete itself** from the database (`Delete()`)
- **Find** instances by ID (`Find(id)`)
- **Query** all instances (`All()`)

```csharp
var product = new Product { Name = "Widget", Price = 9.99m };
product.Save();  // Entity persists itself - no separate repository needed
```

### Metadata-Driven Object Model

The metadata system provides runtime introspection of entity structures. **Metadata is stored in SQLite** and loaded at application startup. This enables:

- **Dynamic UI generation** - Forms built from property metadata
- **Validation rules** - Required fields, data types
- **Runtime configurability** - Modify object definitions without recompiling
- **Schema evolution** - Track metadata versions

```csharp
var metadata = Product.Metadata;
Console.WriteLine($"Table: {metadata.TableName}");
foreach (var prop in metadata.Properties)
    Console.WriteLine($"  {prop.Name}: {prop.ClrType.Name}");
```

## Concepts

### What is the Active Record pattern?

Active Record, cataloged by Martin Fowler in *Patterns of Enterprise Application Architecture*, is an object that **wraps a single row in a database table and carries both its data and the behavior needed to persist itself**. One class owns the schema shape, the domain logic, and the CRUD operations — so `product.Save()` is all a caller needs.

It is most useful when the domain model and the table layout line up closely (CRUD-heavy apps, admin tools, prototypes). The trade-off is tight coupling between domain logic and persistence: entities are harder to unit-test in isolation, and complex domains often outgrow it and migrate toward **Repository** + **Data Mapper** patterns. MetaRecord implements the "classic" form via the generic `ActiveRecord<T>` base class.

### What does "metadata-driven object modeling" mean?

In a typical ORM, an entity's shape is fixed at compile time — via class definitions, attributes, or fluent configuration. A **metadata-driven** model flips that: the shape (tables, columns, types, constraints) lives as **data** that the runtime reads and acts on. The same engine can host any number of entities without code changes, and the set of entities can grow or change while the app is running.

In MetaRecord, `IObjectMetadata` and `PropertyMetadata` describe each entity. `EntityStore` builds `CREATE TABLE`, `INSERT`, `UPDATE`, and `SELECT` statements from that description using reflection to read values off the CLR instance. Swap the metadata and you get a different table — no recompile.

### Why store metadata in a database?

Metadata could live in code (attributes), config files (JSON/YAML), or a database. Putting it in a database adds capabilities that matter for long-lived systems:

- **Runtime editability** — an admin UI or a migration script can add a property to `Product` while the app is running; the next `EnsureTableExists` call will alter the schema.
- **Versioning & change detection** — the `MetadataVersion` table lets clients cheaply detect "has anything changed since I last loaded?" and refresh caches.
- **Multi-tenant variation** — different tenants (or environments) can ship different object definitions without branching code.
- **Auditable history** — because metadata is just rows, it can be queried, diffed, and backed up with normal database tooling.

The cost is a bootstrapping step (`MetadataLoader`) and an indirection — metadata has to be loaded before any entity code can run.

### How the three pieces fit together

```
Product.Save()
   └─► ActiveRecord<Product>.Save()
          └─► asks MetadataRegistry for Product's shape (IObjectMetadata)
                 └─► EntityStore uses that shape + reflection
                        └─► emits parameterized SQL against metarecord.db
```

`MetadataRegistry` is the in-memory cache populated at startup by `MetadataLoader`, which in turn reads from the `meta.ObjectDefinitions` / `meta.PropertyDefinitions` tables via `MetadataRepository`. Nothing in `ActiveRecord<T>` or `EntityStore` hard-codes the `Product` shape — it's all read from metadata.

## Project Structure

```
MetaRecord/
├── Program.cs                    # Demo application
├── metarecord.db                 # SQLite database (created at runtime)
├── Data/
│   ├── MetaRecordDbContext.cs    # EF Core context for metadata
│   ├── EntityStore.cs            # Metadata-driven entity persistence
│   ├── ObjectDefinitionEntity.cs # DB entity for object metadata
│   ├── PropertyDefinitionEntity.cs # DB entity for property metadata
│   └── MetadataVersionEntity.cs  # Version tracking
├── Models/
│   ├── ActiveRecord.cs           # Base class for all entities
│   ├── IObjectMetadata.cs        # Metadata contracts and types
│   ├── MetadataRegistry.cs       # Central metadata repository
│   └── Product.cs                # Example entity
├── Services/
│   ├── MetadataLoader.cs         # Startup initialization
│   └── MetadataRepository.cs     # DB access for metadata
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
│   │   Product   │ ───► │ MetadataRegistry │ (runtime cache) │
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

## Database Schema

All data is stored in `metarecord.db` (SQLite):

### Metadata Tables (EF Core managed)

| Table | Purpose |
|-------|---------|
| `meta.ObjectDefinitions` | Object/entity definitions |
| `meta.PropertyDefinitions` | Property definitions for each object |
| `meta.MetadataVersion` | Version tracking for change detection |

### Entity Tables (Metadata-driven)

Entity tables are created dynamically based on metadata definitions. For example, the `Product` metadata creates:

```sql
CREATE TABLE Products (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Price REAL NOT NULL,
    Quantity INTEGER
)
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later

## How to Run

1. **Clone the repository**
   ```bash
   git clone <repository-url>
   cd MetaRecord
   ```

2. **Build the project**
   ```bash
   dotnet build
   ```

3. **Run the demo**
   ```bash
   dotnet run
   ```

The first run creates `metarecord.db` and seeds metadata. Subsequent runs load from the database.

## Expected Output

```
=== MetaRecord Demo ===

0. METADATA INITIALIZATION (from SQLite)
  [META] Initializing metadata system...
  [META] Database: C:\...\MetaRecord\metarecord.db
  [META] Seeded 1 object definitions to database   <- First run only
  [META] Loaded 1 object definitions (version 1)
  [DATA] Entity tables initialized

1. ACTIVE RECORD PATTERN
   Creating and saving a product...
  [DB] Inserted Product with Id=...
   Created: Widget @ $9.99

2. METADATA-DRIVEN OBJECT MODEL
   Querying object metadata at runtime...

   Object: Product
   Table:  Products
   Properties:
     - Id (Guid) [Required]
     - Name (String) [Required]
     - Price (Decimal) [Required]
     - Quantity (Int32)

3. FIND AND UPDATE
   Found product: Widget @ $9.99
  [DB] Updated Product with Id=...
   Updated price: $12.99

4. QUERY ALL (from SQLite)
   Total products in database: 1   <- Increases each run!

5. ALL REGISTERED METADATA (from database)
   - Product -> Products (4 properties)

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
       // existing Product metadata...
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
- **No transactions, no unit-of-work** — every `Save()` opens its own connection and commits immediately.
- **No concurrency control** — last writer wins; no row versions or optimistic locks.
- **Limited type mapping** — the CLR↔SQLite converter covers common primitives only; enums, `byte[]`, nullable value types, navigation properties, and collections are not supported.
- **No relationships** — foreign keys, associations, and eager/lazy loading are out of scope.
- **No validation pipeline** — `IsRequired`/`MaxLength` shape the schema but are not enforced before insert/update.
- **Global state** — `EntityStore.Current` and `MetadataRegistry` are static, which complicates testing and multi-tenant isolation.

## License

MIT