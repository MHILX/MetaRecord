using MetaRecord.Data;
using MetaRecord.Models;
using MetaRecord.Services;

// ============================================================
// MetaRecord Demo: Active Record + Metadata-Driven Object Model
// ============================================================

await RunDemoAsync();

async Task RunDemoAsync()
{
    Console.WriteLine("=== MetaRecord Demo ===\n");

    // Step 1: Initialize metadata from SQLite database
    Console.WriteLine("0. METADATA INITIALIZATION (from SQLite)");
    await InitializeMetadataAsync();
    Console.WriteLine();

    // Step 2: Demonstrate Active Record pattern - objects save themselves
    Console.WriteLine("1. ACTIVE RECORD PATTERN");
    Console.WriteLine("   Creating and saving a product...");

    var product = new Product { Name = "Widget", Price = 9.99m, Quantity = 100 };
    product.Save();  // Active Record: object knows how to persist itself

    Console.WriteLine($"   Created: {product.Name} @ ${product.Price}\n");

    // Step 3: Demonstrate metadata-driven introspection
    Console.WriteLine("2. METADATA-DRIVEN OBJECT MODEL");
    Console.WriteLine("   Querying object metadata at runtime...\n");

    var metadata = Product.Metadata;
    Console.WriteLine($"   Object: {metadata.Name}");
    Console.WriteLine($"   Table:  {metadata.TableName}");
    Console.WriteLine("   Properties:");
    foreach (var prop in metadata.Properties)
        Console.WriteLine($"     - {prop.Name} ({prop.ClrType.Name}) {(prop.IsRequired ? "[Required]" : "")}");

    Console.WriteLine();

    // Step 4: Demonstrate find and update
    Console.WriteLine("3. FIND AND UPDATE");
    var found = Product.Find(product.Id);
    if (found != null)
    {
        Console.WriteLine($"   Found product: {found.Name} @ ${found.Price}");
        found.Price = 12.99m;
        found.Save();
        Console.WriteLine($"   Updated price: ${found.Price}\n");
    }

    // Step 5: Query all products (persisted in SQLite)
    Console.WriteLine("4. QUERY ALL (from SQLite)");
    var allProducts = Product.All();
    Console.WriteLine($"   Total products in database: {allProducts.Count}");

    // Step 6: Show all registered metadata from database
    Console.WriteLine("\n5. ALL REGISTERED METADATA (from database)");
    foreach (var objMeta in MetadataRegistry.GetAll())
    {
        Console.WriteLine($"   - {objMeta.Name} -> {objMeta.TableName} ({objMeta.Properties.Count} properties)");
    }

    Console.WriteLine("\n=== Demo Complete ===");
}

// ============================================================
// Metadata Initialization - Loads from SQLite Database
// ============================================================
async Task InitializeMetadataAsync()
{
    // Define seed data (will only be inserted if DB is empty)
    var seedData = new[]
    {
        new ObjectMetadata
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Product",
            TableName = "Products",
            Properties = new[]
            {
                new PropertyMetadata("Id", "Id", typeof(Guid), true) { IsPrimaryKey = true },
                new PropertyMetadata("Name", "Name", typeof(string), true) { MaxLength = 100, IsUnique = true, Caption = "Product Name" },
                new PropertyMetadata("Price", "Price", typeof(decimal), true) { Caption = "Unit Price" },
                new PropertyMetadata("Quantity", "Quantity", typeof(int), false) { DefaultValue = "0" }
            }
        }
    };

    // Initialize metadata database and load metadata
    using var metaContext = new MetaRecordDbContext();
    await MetadataLoader.InitializeAsync(metaContext, seedData);

    // Link CLR types to metadata loaded from database
    MetadataRegistry.LinkType<Product>("Product");

    // Create entity tables based on metadata
    var store = EntityStore.Current;
    store.EnsureTableExists(Product.Metadata);
    Console.WriteLine($"  [DATA] Entity tables initialized");
}