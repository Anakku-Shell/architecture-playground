using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shop.Modular.BuildingBlocks.Infrastructure.Persistence;

namespace Shop.Modular.Catalog.Data;

/// <summary>A product row. CRUD style: public setters, no behaviour; the rules are in <c>ProductRules</c>.</summary>
internal sealed class Product
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Sku { get; set; }

    public decimal Price { get; set; }

    public int Stock { get; set; }
}

/// <summary>
/// The Catalog module's own DbContext, mapped to its own schema (<c>catalog</c>). No other module's tables
/// are in it, and no other module may query these. Guide: §7.2, "The database schemas".
/// </summary>
internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("products");
            product.HasKey(p => p.Id);
            product.Property(p => p.Id).ValueGeneratedNever();
            product.Property(p => p.Name).HasMaxLength(200);
            product.Property(p => p.Sku).HasMaxLength(50);
            product.HasIndex(p => p.Sku).IsUnique();
            product.Property(p => p.Price).HasPrecision(18, 2);
        });
    }
}

/// <summary>Lets <c>dotnet ef</c> create this context without starting the Host.</summary>
internal sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) =>
        new(DatabaseServiceCollectionExtensions.DesignTimeOptions<CatalogDbContext>(CatalogDbContext.Schema));
}
