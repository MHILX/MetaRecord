using MetaRecord.Data;
using MetaRecord.Models;
using MetaRecord.Services;

namespace MetaRecord.Web.Infrastructure;

internal static class MetaRecordInitialization
{
    public static async Task InitializeMetaRecordAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MetaRecordDbContext>();
        var entityStore = scope.ServiceProvider.GetRequiredService<EntityStore>();

        MetadataRegistry.Clear();
        await MetadataLoader.InitializeAsync(context, CreateSeedData());
        MetadataRegistry.LinkType<Product>("Product");
        entityStore.EnsureTableExists(Product.Metadata);
    }

    private static IEnumerable<IObjectMetadata> CreateSeedData() => new[]
    {
        new ObjectMetadata
        {
            Name = "Product",
            TableName = "Products",
            Properties = new[]
            {
                new PropertyMetadata("Id", "Id", typeof(Guid), true) { IsPrimaryKey = true },
                new PropertyMetadata("Name", "Name", typeof(string), true),
                new PropertyMetadata("Price", "Price", typeof(decimal), true),
                new PropertyMetadata("Quantity", "Quantity", typeof(int), false)
            }
        }
    };
}