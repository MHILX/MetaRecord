# Active Record vs. Repository

Both patterns answer the same question — *"where does the code that moves data between objects and the database live?"* — but they answer it differently. Both were cataloged by Martin Fowler in *Patterns of Enterprise Application Architecture* (PoEAA). Choosing between them is a design decision driven by **domain complexity** and how much you value **decoupling the model from persistence**.

## The two patterns in one sentence each

- **Active Record** — The entity *is* a row: it carries its own data **and** the methods to load, save, and delete itself.
- **Repository** — The entity is a plain object; a separate **repository** class (often paired with a **Data Mapper** and a **Unit of Work**) handles persistence.

## Side-by-side comparison

| Dimension | Active Record | Repository (+ Data Mapper) |
|---|---|---|
| Where is persistence code? | On the entity | In a separate class |
| What does the entity know? | Its data *and* how to save itself | Only its data (POCO) |
| Typical call site | `product.Save()` | `repo.Add(product); uow.SaveChanges();` |
| Coupling of domain to DB | Tight | Loose (persistence-ignorant domain) |
| Unit-testing domain logic | Harder (needs DB or heavy mocks) | Easier (pure objects) |
| Boilerplate | Low | Higher (repo, mapper, UoW) |
| Schema changes | Usually touch one class | Can ripple across repo + mapper + entity |
| Best-fit domain complexity | Low–moderate (CRUD-shaped) | Moderate–high (rich behavior, invariants) |
| Canonical examples | Rails ActiveRecord, CakePHP, Django models (loosely), this repo | Java JPA/Hibernate, .NET with EF Core + repository, NHibernate |

## Code: the same task, both ways

### Active Record (as in this repo)

```csharp
// Create + save
var p = new Product { Name = "Widget", Price = 9.99m };
p.Save();

// Read + update
var found = Product.Find(p.Id);
found!.Price = 12.99m;
found.Save();

// Delete + list
found.Delete();
var all = Product.All();
```

The `Product` class **inherits** `Save`, `Delete`, `Find`, `All` from `ActiveRecord<T>`. No repository object exists.

### Repository + Data Mapper (EF Core style)

```csharp
// The entity is a POCO — no persistence methods
public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

// A dedicated repository class owns persistence
public interface IProductRepository
{
    Task<Product?> FindAsync(Guid id);
    Task<IReadOnlyList<Product>> AllAsync();
    void Add(Product product);
    void Remove(Product product);
}

// Usage
_repo.Add(new Product { Name = "Widget", Price = 9.99m });
await _uow.SaveChangesAsync();

var found = await _repo.FindAsync(id);
found!.Price = 12.99m;
await _uow.SaveChangesAsync();  // change tracker writes the update

_repo.Remove(found);
await _uow.SaveChangesAsync();
```

The entity is passive; an **outsider** (the repository, coordinated by a unit of work) moves data in and out of the database.

## When to use which

### Use Active Record when…

- The domain maps closely to tables (each class ≈ one table).
- The work is mostly CRUD (admin screens, dashboards, internal tools).
- Time-to-first-demo matters (prototypes, hackathons, line-of-business apps).
- The team is small and comfortable with persistence concerns living on entities.
- You want low ceremony and a small amount of code per entity.

**Representative use cases**

- Internal admin portals, CMS back-ends, customer-support tooling.
- Rapid prototyping and proof-of-concept work.
- Metadata-/data-driven generators where the entity shape *is* the storage shape (this repo's use case).
- Scripts and batch jobs that read/mutate rows one-at-a-time.

### Use Repository (+ Data Mapper) when…

- The domain is complex, with invariants that must hold across multiple entities/aggregates.
- You're doing Domain-Driven Design: ubiquitous language, aggregates, domain events.
- Persistence-ignorance is a goal (unit tests shouldn't need a DB).
- You may swap storage — SQL ↔ NoSQL, in-memory tests, multiple databases.
- You need specialized query shapes (reporting, CQRS read models) without bloating the entity.
- Multiple teams consume the domain and shouldn't need to know schema details.

**Representative use cases**

- Financial, healthcare, or e-commerce core domains with rich business rules.
- Systems implementing DDD or CQRS.
- Polyglot persistence (e.g., write model in SQL, read model in a search index).
- Shared domain libraries used by multiple applications or bounded contexts.
- Any system where testability of the domain model is a first-class concern.

## Hybrid reality

These aren't airtight boxes. Real codebases often mix them:

- Rails is Active Record, but teams add **Query Objects** and **Service Objects** for complex reads and workflows.
- EF Core is technically Data Mapper, but `DbSet<T>` + `SaveChanges()` *feels* close to Active Record and many projects use it that way.
- Aggregates may use Active-Record-like convenience methods while the boundary to the outside world is still a repository interface.

Treat the patterns as **defaults and intentions**, not laws.

## Quick decision guide

```
Is the domain mostly CRUD, with classes mapping 1:1 to tables?
        │
   yes  │  no
   ┌────┴─────┐
   ▼          ▼
Active       Do you need persistence-ignorance,
Record       rich domain behavior, or swappable storage?
             │
        yes  │  no
        ┌────┴─────┐
        ▼          ▼
     Repository   Either works — pick the one
     + Data       your team knows best.
     Mapper
```

## How MetaRecord relates

MetaRecord is a deliberate Active Record demo — every entity inherits `ActiveRecord<T>` and persists itself via `Save()`/`Delete()`. What makes it unusual is that the *shape* each entity saves is read from **database-backed metadata**, so a single Active Record base can serve many entity types without hand-written classes per table. See the main [README.md](../README.md) for the full architecture.

## Further reading

- Martin Fowler, *Patterns of Enterprise Application Architecture* — chapters on Active Record, Data Mapper, Repository, and Unit of Work.
- Eric Evans, *Domain-Driven Design* — the canonical motivation for repositories over Active Record in complex domains.
- Vaughn Vernon, *Implementing Domain-Driven Design* — practical guidance on repositories and aggregates.
