# Schema Drift in Metadata-Driven Systems

In a metadata-driven object model, an entity's "shape" lives in three places at once. Keeping them in sync is the central operational risk of the pattern. This document catalogs how they drift in MetaRecord, why each scenario is dangerous, and the guard rails that close each gap.

## The three shapes

```
   ┌────────────────────────┐     ┌────────────────────────┐     ┌────────────────────────┐
   │ 1. Metadata rows       │     │ 2. Entity table        │     │ 3. CLR class           │
   │ ObjectDefinitions      │     │    e.g. Products       │     │    e.g. Product.cs     │
   │ PropertyDefinitions    │     │                        │     │                        │
   │ Name, ColumnName, Type │     │ Column, SQL type, NULL │     │ Properties + types     │
   └────────────────────────┘     └────────────────────────┘     └────────────────────────┘
            ▲                                ▲                              ▲
            │   loaded by MetadataLoader     │  created by EnsureTableExists│  used by reflection
            │   into MetadataRegistry        │  using metadata as input     │  inside EntityStore
            └────────────────────────────────┴──────────────────────────────┘
                                  All three must stay in sync.
```

The `meta.*` table names used below are the conceptual EF Core schema names. SQLite does not implement schemas, so local database tools may show these tables without a schema prefix; the drift risk is the same either way.

These are coupled only by **convention** (matching names) and a **one-time bootstrap** (`EnsureTableExists` runs once at startup). Any change to one not mirrored in the others is *drift*, and most failure modes are silent until runtime.

## Why drift is especially insidious here

- **No build-time check.** All three sides meet at stringly-typed boundaries. A C# refactor that renames a property compiles even when metadata still references the old name.
- **Silent reflection failures.** `typeof(T).GetProperty(name)` returning `null` is treated as "skip this property" rather than an error.
- **Bootstrap-only DDL.** `EnsureTableExists` creates tables but never reconciles them. Once the table exists, additions to metadata are invisible to the schema.
- **Constraints aren't validated before persistence.** `IsRequired` shapes the DDL, `MaxLength` is only advisory in SQLite, and neither is checked inside `Insert` / `Update` before SQL runs.
- **No transaction across the three layers.** Editing metadata, altering the table, and shipping CLR code are independent steps with no atomic gate.
- **Metadata names become SQL.** Table names, column names, and default expressions are interpolated into generated SQL. Values are parameterized; identifiers and default fragments are not.

## Drift scenarios

### 1. Metadata and CLR class edited → entity table is now stale

Adding a `PropertyDefinitions` row for `Sku TEXT NOT NULL` and adding `Product.Sku` doesn't change an existing `Products` table because [`EntityStore.EnsureTableExists`](../Data/EntityStore.cs) emits `CREATE TABLE IF NOT EXISTS` and never `ALTER`s. If metadata changes without the CLR property, reflection silently omits it instead (scenario 2).

```text
INSERT INTO Products (Id, Name, Price, Quantity, Sku) VALUES (...)
       ↑
       SqliteException: no such column: Sku
```

Once the CLR property exists, inserts crash because generated SQL references a column the table never received. Selects can also fail when `MapToEntity` asks the reader for the missing `Sku` column; if the CLR property is still missing, reads look fine while silently missing the field.

### 2. Metadata edited → CLR class is now stale

A `Description` property is added to metadata but not to `Product.cs`.

```csharp
typeof(Product).GetProperty("Description"); // null
```

`EntityStore` silently drops the column on save and on read. No exception, no warning. If the DB column exists because the table was freshly created or manually migrated, it accumulates `NULL`s while the application acts as if `Description` doesn't exist.

### 3. CLR class edited → metadata is now stale

Renaming `Product.Quantity` to `Product.StockOnHand` in code without updating the metadata row leaves `GetProperty("Quantity") == null`. Saves quietly omit the value; reads quietly leave `StockOnHand = 0`. Compiles fine, runs fine, **loses data**.

### 4. CLR type vs. metadata `DataType` mismatch

Metadata says `Decimal`; the CLR property is `double`. On read, `EntityStore.ConvertValue` returns a `decimal`, and reflection then tries to assign that value to a `double` property. Depending on the path, this can throw at runtime or introduce rounding behavior. Even when metadata and CLR both say `Decimal`, `EntityStore.GetSqliteType` maps it to SQLite `REAL`, so exact monetary precision is not guaranteed by the current storage layer.

### 5. Entity table altered out-of-band → metadata is now stale

A DBA hand-edits `Products` to add `CHECK (Price > 0)`. Metadata doesn't know. The next `EnsureTableExists` is a no-op (table exists), and inserts that violate the check fail at runtime with no helpful diagnostic from the metadata layer.

### 6. Type-name string typos

Metadata stores types as strings. [`MetadataRepository.GetClrType`](../Services/MetadataRepository.cs) falls back to `typeof(string)` for anything it doesn't recognize:

```csharp
"Boolean " => …  // trailing space → fall-through → typeof(string)
"int"      => …  // wrong case → fall-through → typeof(string)
```

A typo silently rewrites the type in runtime metadata. `EntityStore.GetSqliteType` then treats the property as `TEXT` during table creation/reconciliation, and reads may later try to assign string-shaped values into non-string CLR properties.

### 7. Multi-tenant / multi-environment skew

Because metadata is data, different environments can hold different metadata. Promote code from staging to prod and the runtime expects properties that the prod metadata table never gained → reflection mismatches in production only.

### 8. Multi-process editing without invalidation

Two processes hold their `MetadataRegistry` cache. One edits the metadata tables. The other doesn't notice — the `MetadataVersion` table exists, but nothing currently polls it. Versioning only works if every edit path also bumps the version; out-of-band SQL edits need triggers or disciplined tooling. Even within one process, `ActiveRecord<T>.Metadata` caches metadata per CLR type, so refreshing `MetadataRegistry` alone would not update entity types that already resolved their metadata.

### 9. Metadata removed or renamed → table keeps orphaned data

Removing `Quantity` from metadata, or renaming it to `StockOnHand`, does not remove or rename the `Quantity` column in `Products`. The old column can keep accumulating historical data that no runtime path reads, while new metadata points elsewhere. Dropping or renaming columns needs an explicit migration, not a bootstrap check.

### 10. Required / optional semantics drift

Metadata can say a property is optional while the CLR property is a non-nullable value type. If the database contains `NULL` for `Quantity`, `MapToEntity` skips assignment and the CLR property remains `0`. The application can no longer distinguish "missing" from "zero".

### 11. Primary key metadata drifts from hard-coded `Id`

Metadata can mark any property as `IsPrimaryKey`, and `EnsureTableExists` will emit that property as the primary key. But `ActiveRecord<T>` always has a `Guid Id`, and `Update`, `Delete`, and `Find` all use `WHERE Id = @Id`. A non-`Id` primary key makes DDL and CRUD disagree.

### 12. Precision / scale metadata is ignored

`PropertyDefinitionEntity` has `Precision` and `Scale`, but they are not copied into runtime `PropertyMetadata` and are not used by `EntityStore.GetSqliteType`. Numeric metadata can therefore claim constraints the database never enforces.

### 13. Metadata contains unsafe SQL identifiers or fragments

Entity values are parameterized, but `TableName`, `ColumnName`, and `DefaultValue` are concatenated into generated SQL. Bad metadata can produce invalid SQL, and user-editable metadata can become a SQL injection surface unless identifiers and default expressions are validated or quoted.

## Mapping scenarios → guard rails

| # | Scenario | Primary guard rail |
|---|---|---|
| 1 | Metadata/CLR add a column the table lacks | **G1** Reconcile, don't just create |
| 2 | Metadata adds a property the CLR class lacks | **G2** Boot-time contract test |
| 3 | CLR property renamed; metadata stale | **G2** Boot-time contract test |
| 4 | CLR type ≠ metadata `DataType` | **G2** Boot-time contract test |
| 5 | Table altered out-of-band | **G1** Reconcile + **G6** Single source of truth |
| 6 | Type-name typo silently degraded | **G3** Strongly type `DataType` |
| 7 | Environment-specific metadata skew | **G6** Single source of truth |
| 8 | Stale metadata caches across/within processes | **G5** Honor `MetadataVersion` |
| 9 | Metadata removed/renamed but table keeps data | **G1** Reconcile + **G7** Schema lock |
| 10 | Required/optional semantics drift | **G2** Boot-time contract test + **G4** Validate before persistence |
| 11 | Primary key metadata disagrees with hard-coded `Id` | **G2** Boot-time contract test + **G7** Schema lock |
| 12 | Precision/scale metadata ignored | **G2** Boot-time contract test + **G4** Validate before persistence |
| 13 | Unsafe SQL identifiers/default fragments | **G10** Validate identifiers and SQL fragments |

## The guard rails in detail

### G1. Reconcile, don't just create

Replace `CREATE TABLE IF NOT EXISTS` with a routine that introspects the existing table (`PRAGMA table_info(...)` on SQLite, `INFORMATION_SCHEMA` elsewhere) and:

- `ALTER TABLE … ADD COLUMN` for properties present in metadata but missing in the table.
- Report columns present in the table but missing from metadata as orphaned. Do not silently drop them; require a migration for drop/rename operations.
- **Refuse to start** if a column type disagrees with metadata, or if a metadata-declared `PRIMARY KEY` doesn't match the table's PK. Type/PK changes need a real data migration.

### G2. Boot-time contract test

Right after metadata is loaded and CLR types are linked, walk every `IObjectMetadata` and assert:

- Every `PropertyMetadata.Name` resolves to a `PropertyInfo` on the linked CLR type.
- `PropertyInfo.PropertyType` is assignable from `PropertyMetadata.ClrType`.
- CLR nullability/default behavior agrees with metadata `IsRequired` where the platform can inspect it.
- The primary key declared by metadata agrees with the identity convention used by `ActiveRecord<T>` and `EntityStore`.
- The entity table contains every column metadata declares, with a compatible SQLite affinity.

Surface failures as a single `MetadataDriftException` listing **everything** that disagrees, on **which side**, with **suggested fixes**. One method, milliseconds at startup, catches scenarios 2 / 3 / 4 / 10 / 11 / 12.

### G3. Strongly type `DataType`

Replace the free-form `DataType` string column with an enum (`PropertyDataType`) and constrain it via a `CHECK` constraint or a lookup table. Eliminates the typo class entirely (scenario 6) and gives `MetadataRepository.GetClrType` a closed mapping.

### G4. Validate before persistence

Move `IsRequired` / `MaxLength` / type / precision checks into `EntityStore.Insert` and `Update` so a stale schema (G1 hasn't run, or has been bypassed) cannot let an invalid value reach the table. This matters even with a fresh SQLite schema because length and numeric precision are not enforced by SQLite affinity alone.

### G5. Honor `MetadataVersion`

Have running processes:

- Read `MetadataVersion` periodically (or on each request batch).
- Require every metadata edit path to bump `MetadataVersion`, or add database triggers for out-of-band edits.
- Refresh the registry and per-type `ActiveRecord<T>` metadata caches, or fail loud, when the version changes underneath them.
- Reject **mutations** to entities whose metadata version no longer matches the cached one.

This addresses scenario 8, supports multi-environment safety in scenario 7, and finally pays for the cost of having that table.

### G6. Single source of truth per environment

Pick **one direction** and enforce it:

- **Metadata as code.** Commit metadata as JSON / migration scripts in source control; apply via the deploy pipeline. Hand-edits are forbidden and detected.
- **Code from metadata.** Generate the CLR classes from `ObjectDefinitions` at build time; the C# you compile is always derived from the metadata you ship.

Either choice removes the third independent editing surface and reduces the problem to two-way sync (G1 + G2).

### G7. Schema lock for unrecoverable invariants

Some changes cannot be reconciled by `ALTER TABLE`:

- Primary key changed
- Column removed or renamed
- Column type narrowed (e.g. `TEXT` → `INTEGER`)
- Required column added with no default to existing rows

Detect these at startup and **refuse to proceed**, pointing the operator at a migration runbook. Better a hard crash than partial data corruption.

### G8. Concurrency-aware mutations

Once `MetadataVersion` is honored (G5), entity rows should also carry a `RowVersion` column so optimistic concurrency catches scenario 8's data-level twin: two processes saving the same entity with different schemas.

### G9. Diagnostic logging

Every silent fall-through is a future incident. At minimum:

- Log a warning when a metadata property has no matching CLR property (or vice versa).
- Log when `GetClrType` falls into the default branch (typo / unknown type).
- Log when `EnsureTableExists` finds an existing table whose columns disagree with metadata.

Logs alone don't prevent drift, but they make it visible long before users do.

### G10. Validate identifiers and SQL fragments

Treat metadata that affects generated SQL as code, not plain text:

- Restrict `TableName` and `ColumnName` to a safe identifier format, or quote them with provider-aware APIs.
- Store defaults as validated provider-specific expressions, not arbitrary strings.
- Reject unsupported provider features such as precision/scale constraints that the current `EntityStore` cannot enforce.

## Suggested order of adoption

```
1.  G10 Validate identifiers/fragments ← do this from day one
2.  G2  Boot-time contract test        ← cheapest, catches the most
3.  G3  Strongly type DataType         ← prevents an entire failure class
4.  G1  Reconcile DDL                  ← unlocks safe metadata edits
5.  G4  Validate before persistence    ← belt-and-braces for G1
6.  G5  Honor MetadataVersion          ← cache and multi-process safety
7.  G6  Single source of truth         ← organizational, not just code
8.  G7  Schema lock                    ← when you have real data to lose
9.  G8  Row-level concurrency          ← when multiple writers exist
10. G9  Diagnostic logging             ← keep failures visible
```

## Bottom line

Schema drift is the **central operational risk** of any metadata-driven system. The freedom that makes the pattern powerful — *"the shape is just data"* — is also what lets the three views diverge.

Production-grade implementations spend a meaningful slice of their code on **convergence checks** and metadata input hardening: contract tests at startup, reconciliation at deploy, explicit failure when the layers disagree, safe SQL generation from metadata, and clear logs when the layers almost disagree. MetaRecord ships none of these by default — that's appropriate for a teaching demo, but is the first thing to add before any production use.

For the broader list of demo limitations, see the [README](../README.md#limitations). For how this all fits with the Active Record pattern, see [ActiveRecord-vs-Repository.md](ActiveRecord-vs-Repository.md).
